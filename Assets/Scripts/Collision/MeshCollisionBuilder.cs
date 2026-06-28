using System.Collections.Generic;
using UnityEngine;

namespace Seb.Fluid.Collision {
    public class MeshCollisionBuilder : MonoBehaviour {
        [Header("Collision Objects")]
        public bool autoFindObjects = true;
        public List<MeshCollisionObject> collisionObjects = new();

        [Header("Grid Settings")]
        [Min(0.001f)]
        public float gridCellSize = 0.2f;

        [Header("Debug")]
        public bool logObjectDetails = true;

        [SerializeField] int objectCount;
        [SerializeField] int triangleCount;

        readonly List<TriangleData> triangles = new();
        readonly List<ObjectData> objects = new();
        readonly List<string> objectNames = new();

        readonly List<GridCellData> gridCells = new();
        readonly List<GridEntryData> gridEntries = new();
        readonly List<ObjectGridData> objectGrids = new();

        ComputeBuffer triangleBuffer;
        ComputeBuffer objectBuffer;
        ComputeBuffer gridCellBuffer;
        ComputeBuffer gridEntryBuffer;
        ComputeBuffer objectGridBuffer;
        public ComputeBuffer TriangleBuffer => triangleBuffer;
        public ComputeBuffer ObjectBuffer => objectBuffer;

        public ComputeBuffer GridCellBuffer => gridCellBuffer;
        public ComputeBuffer GridEntryBuffer => gridEntryBuffer;
        public ComputeBuffer ObjectGridBuffer => objectGridBuffer;
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

            gridCells.Clear();
            gridEntries.Clear();

            objectGrids.Clear();

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

                DebugGridInfoForObject(mesh, obj.name);

                ObjectGridData objectGrid = BuildGridForObject(mesh, obj.name, triangleStartIndex);
                objectGrids.Add(objectGrid);

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

        void DebugGridInfoForObject(Mesh mesh, string objectName) {
            Bounds bounds = mesh.bounds;

            Vector3 min = bounds.min;
            Vector3 size = bounds.size;

            int resolutionX = Mathf.Max(1, Mathf.CeilToInt(size.x / gridCellSize));
            int resolutionY = Mathf.Max(1, Mathf.CeilToInt(size.y / gridCellSize));
            int resolutionZ = Mathf.Max(1, Mathf.CeilToInt(size.z / gridCellSize));

            int totalCells = resolutionX * resolutionY * resolutionZ;

            Debug.Log(
                $"[MeshCollisionBuilder] Grid {objectName}: " +
                $"min={min}, size={size}, " +
                $"resolution=({resolutionX}, {resolutionY}, {resolutionZ}), " +
                $"totalCells={totalCells}, cellSize={gridCellSize}"
            );
        }

        int FlattenGridIndex(int x, int y, int z, int resolutionX, int resolutionY) {
            return x + resolutionX * (y + resolutionY * z);
        }

        Vector3Int WorldToGridCell(Vector3 localPoint, Vector3 gridMin, int resolutionX, int resolutionY, int resolutionZ) {
            Vector3 relative = (localPoint - gridMin) / gridCellSize;

            int x = Mathf.Clamp(Mathf.FloorToInt(relative.x), 0, resolutionX - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(relative.y), 0, resolutionY - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(relative.z), 0, resolutionZ - 1);

            return new Vector3Int(x, y, z);
        }

        ObjectGridData BuildGridForObject(Mesh mesh, string objectName, int triangleStartIndex) {
            Bounds bounds = mesh.bounds;

            Vector3 gridMin = bounds.min;
            Vector3 size = bounds.size;

            int resolutionX = Mathf.Max(1, Mathf.CeilToInt(size.x / gridCellSize));
            int resolutionY = Mathf.Max(1, Mathf.CeilToInt(size.y / gridCellSize));
            int resolutionZ = Mathf.Max(1, Mathf.CeilToInt(size.z / gridCellSize));

            int totalCells = resolutionX * resolutionY * resolutionZ;

            List<int>[] temporaryCells = new List<int>[totalCells];

            Vector3[] vertices = mesh.vertices;
            int[] meshTriangles = mesh.triangles;

            for (int i = 0; i < meshTriangles.Length; i += 3) {
                Vector3 a = vertices[meshTriangles[i]];
                Vector3 b = vertices[meshTriangles[i + 1]];
                Vector3 c = vertices[meshTriangles[i + 2]];

                Vector3 min = Vector3.Min(a, Vector3.Min(b, c));
                Vector3 max = Vector3.Max(a, Vector3.Max(b, c));

                Vector3Int minCell = WorldToGridCell(min, gridMin, resolutionX, resolutionY, resolutionZ);
                Vector3Int maxCell = WorldToGridCell(max, gridMin, resolutionX, resolutionY, resolutionZ);

                int globalTriangleIndex = triangleStartIndex + (i / 3);

                for (int z = minCell.z; z <= maxCell.z; z++) {
                    for (int y = minCell.y; y <= maxCell.y; y++) {
                        for (int x = minCell.x; x <= maxCell.x; x++) {
                            int cellIndex = FlattenGridIndex(x, y, z, resolutionX, resolutionY);

                            if (temporaryCells[cellIndex] == null) {
                                temporaryCells[cellIndex] = new List<int>();
                            }

                            temporaryCells[cellIndex].Add(globalTriangleIndex);
                        }
                    }
                }
            }

            int cellStartIndex = gridCells.Count;
            int entryStartIndex = gridEntries.Count;

            int occupiedCells = 0;
            int totalEntries = 0;
            int maxTrianglesInCell = 0;

            for (int i = 0; i < temporaryCells.Length; i++) {
                List<int> cellTriangles = temporaryCells[i];

                if (cellTriangles == null || cellTriangles.Count == 0) {
                    gridCells.Add(new GridCellData {
                        entryStartIndex = -1,
                        entryCount = 0
                    });

                    continue;
                }

                int cellEntryStart = gridEntries.Count;

                for (int j = 0; j < cellTriangles.Count; j++) {
                    gridEntries.Add(new GridEntryData {
                        triangleIndex = cellTriangles[j]
                    });
                }

                gridCells.Add(new GridCellData {
                    entryStartIndex = cellEntryStart,
                    entryCount = cellTriangles.Count
                });

                occupiedCells++;
                totalEntries += cellTriangles.Count;
                maxTrianglesInCell = Mathf.Max(maxTrianglesInCell, cellTriangles.Count);
            }

            float averageTrianglesPerOccupiedCell =
                occupiedCells > 0 ? totalEntries / (float)occupiedCells : 0f;

            Debug.Log(
                $"[MeshCollisionBuilder] GridBuilt {objectName}: " +
                $"cells={totalCells}, occupiedCells={occupiedCells}, " +
                $"entries={totalEntries}, avgTrisPerOccupiedCell={averageTrianglesPerOccupiedCell:F2}, " +
                $"maxTrisInCell={maxTrianglesInCell} + " +
                $"cellStartIndex={cellStartIndex}, entryStartIndex={entryStartIndex}"
            );

            return new ObjectGridData {
                gridMin = gridMin,
                gridCellSize = gridCellSize,
                resolutionX = resolutionX,
                resolutionY = resolutionY,
                resolutionZ = resolutionZ,
                cellStartIndex = cellStartIndex,
                cellCount = totalCells,
                entryStartIndex = entryStartIndex,
                entryCount = totalEntries
            };
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

            if (gridCells.Count > 0) {
                gridCellBuffer = new ComputeBuffer(gridCells.Count, GridCellData.Stride);
                gridCellBuffer.SetData(gridCells);
            }

            if (gridEntries.Count > 0) {
                gridEntryBuffer = new ComputeBuffer(gridEntries.Count, GridEntryData.Stride);
                gridEntryBuffer.SetData(gridEntries);
            }

            if (objectGrids.Count > 0) {
                objectGridBuffer = new ComputeBuffer(objectGrids.Count, ObjectGridData.Stride);
                objectGridBuffer.SetData(objectGrids);
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

            if (gridCellBuffer != null) {
                gridCellBuffer.Release();
                gridCellBuffer = null;
            }

            if (gridEntryBuffer != null) {
                gridEntryBuffer.Release();
                gridEntryBuffer = null;
            }

            if (objectGridBuffer != null) {
                objectGridBuffer.Release();
                objectGridBuffer = null;
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
                $"ObjectBuffer={(objectBuffer != null ? objectBuffer.count : 0)}, " +
                $"GridCellBuffer={(gridCellBuffer != null ? gridCellBuffer.count : 0)}, " +
                $"GridEntryBuffer={(gridEntryBuffer != null ? gridEntryBuffer.count : 0)}, " +
                $"ObjectGridBuffer={(objectGridBuffer != null ? objectGridBuffer.count : 0)}"
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

        struct GridCellData {
            public int entryStartIndex;
            public int entryCount;

            public const int Stride =
                sizeof(int) +    // entryStartIndex
                sizeof(int);     // entryCount
        }

        struct GridEntryData {
            public int triangleIndex;

            public const int Stride =
                sizeof(int);     // triangleIndex
        }

        struct ObjectGridData {
            public Vector3 gridMin;
            public float gridCellSize;

            public int resolutionX;
            public int resolutionY;
            public int resolutionZ;

            public int cellStartIndex;
            public int cellCount;
            public int entryStartIndex;
            public int entryCount;

            public const int Stride =
                sizeof(float) * 3 +   // gridMin
                sizeof(float) +       // gridCellSize
                sizeof(int) +         // resolutionX
                sizeof(int) +         // resolutionY
                sizeof(int) +         // resolutionZ
                sizeof(int) +         // cellStartIndex
                sizeof(int) +         // cellCount
                sizeof(int) +         // entryStartIndex
                sizeof(int);          // entryCount
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