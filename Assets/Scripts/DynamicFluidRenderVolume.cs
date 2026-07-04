using System.Collections.Generic;
using Seb.Fluid.Collision;
using UnityEngine;

/// <summary>
/// Owns only the world-space volume used to bake and raymarch the fluid density.
/// It never moves particles and never parents the simulation to the bucket.
/// </summary>
[DisallowMultipleComponent]
public sealed class DynamicFluidRenderVolume : MonoBehaviour
{
    private struct OutletSample
    {
        public Vector3 Position;
        public float Time;

        public OutletSample(Vector3 position, float time)
        {
            Position = position;
            Time = time;
        }
    }

    [Header("Required References")]
    [SerializeField] private MeshCollisionObject _bucketCollisionObject;
    [SerializeField] private Transform _paintOutletRegion;
    [SerializeField] private DrawingSurface _drawingSurface;

    [Header("Render Coverage")]
    [SerializeField, Min(0f)] private float _maximumRelativeExitSpeed = 5f;
    [SerializeField, Min(0f)] private float _maximumUpwardExitSpeed = 0f;
    [SerializeField, Min(0f)] private float _safetyMargin = 0.5f;
    [SerializeField] private Vector3 _minimumWorldSize = new Vector3(6f, 6f, 6f);
    [SerializeField] private Vector3 _maximumWorldSize = new Vector3(40f, 40f, 40f);

    [Header("Density Texture")]
    [SerializeField] private Vector3Int _densityTextureResolution = new Vector3Int(96, 160, 96);

    [Header("Debug")]
    [SerializeField] private bool _drawGizmos = true;

    private readonly List<OutletSample> _outletHistory = new();
    private Bounds _worldBounds;
    private bool _hasValidBounds;

    public Vector3 WorldSize => _hasValidBounds ? _worldBounds.size : transform.lossyScale;
    public Vector3Int DensityTextureResolution => new Vector3Int(
        Mathf.Max(8, _densityTextureResolution.x),
        Mathf.Max(8, _densityTextureResolution.y),
        Mathf.Max(8, _densityTextureResolution.z));
    public Matrix4x4 LocalToWorldMatrix => Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
    public Matrix4x4 WorldToLocalMatrix => LocalToWorldMatrix.inverse;
    public Bounds WorldBounds => _worldBounds;

    public void ResetHistory()
    {
        _outletHistory.Clear();
        _hasValidBounds = false;
    }

    /// <summary>
    /// Rebuilds the smallest conservative render volume that covers the bucket and
    /// every still-airborne outlet path down to the drawing surface.
    /// </summary>
    public void UpdateBounds(float gravity)
    {
        ResolveMissingReferences();
        if (_bucketCollisionObject == null || _paintOutletRegion == null || _drawingSurface == null)
            return;

        float now = Time.fixedTime;
        float airborneLifetime = EstimateMaximumAirborneLifetime(Mathf.Abs(gravity));
        _outletHistory.Add(new OutletSample(_paintOutletRegion.position, now));

        for (int i = _outletHistory.Count - 1; i >= 0; i--)
        {
            if (now - _outletHistory[i].Time > airborneLifetime)
                _outletHistory.RemoveAt(i);
        }

        Bounds bounds = GetBucketWorldBounds();
        Vector3 gravityDirection = gravity <= 0f ? Vector3.down : Vector3.up;
        float lateralMargin = _maximumRelativeExitSpeed * airborneLifetime + _safetyMargin;
        Vector3 expansion = new Vector3(lateralMargin, _safetyMargin, lateralMargin) * 2f;

        for (int i = 0; i < _outletHistory.Count; i++)
        {
            Vector3 outletPoint = _outletHistory[i].Position;
            bounds.Encapsulate(outletPoint);

            if (_drawingSurface.TryProjectAlongGravity(outletPoint, gravityDirection, out Vector3 landingPoint))
                bounds.Encapsulate(landingPoint);
        }

        bounds.Expand(expansion);
        bounds.size = ClampSize(EnsureMinimumSize(bounds.size));

        _worldBounds = bounds;
        _hasValidBounds = true;

        // World-aligned volume: gravity and falling paint do not inherit bucket rotation.
        transform.SetPositionAndRotation(bounds.center, Quaternion.identity);
        transform.localScale = bounds.size;
    }

    private float EstimateMaximumAirborneLifetime(float gravityMagnitude)
    {
        Vector3 start = _paintOutletRegion != null ? _paintOutletRegion.position : transform.position;
        Vector3 gravityDirection = gravityMagnitude > 0.0001f ? Vector3.down : Vector3.down;
        float fallDistance = 0f;

        if (_drawingSurface != null && _drawingSurface.TryProjectAlongGravity(start, gravityDirection, out Vector3 landingPoint))
            fallDistance = Vector3.Distance(start, landingPoint);

        gravityMagnitude = Mathf.Max(0.01f, gravityMagnitude);
        float fallTime = Mathf.Sqrt(2f * Mathf.Max(0f, fallDistance) / gravityMagnitude);
        float upwardTime = _maximumUpwardExitSpeed / gravityMagnitude;
        return Mathf.Max(0.1f, fallTime + upwardTime + 0.1f);
    }

    private Bounds GetBucketWorldBounds()
    {
        MeshFilter meshFilter = _bucketCollisionObject.meshFilter;
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            Bounds localBounds = meshFilter.sharedMesh.bounds;
            return TransformLocalBounds(meshFilter.transform.localToWorldMatrix, localBounds);
        }

        Renderer[] renderers = _bucketCollisionObject.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds result = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                result.Encapsulate(renderers[i].bounds);
            return result;
        }

        return new Bounds(_bucketCollisionObject.transform.position, Vector3.one);
    }

    private static Bounds TransformLocalBounds(Matrix4x4 localToWorld, Bounds localBounds)
    {
        Vector3 center = localBounds.center;
        Vector3 extents = localBounds.extents;
        Bounds result = new Bounds(localToWorld.MultiplyPoint3x4(center), Vector3.zero);

        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
            result.Encapsulate(localToWorld.MultiplyPoint3x4(corner));
        }

        return result;
    }

    private Vector3 EnsureMinimumSize(Vector3 size)
    {
        return new Vector3(
            Mathf.Max(size.x, _minimumWorldSize.x),
            Mathf.Max(size.y, _minimumWorldSize.y),
            Mathf.Max(size.z, _minimumWorldSize.z));
    }

    private Vector3 ClampSize(Vector3 size)
    {
        return new Vector3(
            Mathf.Min(size.x, Mathf.Max(_minimumWorldSize.x, _maximumWorldSize.x)),
            Mathf.Min(size.y, Mathf.Max(_minimumWorldSize.y, _maximumWorldSize.y)),
            Mathf.Min(size.z, Mathf.Max(_minimumWorldSize.z, _maximumWorldSize.z)));
    }

    private void ResolveMissingReferences()
    {
        if (_bucketCollisionObject == null)
            _bucketCollisionObject = FindAnyObjectByType<BucketMotionController>()?.GetComponentInChildren<MeshCollisionObject>();
        if (_drawingSurface == null)
            _drawingSurface = FindAnyObjectByType<DrawingSurface>();
    }

    private void OnValidate()
    {
        _minimumWorldSize.x = Mathf.Max(0.1f, _minimumWorldSize.x);
        _minimumWorldSize.y = Mathf.Max(0.1f, _minimumWorldSize.y);
        _minimumWorldSize.z = Mathf.Max(0.1f, _minimumWorldSize.z);
        _maximumWorldSize.x = Mathf.Max(_minimumWorldSize.x, _maximumWorldSize.x);
        _maximumWorldSize.y = Mathf.Max(_minimumWorldSize.y, _maximumWorldSize.y);
        _maximumWorldSize.z = Mathf.Max(_minimumWorldSize.z, _maximumWorldSize.z);
        _densityTextureResolution.x = Mathf.Max(8, _densityTextureResolution.x);
        _densityTextureResolution.y = Mathf.Max(8, _densityTextureResolution.y);
        _densityTextureResolution.z = Mathf.Max(8, _densityTextureResolution.z);
    }

    private void OnDrawGizmosSelected()
    {
        if (!_drawGizmos || !_hasValidBounds)
            return;

        Gizmos.color = new Color(0.15f, 0.9f, 1f, 0.2f);
        Gizmos.DrawCube(_worldBounds.center, _worldBounds.size);
        Gizmos.color = new Color(0.15f, 0.9f, 1f, 1f);
        Gizmos.DrawWireCube(_worldBounds.center, _worldBounds.size);
    }
}
