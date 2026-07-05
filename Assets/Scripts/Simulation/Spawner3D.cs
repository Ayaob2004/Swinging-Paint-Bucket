using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Seb.Fluid.Simulation
{
    /// <summary>
    /// Creates the initial particle set for FluidSim.
    /// Spawn regions are authored in Bucket Fill Point local space, then converted
    /// to world space at the instant FluidSim asks for fresh spawn data.
    /// </summary>
    public class Spawner3D : MonoBehaviour
    {
        public enum InitialVelocitySpace
        {
            World,
            BucketFillPointLocal
        }

        [Header("Bucket Fill Reference")]
        [Tooltip("The point inside the bucket that defines the origin and orientation of every spawn region.")]
        [SerializeField] private Transform bucketFillPoint;

        [Header("Particle Spawn")]
        [Min(1)] public int particleSpawnDensity = 600;
        [Tooltip("Initial velocity applied to every newly created particle.")]
        public float3 initialVel;
        public InitialVelocitySpace initialVelocitySpace = InitialVelocitySpace.World;
        [Min(0f)] public float jitterStrength;
        public bool showSpawnBounds = true;
        public SpawnRegion[] spawnRegions;

        [Header("Debug Info")]
        public int debug_num_particles;
        public int debug_bucket_particles;
        public float debug_spawn_volume;
        
        public Transform BucketFillPoint => bucketFillPoint;
        public int ParticleSpawnDensity => particleSpawnDensity;

        public void SetParticleSpawnDensity(int density)
        {
            particleSpawnDensity = Mathf.Max(1, density);
        }

        public Color GetPrimaryPaintColor()
        {
            if (spawnRegions == null || spawnRegions.Length == 0)
                return Color.red;

            return spawnRegions[0].paintColor;
        }

        public void SetAllPaintColors(Color color)
        {
            if (spawnRegions == null)
                return;

            for (int i = 0; i < spawnRegions.Length; i++)
            {
                SpawnRegion region = spawnRegions[i];
                region.paintColor = color;
                spawnRegions[i] = region;
            }
        }
        /// <summary>
        /// Generates particle positions in world space from the bucket's current pose.
        /// This is intentionally called at initialization and at every fluid reset.
        /// </summary>
        public SpawnData GetSpawnData()
        {
            if (bucketFillPoint == null)
            {
                Debug.LogError("Spawner3D needs a Bucket Fill Point reference before it can create fluid.", this);
                return new SpawnData
                {
                    points = Array.Empty<float3>(),
                    velocities = Array.Empty<float3>(),
                    colors = Array.Empty<Color>()
                };
            }

            if (spawnRegions == null || spawnRegions.Length == 0)
            {
                Debug.LogError("Spawner3D needs at least one spawn region.", this);
                return new SpawnData
                {
                    points = Array.Empty<float3>(),
                    velocities = Array.Empty<float3>(),
                    colors = Array.Empty<Color>()
                };
            }

            List<float3> allPoints = new();
            List<float3> allVelocities = new();
            List<Color> allColors = new();

            foreach (SpawnRegion region in spawnRegions)
            {
                if (region.size <= 0f)
                    continue;

                int particlesPerAxis = region.CalculateParticleCountPerAxis(particleSpawnDensity);
                (float3[] points, float3[] velocities) = SpawnBucket(particlesPerAxis, region);
                allPoints.AddRange(points);
                allVelocities.AddRange(velocities);
                for (int i = 0; i < points.Length; i++)
                { allColors.Add(region.paintColor); }
            }

            return new SpawnData
            {
                points = allPoints.ToArray(),
                velocities = allVelocities.ToArray(),
                colors = allColors.ToArray()
            };
        }

        (float3[] points, float3[] velocities) SpawnBucket(int numPerAxis, SpawnRegion region)
        {
            List<float3> points = new();
            List<float3> velocities = new();

            // A tapered bucket-shaped initial volume in Bucket Fill Point local space.
            float bottomRadius = region.size * 0.24f;
            float topRadius = region.size * 0.38f;
            float height = region.size;
            float3 worldInitialVelocity = GetWorldInitialVelocity();

            for (int x = 0; x < numPerAxis; x++)
            {
                for (int y = 0; y < numPerAxis; y++)
                {
                    for (int z = 0; z < numPerAxis; z++)
                    {
                        float tx = x / (numPerAxis - 1f);
                        float ty = y / (numPerAxis - 1f);
                        float tz = z / (numPerAxis - 1f);

                        float px = (tx - 0.5f) * region.size;
                        float py = (ty - 0.5f) * height;
                        float pz = (tz - 0.5f) * region.size;

                        float currentRadius = Mathf.Lerp(bottomRadius, topRadius, ty);
                        float radius = Mathf.Sqrt(px * px + pz * pz);

                        if (radius > currentRadius)
                            continue;

                        // Jitter belongs to the bucket-local spawn shape so it rotates with the bucket too.
                        Vector3 localJitter = UnityEngine.Random.insideUnitSphere * jitterStrength;
                        Vector3 localPoint = region.localCentre + new Vector3(px, py, pz) + localJitter;
                        Vector3 worldPoint = bucketFillPoint.TransformPoint(localPoint);

                        points.Add(worldPoint);
                        velocities.Add(worldInitialVelocity);
                    }
                }
            }

            return (points.ToArray(), velocities.ToArray());
        }

        float3 GetWorldInitialVelocity()
        {
            Vector3 velocity = initialVel;
            if (initialVelocitySpace == InitialVelocitySpace.BucketFillPointLocal && bucketFillPoint != null)
                velocity = bucketFillPoint.TransformDirection(velocity);

            return velocity;
        }

        void OnValidate()
        {
            particleSpawnDensity = Mathf.Max(1, particleSpawnDensity);
            jitterStrength = Mathf.Max(0f, jitterStrength);

            debug_spawn_volume = 0f;
            debug_num_particles = 0;
            debug_bucket_particles = 0;

            if (spawnRegions == null)
                return;

            foreach (SpawnRegion region in spawnRegions)
            {
                if (region.size <= 0f)
                    continue;

                debug_spawn_volume += region.Volume;
                int numPerAxis = region.CalculateParticleCountPerAxis(particleSpawnDensity);
                debug_num_particles += numPerAxis * numPerAxis * numPerAxis;
                debug_bucket_particles += EstimateBucketParticleCount(numPerAxis, region.size);
            }
        }

        static int EstimateBucketParticleCount(int numPerAxis, float size)
        {
            float bottomRadius = size * 0.24f;
            float topRadius = size * 0.38f;
            int count = 0;

            for (int x = 0; x < numPerAxis; x++)
            {
                for (int y = 0; y < numPerAxis; y++)
                {
                    for (int z = 0; z < numPerAxis; z++)
                    {
                        float tx = x / (numPerAxis - 1f);
                        float ty = y / (numPerAxis - 1f);
                        float tz = z / (numPerAxis - 1f);

                        float px = (tx - 0.5f) * size;
                        float py = (ty - 0.5f) * size;
                        float pz = (tz - 0.5f) * size;

                        float currentRadius = Mathf.Lerp(bottomRadius, topRadius, ty);
                        float radius = Mathf.Sqrt(px * px + pz * pz);

                        if (radius <= currentRadius)
                            count++;
                    }
                }
            }

            return count;
        }

        void OnDrawGizmos()
        {
            if (!showSpawnBounds || Application.isPlaying || bucketFillPoint == null || spawnRegions == null)
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = bucketFillPoint.localToWorldMatrix;

            foreach (SpawnRegion region in spawnRegions)
            {
                Gizmos.color = region.paintColor;
                Gizmos.DrawWireCube(region.localCentre, Vector3.one * region.size);
            }

            Gizmos.matrix = previousMatrix;
        }

        [Serializable]
        public struct SpawnRegion
        {
            [Tooltip("Centre of this region in Bucket Fill Point local space.")]
            [FormerlySerializedAs("centre")]
            public Vector3 localCentre;

            [Min(0.001f)]
            public float size;
            [FormerlySerializedAs("debugDisplayCol")]
            [Tooltip(
                "��� �������� �� ��� �������"
            )]
            public Color paintColor;

            public float Volume => size * size * size;

            public int CalculateParticleCountPerAxis(int particleDensity)
            {
                int targetParticleCount = Mathf.Max(8, Mathf.RoundToInt(Volume * particleDensity));
                return Mathf.Max(2, Mathf.RoundToInt(Mathf.Pow(targetParticleCount, 1f / 3f)));
            }
        }

        public struct SpawnData
        {
            public float3[] points;
            public float3[] velocities;
            public Color[] colors;
        }

        public int SpawnRegionCount
        {
            get
            {
                if (spawnRegions == null)
                    return 0;

                return spawnRegions.Length;
            }
        }

        public Color GetSpawnRegionColor(int index)
        {
            if (spawnRegions == null || spawnRegions.Length == 0)
                return Color.white;

            index = Mathf.Clamp(index, 0, spawnRegions.Length - 1);
            return spawnRegions[index].paintColor;
        }

        public void SetSpawnRegionColor(int index, Color color)
        {
            if (spawnRegions == null || spawnRegions.Length == 0)
                return;

            index = Mathf.Clamp(index, 0, spawnRegions.Length - 1);

            SpawnRegion region = spawnRegions[index];
            region.paintColor = color;
            spawnRegions[index] = region;
        }
    }
}
