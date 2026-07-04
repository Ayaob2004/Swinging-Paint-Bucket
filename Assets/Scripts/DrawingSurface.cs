using Seb.Fluid.Collision;
using UnityEngine;

/// <summary>
/// Geometric reference for the real drawing surface.
/// It does not paint or modify the fluid; MeshCollisionObject remains responsible
/// for GPU collision.
/// </summary>
[DisallowMultipleComponent]
public sealed class DrawingSurface : MonoBehaviour
{
    [Header("Required Reference")]
    [SerializeField] private MeshCollisionObject _collisionObject;

    [Header("Surface Orientation")]
    [Tooltip("The drawing face is the +Y face in this object's local space.")]
    [SerializeField] private bool _usePositiveLocalYFace = true;

    public MeshCollisionObject CollisionObject => _collisionObject;
    public Vector3 SurfaceNormal => _usePositiveLocalYFace ? transform.up : -transform.up;

    private void Reset()
    {
        _collisionObject = GetComponent<MeshCollisionObject>();
    }

    private void OnValidate()
    {
        if (_collisionObject == null)
            _collisionObject = GetComponent<MeshCollisionObject>();
    }

    /// <summary>Returns a stable point on the active drawing face.</summary>
    public Vector3 GetSurfacePoint()
    {
        MeshFilter meshFilter = _collisionObject != null ? _collisionObject.meshFilter : GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return transform.position;

        Bounds localBounds = meshFilter.sharedMesh.bounds;
        Vector3 localPoint = localBounds.center;
        localPoint.y = _usePositiveLocalYFace ? localBounds.max.y : localBounds.min.y;
        return transform.TransformPoint(localPoint);
    }

    /// <summary>
    /// Finds where a ray from worldStart, travelling in gravityDirection, reaches
    /// the mathematical plane of the drawing face.
    /// </summary>
    public bool TryProjectAlongGravity(Vector3 worldStart, Vector3 gravityDirection, out Vector3 hitPoint)
    {
        hitPoint = worldStart;
        Vector3 direction = gravityDirection.sqrMagnitude > 0.000001f
            ? gravityDirection.normalized
            : Vector3.down;

        Vector3 normal = SurfaceNormal;
        float denominator = Vector3.Dot(direction, normal);
        if (Mathf.Abs(denominator) < 0.00001f)
            return false;

        float distance = Vector3.Dot(GetSurfacePoint() - worldStart, normal) / denominator;
        if (distance < 0f)
            return false;

        hitPoint = worldStart + direction * distance;
        return true;
    }
}
