using System;
using UnityEngine;
using Seb.GPUSorting;
using Unity.Mathematics;
using System.Collections.Generic;
using Seb.Helpers;
using static Seb.Helpers.ComputeHelper;
using Seb.Fluid.Collision;

namespace Seb.Fluid.Simulation
{
	public enum DensityBakeMode
	{
		FastParticleSplat,
		AccurateSPHPerVoxel
	}

	public enum SimulationPerformanceProfile
	{
		Custom,
		Laptop,
		Balanced,
		HighQuality
	}

	public enum FluidMaterialPreset
	{
		Custom,
		Water,
		Paint,
		ThickPaint
	}

	public class FluidSim : MonoBehaviour
	{
		public event Action<FluidSim> SimulationInitCompleted;

		[Header("Performance")]
		public SimulationPerformanceProfile performanceProfile = SimulationPerformanceProfile.Laptop;
		public FluidMaterialPreset materialPreset = FluidMaterialPreset.Paint;
		public DensityBakeMode densityBakeMode = DensityBakeMode.FastParticleSplat;
		[Min(1)] public int densityTextureUpdateInterval = 1;
		[Tooltip("Small fixed prediction horizon improves SPH stability at large timesteps.")]
		public float predictionTimestep = 1f / 120f;
		[Tooltip("Scale applied when splatting particle density into the 3D texture.")]
		public float splatDensityScale = 1f;

		[Header("Time Step")] public float normalTimeScale = 1;
		public float slowTimeScale = 0.1f;
		public float maxTimestepFPS = 60; // if time-step dips lower than this fps, simulation will run slower (set to 0 to disable)
		public int iterationsPerFrame = 3;

		[Header("Simulation Settings")] public float gravity = -10;
		public float smoothingRadius = 0.2f;
		public float targetDensity = 630;
		public float pressureMultiplier = 288;
		public float nearPressureMultiplier = 2.15f;
		public float viscosityStrength = 0;
		[Range(0, 1)] public float collisionDamping = 0.95f;

        [Header("Collision")]
        public bool useMeshCollisions = false;

        [Header("Foam Settings")] public bool foamActive;
		public int maxFoamParticleCount = 1000;
		public float trappedAirSpawnRate = 70;
		public float spawnRateFadeInTime = 0.5f;
		public float spawnRateFadeStartTime = 0;
		public Vector2 trappedAirVelocityMinMax = new(5, 25);
		public Vector2 foamKineticEnergyMinMax = new(15, 80);
		public float bubbleBuoyancy = 1.5f;
		public int sprayClassifyMaxNeighbours = 5;
		public int bubbleClassifyMinNeighbours = 15;
		public float bubbleScale = 0.5f;
		public float bubbleChangeScaleSpeed = 7;

		[Header("Volumetric Render Settings")] public bool renderToTex3D;
		public int densityTextureRes;

		[Header("References")] public ComputeShader compute;
		public Spawner3D spawner;
        public MeshCollisionBuilder meshCollisionBuilder;

        [HideInInspector] public RenderTexture DensityMap;
		public Vector3 Scale => transform.localScale;

		// Buffers
		public ComputeBuffer foamBuffer { get; private set; }
		public ComputeBuffer foamSortTargetBuffer { get; private set; }
		public ComputeBuffer foamCountBuffer { get; private set; }
		public ComputeBuffer positionBuffer { get; private set; }
		public ComputeBuffer velocityBuffer { get; private set; }
		public ComputeBuffer densityBuffer { get; private set; }
		public ComputeBuffer predictedPositionsBuffer;
		public ComputeBuffer debugBuffer { get; private set; }

		ComputeBuffer sortTarget_positionBuffer;
		ComputeBuffer sortTarget_velocityBuffer;
		ComputeBuffer sortTarget_predictedPositionsBuffer;

		// Kernel IDs
		const int externalForcesKernel = 0;
		const int spatialHashKernel = 1;
		const int reorderKernel = 2;
		const int reorderCopybackKernel = 3;
		const int densityKernel = 4;
		const int pressureKernel = 5;
		const int viscosityKernel = 6;
		const int updatePositionsKernel = 7;
		const int renderKernel = 8;
		const int foamUpdateKernel = 9;
		const int foamReorderCopyBackKernel = 10;
		const int clearDensityKernel = 11;
		const int splatDensityKernel = 12;

		SpatialHash spatialHash;

		// State
		bool isPaused;
		bool pauseNextFrame;
		float smoothRadiusOld;
		float simTimer;
		bool inSlowMode;
		Spawner3D.SpawnData spawnData;
		Dictionary<ComputeBuffer, string> bufferNameLookup;
		int densityMapFrameCounter;
		Vector3Int cachedDensityMapSize;

		void Start()
		{
			Debug.Log("Controls: Space = Play/Pause, Q = SlowMode, R = Reset");
			isPaused = false;

			Initialize();
		}

		void Initialize()
		{
			ApplyPerformanceProfile();
			ApplyMaterialPreset();

			spawnData = spawner.GetSpawnData();
			int numParticles = spawnData.points.Length;

			spatialHash = new SpatialHash(numParticles);
			
			// Create buffers
			positionBuffer = CreateStructuredBuffer<float3>(numParticles);
			predictedPositionsBuffer = CreateStructuredBuffer<float3>(numParticles);
			velocityBuffer = CreateStructuredBuffer<float3>(numParticles);
			densityBuffer = CreateStructuredBuffer<float2>(numParticles);
			foamBuffer = CreateStructuredBuffer<FoamParticle>(maxFoamParticleCount);
			foamSortTargetBuffer = CreateStructuredBuffer<FoamParticle>(maxFoamParticleCount);
			foamCountBuffer = CreateStructuredBuffer<uint>(4096);
			debugBuffer = CreateStructuredBuffer<float3>(numParticles);

			sortTarget_positionBuffer = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_predictedPositionsBuffer = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_velocityBuffer = CreateStructuredBuffer<float3>(numParticles);

			bufferNameLookup = new Dictionary<ComputeBuffer, string>
			{
				{ positionBuffer, "Positions" },
				{ predictedPositionsBuffer, "PredictedPositions" },
				{ velocityBuffer, "Velocities" },
				{ densityBuffer, "Densities" },
				{ spatialHash.SpatialKeys, "SpatialKeys" },
				{ spatialHash.SpatialOffsets, "SpatialOffsets" },
				{ spatialHash.SpatialIndices, "SortedIndices" },
				{ sortTarget_positionBuffer, "SortTarget_Positions" },
				{ sortTarget_predictedPositionsBuffer, "SortTarget_PredictedPositions" },
				{ sortTarget_velocityBuffer, "SortTarget_Velocities" },
				{ foamCountBuffer, "WhiteParticleCounters" },
				{ foamBuffer, "WhiteParticles" },
				{ foamSortTargetBuffer, "WhiteParticlesCompacted" },
				{ debugBuffer, "Debug" }
			};

            if (meshCollisionBuilder != null) {
                meshCollisionBuilder.Build();

                if (meshCollisionBuilder.TriangleBuffer != null) {
                    bufferNameLookup.Add(meshCollisionBuilder.TriangleBuffer, "CollisionTriangles");
                }

                if (meshCollisionBuilder.ObjectBuffer != null) {
                    bufferNameLookup.Add(meshCollisionBuilder.ObjectBuffer, "CollisionObjects");
                }
            }

            // Set buffer data
            SetInitialBufferData(spawnData);

			// External forces kernel
			SetBuffers(compute, externalForcesKernel, bufferNameLookup, new ComputeBuffer[]
			{
				positionBuffer,
				predictedPositionsBuffer,
				velocityBuffer
			});

			// Spatial hash kernel
			SetBuffers(compute, spatialHashKernel, bufferNameLookup, new ComputeBuffer[]
			{
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets,
				predictedPositionsBuffer,
				spatialHash.SpatialIndices
			});

			// Reorder kernel
			SetBuffers(compute, reorderKernel, bufferNameLookup, new ComputeBuffer[]
			{
				positionBuffer,
				sortTarget_positionBuffer,
				predictedPositionsBuffer,
				sortTarget_predictedPositionsBuffer,
				velocityBuffer,
				sortTarget_velocityBuffer,
				spatialHash.SpatialIndices
			});

			// Reorder copyback kernel
			SetBuffers(compute, reorderCopybackKernel, bufferNameLookup, new ComputeBuffer[]
			{
				positionBuffer,
				sortTarget_positionBuffer,
				predictedPositionsBuffer,
				sortTarget_predictedPositionsBuffer,
				velocityBuffer,
				sortTarget_velocityBuffer,
				spatialHash.SpatialIndices
			});

			// Density kernel
			SetBuffers(compute, densityKernel, bufferNameLookup, new ComputeBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets
			});

			// Pressure kernel
			SetBuffers(compute, pressureKernel, bufferNameLookup, new ComputeBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				velocityBuffer,
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets,
				foamBuffer,
				foamCountBuffer,
				debugBuffer
			});

			// Viscosity kernel
			SetBuffers(compute, viscosityKernel, bufferNameLookup, new ComputeBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				velocityBuffer,
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets
			});

            // Update positions kernel
            if (meshCollisionBuilder != null &&
                meshCollisionBuilder.TriangleBuffer != null &&
                meshCollisionBuilder.ObjectBuffer != null) {
                SetBuffers(compute, updatePositionsKernel, bufferNameLookup, new ComputeBuffer[]
                {
					positionBuffer,
					velocityBuffer,
					meshCollisionBuilder.TriangleBuffer,
					meshCollisionBuilder.ObjectBuffer
                });
            } else {
                SetBuffers(compute, updatePositionsKernel, bufferNameLookup, new ComputeBuffer[]
                {
					positionBuffer,
					velocityBuffer
                });
            }

            // Render to 3d tex kernel
            SetBuffers(compute, renderKernel, bufferNameLookup, new ComputeBuffer[]
			{
				predictedPositionsBuffer,
				densityBuffer,
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets,
			});

			SetBuffers(compute, clearDensityKernel, bufferNameLookup, new ComputeBuffer[] { });

			SetBuffers(compute, splatDensityKernel, bufferNameLookup, new ComputeBuffer[]
			{
				positionBuffer,
			});

			// Foam update kernel
			SetBuffers(compute, foamUpdateKernel, bufferNameLookup, new ComputeBuffer[]
			{
				foamBuffer,
				foamCountBuffer,
				predictedPositionsBuffer,
				densityBuffer,
				velocityBuffer,
				spatialHash.SpatialKeys,
				spatialHash.SpatialOffsets,
				foamSortTargetBuffer,
				//debugBuffer
			});


			// Foam reorder copyback kernel
			SetBuffers(compute, foamReorderCopyBackKernel, bufferNameLookup, new ComputeBuffer[]
			{
				foamBuffer,
				foamSortTargetBuffer,
				foamCountBuffer,
			});

			compute.SetInt("numParticles", positionBuffer.count);
			compute.SetInt("MaxWhiteParticleCount", maxFoamParticleCount);

			UpdateSmoothingConstants();

			// Run single frame of sim with deltaTime = 0 to initialize density texture
			// (so that display can work even if paused at start)
			if (renderToTex3D)
			{
				RunSimulationFrame(0, forceDensityUpdate: true);
			}

			SimulationInitCompleted?.Invoke(this);
		}

		void Update()
		{
			// Run simulation
			if (!isPaused)
			{
				float maxDeltaTime = maxTimestepFPS > 0 ? 1 / maxTimestepFPS : float.PositiveInfinity; // If framerate dips too low, run the simulation slower than real-time
				float dt = Mathf.Min(Time.deltaTime * ActiveTimeScale, maxDeltaTime);
				RunSimulationFrame(dt);
			}

			if (pauseNextFrame)
			{
				isPaused = true;
				pauseNextFrame = false;
			}

			HandleInput();
		}

		void RunSimulationFrame(float frameDeltaTime, bool forceDensityUpdate = false)
		{
			float subStepDeltaTime = frameDeltaTime / iterationsPerFrame;
			UpdateSettings(subStepDeltaTime, frameDeltaTime);

			// Simulation sub-steps
			for (int i = 0; i < iterationsPerFrame; i++)
			{
				simTimer += subStepDeltaTime;
				RunSimulationStep();
			}

			// Foam and spray particles
			if (foamActive)
			{
				Dispatch(compute, maxFoamParticleCount, kernelIndex: foamUpdateKernel);
				Dispatch(compute, maxFoamParticleCount, kernelIndex: foamReorderCopyBackKernel);
			}

			// 3D density map
			if (renderToTex3D && (forceDensityUpdate || ShouldUpdateDensityMap()))
			{
				UpdateDensityMap();
			}
		}

		bool ShouldUpdateDensityMap()
		{
			densityMapFrameCounter++;
			if (densityMapFrameCounter >= densityTextureUpdateInterval)
			{
				densityMapFrameCounter = 0;
				return true;
			}

			return false;
		}

		void UpdateDensityMap()
		{
			EnsureDensityMapAllocated();

			compute.SetTexture(clearDensityKernel, "DensityMap", DensityMap);
			compute.SetTexture(renderKernel, "DensityMap", DensityMap);
			compute.SetTexture(splatDensityKernel, "DensityMap", DensityMap);
			compute.SetInts("densityMapSize", cachedDensityMapSize.x, cachedDensityMapSize.y, cachedDensityMapSize.z);
			compute.SetFloat("splatDensityScale", splatDensityScale);

			if (densityBakeMode == DensityBakeMode.FastParticleSplat)
			{
				Dispatch(compute, cachedDensityMapSize.x, cachedDensityMapSize.y, cachedDensityMapSize.z, clearDensityKernel);
				Dispatch(compute, positionBuffer.count, kernelIndex: splatDensityKernel);
			}
			else
			{
				Dispatch(compute, cachedDensityMapSize.x, cachedDensityMapSize.y, cachedDensityMapSize.z, renderKernel);
			}
		}

		void EnsureDensityMapAllocated()
		{
			float maxAxis = Mathf.Max(transform.localScale.x, transform.localScale.y, transform.localScale.z);
			int w = Mathf.Max(8, Mathf.RoundToInt(transform.localScale.x / maxAxis * densityTextureRes));
			int h = Mathf.Max(8, Mathf.RoundToInt(transform.localScale.y / maxAxis * densityTextureRes));
			int d = Mathf.Max(8, Mathf.RoundToInt(transform.localScale.z / maxAxis * densityTextureRes));
			cachedDensityMapSize = new Vector3Int(w, h, d);
			CreateRenderTexture3D(ref DensityMap, w, h, d, UnityEngine.Experimental.Rendering.GraphicsFormat.R16_SFloat, TextureWrapMode.Clamp);
		}

		void RunSimulationStep()
		{
			Dispatch(compute, positionBuffer.count, kernelIndex: externalForcesKernel);

			Dispatch(compute, positionBuffer.count, kernelIndex: spatialHashKernel);
			spatialHash.Run();
			
			Dispatch(compute, positionBuffer.count, kernelIndex: reorderKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: reorderCopybackKernel);

			Dispatch(compute, positionBuffer.count, kernelIndex: densityKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: pressureKernel);
			if (viscosityStrength != 0) Dispatch(compute, positionBuffer.count, kernelIndex: viscosityKernel);
			Dispatch(compute, positionBuffer.count, kernelIndex: updatePositionsKernel);
		}

		void UpdateSmoothingConstants()
		{
			float r = smoothingRadius;
			float spikyPow2 = 15 / (2 * Mathf.PI * Mathf.Pow(r, 5));
			float spikyPow3 = 15 / (Mathf.PI * Mathf.Pow(r, 6));
			float spikyPow2Grad = 15 / (Mathf.PI * Mathf.Pow(r, 5));
			float spikyPow3Grad = 45 / (Mathf.PI * Mathf.Pow(r, 6));

			compute.SetFloat("K_SpikyPow2", spikyPow2);
			compute.SetFloat("K_SpikyPow3", spikyPow3);
			compute.SetFloat("K_SpikyPow2Grad", spikyPow2Grad);
			compute.SetFloat("K_SpikyPow3Grad", spikyPow3Grad);
		}

		void UpdateSettings(float stepDeltaTime, float frameDeltaTime)
		{
			if (smoothingRadius != smoothRadiusOld)
			{
				smoothRadiusOld = smoothingRadius;
				UpdateSmoothingConstants();
			}

			Vector3 simBoundsSize = transform.localScale;
			Vector3 simBoundsCentre = transform.position;

            if (meshCollisionBuilder != null) {
                meshCollisionBuilder.UpdateDynamicObjects();
            }

            compute.SetInt("collisionTriangleCount", meshCollisionBuilder != null ? meshCollisionBuilder.TriangleCount : 0);
            compute.SetInt("collisionObjectCount", meshCollisionBuilder != null ? meshCollisionBuilder.ObjectCount : 0);

            compute.SetFloat("deltaTime", stepDeltaTime);
			compute.SetFloat("whiteParticleDeltaTime", frameDeltaTime);
			compute.SetFloat("simTime", simTimer);
			compute.SetFloat("gravity", gravity);
			compute.SetFloat("collisionDamping", collisionDamping);
            compute.SetInt("useMeshCollisions", useMeshCollisions ? 1 : 0);
            compute.SetFloat("smoothingRadius", smoothingRadius);
			compute.SetFloat("targetDensity", targetDensity);
			compute.SetFloat("pressureMultiplier", pressureMultiplier);
			compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
			compute.SetFloat("viscosityStrength", viscosityStrength);
			compute.SetFloat("predictionTimestep", predictionTimestep);
			compute.SetInt("foamEnabled", foamActive ? 1 : 0);
			compute.SetVector("boundsSize", simBoundsSize);
			compute.SetVector("centre", simBoundsCentre);

			compute.SetMatrix("localToWorld", transform.localToWorldMatrix);
			compute.SetMatrix("worldToLocal", transform.worldToLocalMatrix);

			// Foam settings
			float fadeInT = (spawnRateFadeInTime <= 0) ? 1 : Mathf.Clamp01((simTimer - spawnRateFadeStartTime) / spawnRateFadeInTime);
			compute.SetVector("trappedAirParams", new Vector3(trappedAirSpawnRate * fadeInT * fadeInT, trappedAirVelocityMinMax.x, trappedAirVelocityMinMax.y));
			compute.SetVector("kineticEnergyParams", foamKineticEnergyMinMax);
			compute.SetFloat("bubbleBuoyancy", bubbleBuoyancy);
			compute.SetInt("sprayClassifyMaxNeighbours", sprayClassifyMaxNeighbours);
			compute.SetInt("bubbleClassifyMinNeighbours", bubbleClassifyMinNeighbours);
			compute.SetFloat("bubbleScaleChangeSpeed", bubbleChangeScaleSpeed);
			compute.SetFloat("bubbleScale", bubbleScale);
		}

		void SetInitialBufferData(Spawner3D.SpawnData spawnData)
		{
			positionBuffer.SetData(spawnData.points);
			predictedPositionsBuffer.SetData(spawnData.points);
			velocityBuffer.SetData(spawnData.velocities);

			foamBuffer.SetData(new FoamParticle[foamBuffer.count]);

			debugBuffer.SetData(new float3[debugBuffer.count]);
			foamCountBuffer.SetData(new uint[foamCountBuffer.count]);
			simTimer = 0;
		}

		void HandleInput()
		{
			if (Input.GetKeyDown(KeyCode.Space))
			{
				isPaused = !isPaused;
			}

			if (Input.GetKeyDown(KeyCode.RightArrow))
			{
				isPaused = false;
				pauseNextFrame = true;
			}

			if (Input.GetKeyDown(KeyCode.R))
			{
				pauseNextFrame = true;
				SetInitialBufferData(spawnData);
				// Run single frame of sim with deltaTime = 0 to initialize density texture
				// (so that display can work even if paused at start)
				if (renderToTex3D)
				{
					RunSimulationFrame(0, forceDensityUpdate: true);
				}
			}

			if (Input.GetKeyDown(KeyCode.Q))
			{
				inSlowMode = !inSlowMode;
			}
		}

		private float ActiveTimeScale => inSlowMode ? slowTimeScale : normalTimeScale;

		public void ApplyPerformanceProfile()
		{
			switch (performanceProfile)
			{
				case SimulationPerformanceProfile.Laptop:
					densityBakeMode = DensityBakeMode.FastParticleSplat;
					densityTextureRes = 96;
					densityTextureUpdateInterval = 1;
					iterationsPerFrame = 2;
					maxTimestepFPS = 60;
					splatDensityScale = 1.15f;
					break;
				case SimulationPerformanceProfile.Balanced:
					densityBakeMode = DensityBakeMode.FastParticleSplat;
					densityTextureRes = 128;
					densityTextureUpdateInterval = 1;
					iterationsPerFrame = 3;
					maxTimestepFPS = 60;
					splatDensityScale = 1f;
					break;
				case SimulationPerformanceProfile.HighQuality:
					densityBakeMode = DensityBakeMode.AccurateSPHPerVoxel;
					densityTextureRes = 150;
					densityTextureUpdateInterval = 1;
					iterationsPerFrame = 3;
					maxTimestepFPS = 0;
					splatDensityScale = 1f;
					break;
			}
		}

		public void ApplyMaterialPreset()
		{
			switch (materialPreset)
			{
				case FluidMaterialPreset.Water:
					gravity = -10f;
					smoothingRadius = 0.2f;
					targetDensity = 630f;
					pressureMultiplier = 288f;
					nearPressureMultiplier = 2.15f;
					viscosityStrength = 0.001f;
					collisionDamping = 0.95f;
					break;
				case FluidMaterialPreset.Paint:
					gravity = -9.81f;
					smoothingRadius = 0.18f;
					targetDensity = 700f;
					pressureMultiplier = 340f;
					nearPressureMultiplier = 2.6f;
					viscosityStrength = 0.018f;
					collisionDamping = 0.88f;
					splatDensityScale = 1.2f;
					break;
				case FluidMaterialPreset.ThickPaint:
					gravity = -9.81f;
					smoothingRadius = 0.16f;
					targetDensity = 780f;
					pressureMultiplier = 420f;
					nearPressureMultiplier = 3f;
					viscosityStrength = 0.045f;
					collisionDamping = 0.82f;
					splatDensityScale = 1.35f;
					break;
			}
		}

		public int ParticleCount => positionBuffer != null ? positionBuffer.count : 0;

		void OnValidate()
		{
			if (performanceProfile != SimulationPerformanceProfile.Custom)
				ApplyPerformanceProfile();
			if (materialPreset != FluidMaterialPreset.Custom)
				ApplyMaterialPreset();
		}

        void OnDestroy() {
            foreach (var kvp in bufferNameLookup) {
                if (meshCollisionBuilder != null &&
                    (kvp.Key == meshCollisionBuilder.TriangleBuffer ||
                     kvp.Key == meshCollisionBuilder.ObjectBuffer)) {
                    continue;
                }

                Release(kvp.Key);
            }

            spatialHash.Release();
        }


        public struct FoamParticle
		{
			public float3 position;
			public float3 velocity;
			public float lifetime;
			public float scale;
		}

		void OnDrawGizmos()
		{
			// Draw Bounds
			var m = Gizmos.matrix;
			Gizmos.matrix = transform.localToWorldMatrix;
			Gizmos.color = new Color(0, 1, 0, 0.5f);
			Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
			Gizmos.matrix = m;
		}
	}
}