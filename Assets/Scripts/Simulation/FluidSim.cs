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

		[Header("External Simulation Control")]
		[Tooltip("SimulationCoordinator owns the simulation timestep and the number of substeps.")]
		[SerializeField, HideInInspector] private bool externallyStepped = true;

		[Header("Simulation Settings")] public float gravity = -10;
		public float smoothingRadius = 0.2f;
		public float targetDensity = 630;
		public float pressureMultiplier = 288;
		public float nearPressureMultiplier = 2.15f;
		public float viscosityStrength = 0;
		[Range(0, 1)] public float collisionDamping = 0.95f;

        [Header("Collision")]
        public bool useMeshCollisions = false;
        public bool useGridCollisionQuery = false;

		[Header("Volumetric Render Settings")] public bool renderToTex3D;
		public int densityTextureRes;

		[Header("References")] public ComputeShader compute;
		public Spawner3D spawner;
        public MeshCollisionBuilder meshCollisionBuilder;
        [Tooltip("World-space density volume used only for baking and raymarching. It never parents or moves particles.")]
        public DynamicFluidRenderVolume renderVolume;

        [HideInInspector] public RenderTexture DensityMap;
		[HideInInspector] public RenderTexture ColorMap;
		public Vector3 Scale => renderVolume != null ? renderVolume.WorldSize : transform.localScale;

		// Buffers
		public ComputeBuffer positionBuffer { get; private set; }
		public ComputeBuffer velocityBuffer { get; private set; }
		public ComputeBuffer densityBuffer { get; private set; }
		public ComputeBuffer predictedPositionsBuffer;
		public ComputeBuffer debugBuffer { get; private set; }
		public ComputeBuffer colorBuffer { get; private set; }

		ComputeBuffer sortTarget_positionBuffer;
		ComputeBuffer sortTarget_velocityBuffer;
		ComputeBuffer sortTarget_predictedPositionsBuffer;
		ComputeBuffer sortTarget_colorBuffer;

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
		const int clearDensityKernel = 9;
		const int splatDensityKernel = 10;

		SpatialHash spatialHash;

		// State
		bool isInitialized;
		bool isSimulationRunning;
		float smoothRadiusOld;
		float simTimer;
		Spawner3D.SpawnData spawnData;
		Dictionary<ComputeBuffer, string> bufferNameLookup;
		int densityMapFrameCounter;
		Vector3Int cachedDensityMapSize;

		public bool IsInitialized => isInitialized;
		public bool IsSimulationRunning => isSimulationRunning;

		/// <summary>
		/// Creates GPU resources once. SimulationCoordinator calls this after the bucket
		/// has been placed in its initial paused pose.
		/// </summary>
		public void InitializeSimulation()
		{
			if (isInitialized)
				return;

			if (!ValidateReferences())
				return;
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
			debugBuffer = CreateStructuredBuffer<float3>(numParticles);
			colorBuffer = CreateStructuredBuffer<Color>(numParticles);
			sortTarget_positionBuffer = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_predictedPositionsBuffer = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_velocityBuffer = CreateStructuredBuffer<float3>(numParticles);
			sortTarget_colorBuffer = CreateStructuredBuffer<Color>(numParticles);
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
				{ debugBuffer, "Debug" },
				{ colorBuffer,"Colors" },
				{ sortTarget_colorBuffer,"SortTarget_Colors" }
			};

            if (meshCollisionBuilder != null) {
                meshCollisionBuilder.Build();

                if (meshCollisionBuilder.TriangleBuffer != null) {
                    bufferNameLookup.Add(meshCollisionBuilder.TriangleBuffer, "CollisionTriangles");
                }

                if (meshCollisionBuilder.ObjectBuffer != null) {
                    bufferNameLookup.Add(meshCollisionBuilder.ObjectBuffer, "CollisionObjects");
                }

                if (meshCollisionBuilder.GridCellBuffer != null) {
                    bufferNameLookup.Add(meshCollisionBuilder.GridCellBuffer, "CollisionGridCells");
                }

                if (meshCollisionBuilder.GridEntryBuffer != null) {
                    bufferNameLookup.Add(meshCollisionBuilder.GridEntryBuffer, "CollisionGridEntries");
                }

                if (meshCollisionBuilder.ObjectGridBuffer != null) {
                    bufferNameLookup.Add(meshCollisionBuilder.ObjectGridBuffer, "CollisionObjectGrids");
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
				colorBuffer, sortTarget_colorBuffer,
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
				colorBuffer, sortTarget_colorBuffer,
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
				meshCollisionBuilder.ObjectBuffer != null &&
				meshCollisionBuilder.GridCellBuffer != null &&
				meshCollisionBuilder.GridEntryBuffer != null &&
				meshCollisionBuilder.ObjectGridBuffer != null) {
                SetBuffers(compute, updatePositionsKernel, bufferNameLookup, new ComputeBuffer[]
                {
					positionBuffer,
					velocityBuffer,
					meshCollisionBuilder.TriangleBuffer,
					meshCollisionBuilder.ObjectBuffer,
					meshCollisionBuilder.GridCellBuffer,
					meshCollisionBuilder.GridEntryBuffer,
					meshCollisionBuilder.ObjectGridBuffer
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
				colorBuffer
			});

			SetBuffers(compute, clearDensityKernel, bufferNameLookup, new ComputeBuffer[] { });

			SetBuffers(compute, splatDensityKernel, bufferNameLookup, new ComputeBuffer[]
			{
				positionBuffer,colorBuffer
			});

			compute.SetInt("numParticles", positionBuffer.count);

			UpdateSmoothingConstants();

			isInitialized = true;
			isSimulationRunning = false;
			BakeDensityMap(forceDensityUpdate: true);
			SimulationInitCompleted?.Invoke(this);
		}

		/// <summary>
		/// Starts or pauses the fluid without changing its particle data.
		/// </summary>
		public void SetSimulationRunning(bool running)
		{
			isSimulationRunning = isInitialized && running;
		}

		/// <summary>
		/// Rebuilds fluid state at the current Spawner3D pose. All old particle state is discarded.
		/// </summary>
		public void ResetFluidAtCurrentSpawn()
		{
			if (!isInitialized)
			{
				InitializeSimulation();
				return;
			}

			if (spawner == null)
			{
				Debug.LogError("FluidSim needs a Spawner3D reference to reset the fluid.", this);
				return;
			}

			SetSimulationRunning(false);
			Spawner3D.SpawnData newSpawnData = spawner.GetSpawnData();
			if (newSpawnData.points == null || newSpawnData.points.Length == 0)
			{
				Debug.LogError("Spawner3D returned no particles. Fluid reset was cancelled.", this);
				return;
			}

			if (positionBuffer == null || newSpawnData.points.Length != positionBuffer.count)
			{
				ReleaseFluidResources();
				isInitialized = false;
				InitializeSimulation();
				return;
			}

			spawnData = newSpawnData;
			SetInitialBufferData(spawnData);
			densityMapFrameCounter = 0;
			BakeDensityMap(forceDensityUpdate: true);
		}

		/// <summary>
		/// Advances exactly one simulation substep. It is intentionally not called from Update.
		/// </summary>
		public void SimulateSubstep(float deltaTime)
		{
			if (!isInitialized || !isSimulationRunning || deltaTime <= 0f)
				return;

			UpdateSettings(deltaTime, deltaTime);
			simTimer += deltaTime;
			RunSimulationStep();
		}

		/// <summary>
		/// Updates the volumetric density texture once after the externally controlled substeps.
		/// </summary>
		public void BakeDensityMap(bool forceDensityUpdate = false)
		{
			if (!isInitialized || !renderToTex3D)
				return;

			UpdateSettings(0f, 0f);
			if (forceDensityUpdate || ShouldUpdateDensityMap())
				UpdateDensityMap();
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
			compute.SetTexture(clearDensityKernel, "ColorMap", ColorMap);
			compute.SetTexture(renderKernel, "ColorMap", ColorMap);
			compute.SetTexture(splatDensityKernel, "ColorMap", ColorMap);
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
			Vector3Int desiredSize = renderVolume != null
				? renderVolume.DensityTextureResolution
				: new Vector3Int(Mathf.Max(8, densityTextureRes), Mathf.Max(8, densityTextureRes), Mathf.Max(8, densityTextureRes));

			if (DensityMap != null && cachedDensityMapSize == desiredSize)
				return;

			cachedDensityMapSize = desiredSize;
			CreateRenderTexture3D(ref DensityMap, desiredSize.x, desiredSize.y, desiredSize.z, UnityEngine.Experimental.Rendering.GraphicsFormat.R16_SFloat, TextureWrapMode.Clamp);
			CreateRenderTexture3D(ref ColorMap, desiredSize.x, desiredSize.y, desiredSize.z,UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat, TextureWrapMode.Clamp);
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

			// Simulation bounds stay separate from the render volume. The legacy analytic
			// fallback collision still uses this transform; normal operation uses mesh collisions.
			Vector3 simBoundsSize = transform.localScale;
			Vector3 simBoundsCentre = transform.position;
			Vector3 densityBoundsSize = renderVolume != null ? renderVolume.WorldSize : transform.localScale;
			Matrix4x4 densityVolumeLocalToWorld = renderVolume != null
				? renderVolume.LocalToWorldMatrix
				: Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
			Matrix4x4 densityVolumeWorldToLocal = densityVolumeLocalToWorld.inverse;


            compute.SetInt("collisionTriangleCount", meshCollisionBuilder != null ? meshCollisionBuilder.TriangleCount : 0);
            compute.SetInt("collisionObjectCount", meshCollisionBuilder != null ? meshCollisionBuilder.ObjectCount : 0);

            compute.SetFloat("deltaTime", stepDeltaTime);
            compute.SetFloat("meshCollisionDeltaTime", frameDeltaTime);
			compute.SetFloat("simTime", simTimer);
			compute.SetFloat("gravity", gravity);
			compute.SetFloat("collisionDamping", collisionDamping);
            compute.SetInt("useMeshCollisions", useMeshCollisions ? 1 : 0);
            compute.SetInt("useGridCollisionQuery", useGridCollisionQuery ? 1 : 0);
            compute.SetFloat("smoothingRadius", smoothingRadius);
			compute.SetFloat("targetDensity", targetDensity);
			compute.SetFloat("pressureMultiplier", pressureMultiplier);
			compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
			compute.SetFloat("viscosityStrength", viscosityStrength);
			compute.SetFloat("predictionTimestep", predictionTimestep);

			compute.SetVector("boundsSize", simBoundsSize);
			compute.SetVector("centre", simBoundsCentre);

			compute.SetMatrix("localToWorld", transform.localToWorldMatrix);
			compute.SetMatrix("worldToLocal", transform.worldToLocalMatrix);

            compute.SetVector("densityBoundsSize", densityBoundsSize);
            compute.SetMatrix("densityVolumeLocalToWorld", densityVolumeLocalToWorld);
            compute.SetMatrix("densityVolumeWorldToLocal", densityVolumeWorldToLocal);

		}

		void SetInitialBufferData(Spawner3D.SpawnData spawnData)
		{
			positionBuffer.SetData(spawnData.points);
			predictedPositionsBuffer.SetData(spawnData.points);
			velocityBuffer.SetData(spawnData.velocities);
			densityBuffer.SetData(new float2[densityBuffer.count]);
			colorBuffer.SetData(spawnData.colors);
			sortTarget_colorBuffer.SetData(new Color[sortTarget_colorBuffer.count]);
			sortTarget_positionBuffer.SetData(new float3[sortTarget_positionBuffer.count]);
			sortTarget_predictedPositionsBuffer.SetData(new float3[sortTarget_predictedPositionsBuffer.count]);
			sortTarget_velocityBuffer.SetData(new float3[sortTarget_velocityBuffer.count]);
			debugBuffer.SetData(new float3[debugBuffer.count]);
			densityMapFrameCounter = 0;
			simTimer = 0f;
		}

		public void ApplyPerformanceProfile()
		{
			switch (performanceProfile)
			{
				case SimulationPerformanceProfile.Laptop:
					densityBakeMode = DensityBakeMode.FastParticleSplat;
					densityTextureRes = 96;
					densityTextureUpdateInterval = 1;
					splatDensityScale = 1.15f;
					break;
				case SimulationPerformanceProfile.Balanced:
					densityBakeMode = DensityBakeMode.FastParticleSplat;
					densityTextureRes = 128;
					densityTextureUpdateInterval = 1;
					splatDensityScale = 1f;
					break;
				case SimulationPerformanceProfile.HighQuality:
					densityBakeMode = DensityBakeMode.AccurateSPHPerVoxel;
					densityTextureRes = 150;
					densityTextureUpdateInterval = 1;
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
			if (renderVolume == null)
				renderVolume = FindAnyObjectByType<DynamicFluidRenderVolume>();

			if (performanceProfile != SimulationPerformanceProfile.Custom)
				ApplyPerformanceProfile();
			if (materialPreset != FluidMaterialPreset.Custom)
				ApplyMaterialPreset();
		}

        bool ValidateReferences()
        {
            if (compute == null)
            {
                Debug.LogError("FluidSim needs a ComputeShader reference.", this);
                return false;
            }

            if (spawner == null)
            {
                Debug.LogError("FluidSim needs a Spawner3D reference.", this);
                return false;
            }

            return true;
        }

        void ReleaseFluidResources()
        {
            bool hadFluidResources = positionBuffer != null;

            Release(positionBuffer);
            Release(predictedPositionsBuffer);
            Release(velocityBuffer);
            Release(densityBuffer);
			Release(colorBuffer);
			Release(sortTarget_colorBuffer);
            Release(debugBuffer);
            Release(sortTarget_positionBuffer);
            Release(sortTarget_predictedPositionsBuffer);
            Release(sortTarget_velocityBuffer);

            positionBuffer = null;
            predictedPositionsBuffer = null;
            velocityBuffer = null;
            densityBuffer = null;
			colorBuffer = null;
			sortTarget_colorBuffer = null;
            debugBuffer = null;
            sortTarget_positionBuffer = null;
            sortTarget_predictedPositionsBuffer = null;
            sortTarget_velocityBuffer = null;

            if (hadFluidResources)
                spatialHash.Release();

            bufferNameLookup?.Clear();
            bufferNameLookup = null;
        }

        void OnDestroy()
        {
            ReleaseFluidResources();

            if (DensityMap != null)
            {
                DensityMap.Release();
                Destroy(DensityMap);
                DensityMap = null;
            }
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