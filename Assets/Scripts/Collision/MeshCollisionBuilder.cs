using System.Collections.Generic;
using UnityEngine;

namespace Seb.Fluid.Collision {
    public class MeshCollisionBuilder : MonoBehaviour {
        [Header("Collision Objects")]
        public bool autoFindObjects = true;
        public List<MeshCollisionObject> collisionObjects = new();

        [Header("Debug")]
        public bool logObjectDetails = true;

        [SerializeField] int objectCount;
        [SerializeField] int triangleCount;

        readonly List<TriangleData> triangles = new();
        readonly List<ObjectData> objects = new();
        readonly List<string> objectNames = new();

        ComputeBuffer triangleBuffer;
        ComputeBuffer objectBuffer;

        public ComputeBuffer TriangleBuffer => triangleBuffer;
        public ComputeBuffer ObjectBuffer => objectBuffer;
        public int TriangleCount => triangleCount;
        public int ObjectCount => objectCount;

        //void Start() {
        //    Build();
        //}

        public void Build() {
            if (autoFindObjects) {
                FindCollisionObjects();
            }

            BuildCollisionData();
            CreateBuffers();

            Debug.Log(
                $"[MeshCollisionBuilder] Built {objectCount} collision object(s), {triangleCount} triangle(s)."
            );

            if (logObjectDetails) {
                LogObjectDetails();
            }
        }

        void FindCollisionObjects() {
            collisionObjects.Clear();

            MeshCollisionObject[] foundObjects =
                FindObjectsByType<MeshCollisionObject>(FindObjectsSortMode.None);

            foreach (MeshCollisionObject obj in foundObjects) {
                if (obj != null && obj.includeInFluidCollision) {
                    collisionObjects.Add(obj);
                }
            }
        }

        void BuildCollisionData() {
            triangles.Clear();
            objects.Clear();
            objectNames.Clear();

            objectCount = 0;
            triangleCount = 0;

            for (int objectIndex = 0; objectIndex < collisionObjects.Count; objectIndex++) {
                MeshCollisionObject obj = collisionObjects[objectIndex];

                if (obj == null || !obj.includeInFluidCollision)
                    continue;

                MeshFilter meshFilter = obj.meshFilter;

                if (meshFilter == null || meshFilter.sharedMesh == null)
                    continue;

                Mesh mesh = meshFilter.sharedMesh;

                int triangleStartIndex = triangles.Count;
                int meshTriangleCount = mesh.triangles.Length / 3;

                objectNames.Add(obj.name);

                Matrix4x4 localToWorld = obj.transform.localToWorldMatrix;
                Matrix4x4 worldToLocal = obj.transform.worldToLocalMatrix;

                objects.Add(new ObjectData {
                    localToWorld = localToWorld,
                    worldToLocal = worldToLocal,
                    previousLocalToWorld = localToWorld,
                    previousWorldToLocal = worldToLocal,
                    triangleStartIndex = triangleStartIndex,
                    triangleCount = meshTriangleCount,
                    collisionThickness = obj.collisionThickness,
                    collisionDamping = obj.collisionDamping,
                    friction = obj.friction,
                    isDynamic = obj.isDynamic ? 1 : 0
                });

                Vector3[] vertices = mesh.vertices;
                int[] meshTriangles = mesh.triangles;

                int storedObjectIndex = objects.Count - 1;

                for (int i = 0; i < meshTriangles.Length; i += 3) {
                    Vector3 a = vertices[meshTriangles[i]];
                    Vector3 b = vertices[meshTriangles[i + 1]];
                    Vector3 c = vertices[meshTriangles[i + 2]];

                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;

                    triangles.Add(new TriangleData {
                        a = a,
                        b = b,
                        c = c,
                        normal = normal,
                        objectIndex = storedObjectIndex
                    });
                }
            }

            objectCount = objects.Count;
            triangleCount = triangles.Count;
        }

        void CreateBuffers() {
            ReleaseBuffers();

            if (triangleCount > 0) {
                triangleBuffer = new ComputeBuffer(triangleCount, TriangleData.Stride);
                triangleBuffer.SetData(triangles);
            }

            if (objectCount > 0) {
                objectBuffer = new ComputeBuffer(objectCount, ObjectData.Stride);
                objectBuffer.SetData(objects);
            }
        }

        void ReleaseBuffers() {
            if (triangleBuffer != null) {
                triangleBuffer.Release();
                triangleBuffer = null;
            }

            if (objectBuffer != null) {
                objectBuffer.Release();
                objectBuffer = null;
            }
        }

        void LogObjectDetails() {
            for (int i = 0; i < objects.Count; i++) {
                ObjectData obj = objects[i];

                Debug.Log(
                    $"[MeshCollisionBuilder] Object {i}: {objectNames[i]}, " +
                    $"triangleStartIndex={obj.triangleStartIndex}, " +
                    $"triangleCount={obj.triangleCount}, " +
                    $"isDynamic={obj.isDynamic}"
                );
            }

            Debug.Log(
                $"[MeshCollisionBuilder] GPU buffers created: " +
                $"TriangleBuffer={(triangleBuffer != null ? triangleBuffer.count : 0)}, " +
                $"ObjectBuffer={(objectBuffer != null ? objectBuffer.count : 0)}"
            );
        }

        public void UpdateDynamicObjects() {
            if (objectBuffer == null || objects.Count == 0)
                return;

            bool hasChanges = false;

            for (int i = 0; i < objects.Count; i++) {
                if (i >= collisionObjects.Count)
                    continue;

                MeshCollisionObject collisionObject = collisionObjects[i];

                if (collisionObject == null || !collisionObject.isDynamic)
                    continue;

                ObjectData objectData = objects[i];

                objectData.previousLocalToWorld = objectData.localToWorld;
                objectData.previousWorldToLocal = objectData.worldToLocal;

                objectData.localToWorld = collisionObject.transform.localToWorldMatrix;
                objectData.worldToLocal = collisionObject.transform.worldToLocalMatrix;

                objects[i] = objectData;
                hasChanges = true;
            }

            if (hasChanges) {
                objectBuffer.SetData(objects);
            }
        }

        void OnDestroy() {
            ReleaseBuffers();
        }

        struct TriangleData {
            public Vector3 a;
            public Vector3 b;
            public Vector3 c;
            public Vector3 normal;
            public int objectIndex;

            public const int Stride =
                sizeof(float) * 3 +   // a
                sizeof(float) * 3 +   // b
                sizeof(float) * 3 +   // c
                sizeof(float) * 3 +   // normal
                sizeof(int);          // objectIndex
        }

        struct ObjectData {
            public Matrix4x4 localToWorld;
            public Matrix4x4 worldToLocal;
            public Matrix4x4 previousLocalToWorld;
            public Matrix4x4 previousWorldToLocal;

            public int triangleStartIndex;
            public int triangleCount;
            public float collisionThickness;
            public float collisionDamping;
            public float friction;
            public int isDynamic;

            public const int Stride =
                sizeof(float) * 16 +  // localToWorld
                sizeof(float) * 16 +  // worldToLocal
                sizeof(float) * 16 +  // previousLocalToWorld
                sizeof(float) * 16 +  // previousWorldToLocal
                sizeof(int) +         // triangleStartIndex
                sizeof(int) +         // triangleCount
                sizeof(float) +       // collisionThickness
                sizeof(float) +       // collisionDamping
                sizeof(float) +       // friction
                sizeof(int);          // isDynamic
        }
    }
}