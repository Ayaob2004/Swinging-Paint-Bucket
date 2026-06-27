using UnityEngine;

namespace Seb.Fluid.Collision {
    [DisallowMultipleComponent]
    public class MeshCollisionObject : MonoBehaviour {
        [Header("Mesh Source")]
        public MeshFilter meshFilter;

        [Header("Collision Settings")]
        public bool includeInFluidCollision = true;
        public bool isDynamic = false;

        [Min(0f)]
        public float collisionThickness = 0.02f;

        [Range(0f, 1f)]
        public float collisionDamping = 0.85f;

        [Range(0f, 1f)]
        public float friction = 0.05f;

        void Reset() {
            meshFilter = GetComponent<MeshFilter>();
        }

        void OnValidate() {
            if (meshFilter == null) {
                meshFilter = GetComponent<MeshFilter>();
            }
        }
    }
}