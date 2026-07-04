using UnityEngine;

/// <summary>
/// Procedural mesh renderer for RopeVerlet3D. Rendering is fully separate from simulation.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[DisallowMultipleComponent]
public sealed class RopeMeshRenderer : MonoBehaviour
{
    [SerializeField] private RopeVerlet3D _rope;
    [SerializeField] private BucketMotionController _bucketMotion;

    [Header("Mesh Settings")]
    [SerializeField, Min(0.001f)] private float _ropeRadius = 0.2f;
    [SerializeField, Range(3, 32)] private int _radialSegments = 8;
    [SerializeField, Min(0f)] private float _visualTwistMultiplier = 1f;

    private Mesh _mesh;
    private Vector3[] _vertices;
    private Vector2[] _uvs;
    private int[] _triangles;
    private Vector3[] _ringTangents;
    private Vector3[] _ringRights;
    private Vector3[] _ringUps;

    private void Awake()
    {
        _mesh = new Mesh { name = "Procedural Rope Mesh" };
        _mesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = _mesh;

        if (_rope == null)
            _rope = GetComponent<RopeVerlet3D>();

        if (_bucketMotion == null)
            _bucketMotion = FindFirstObjectByType<BucketMotionController>();
    }

    private void LateUpdate()
    {
        if (_rope == null || _rope.RopeParticles == null || _rope.RopeParticles.Count < 2)
            return;

        BuildMesh();
    }

    private void BuildMesh()
    {
        int ringCount = _rope.RopeParticles.Count;
        int vertexCount = ringCount * _radialSegments;

        EnsureBuffers(ringCount, vertexCount);
        BuildTransportFrames(ringCount);

        float totalTwistDegrees = _bucketMotion != null
            ? _bucketMotion.CurrentTwistAngle * _visualTwistMultiplier
            : 0f;

        for (int ringIndex = 0; ringIndex < ringCount; ringIndex++)
        {
            Vector3 center = _rope.RopeParticles[ringIndex].CurrentPosition;
            float twistRadians = (ringIndex / (float)(ringCount - 1)) * totalTwistDegrees * Mathf.Deg2Rad;

            for (int radialIndex = 0; radialIndex < _radialSegments; radialIndex++)
            {
                float baseAngle = radialIndex / (float)_radialSegments * Mathf.PI * 2f;
                float angle = baseAngle + twistRadians;

                Vector3 offset =
                    Mathf.Cos(angle) * _ringRights[ringIndex] * _ropeRadius +
                    Mathf.Sin(angle) * _ringUps[ringIndex] * _ropeRadius;

                int vertexIndex = ringIndex * _radialSegments + radialIndex;
                _vertices[vertexIndex] = transform.InverseTransformPoint(center + offset);
                _uvs[vertexIndex] = new Vector2(
                    radialIndex / (float)_radialSegments,
                    ringIndex / (float)(ringCount - 1));
            }
        }

        _mesh.Clear();
        _mesh.vertices = _vertices;
        _mesh.triangles = _triangles;
        _mesh.uv = _uvs;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    private void EnsureBuffers(int ringCount, int vertexCount)
    {
        if (_vertices != null && _vertices.Length == vertexCount)
            return;

        _vertices = new Vector3[vertexCount];
        _uvs = new Vector2[vertexCount];
        _triangles = BuildTriangles(ringCount);
        _ringTangents = new Vector3[ringCount];
        _ringRights = new Vector3[ringCount];
        _ringUps = new Vector3[ringCount];
    }

    private void BuildTransportFrames(int ringCount)
    {
        for (int i = 0; i < ringCount; i++)
        {
            Vector3 center = _rope.RopeParticles[i].CurrentPosition;
            Vector3 tangent;

            if (i == 0)
                tangent = _rope.RopeParticles[1].CurrentPosition - center;
            else if (i == ringCount - 1)
                tangent = center - _rope.RopeParticles[i - 1].CurrentPosition;
            else
                tangent = _rope.RopeParticles[i + 1].CurrentPosition - _rope.RopeParticles[i - 1].CurrentPosition;

            tangent = tangent.sqrMagnitude > 0.000001f ? tangent.normalized : Vector3.down;
            _ringTangents[i] = tangent;

            if (i == 0)
            {
                Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.95f
                    ? Vector3.right
                    : Vector3.up;

                _ringRights[i] = Vector3.Cross(reference, tangent).normalized;
                _ringUps[i] = Vector3.Cross(tangent, _ringRights[i]).normalized;
                continue;
            }

            Quaternion turn = Quaternion.FromToRotation(_ringTangents[i - 1], tangent);
            Vector3 right = turn * _ringRights[i - 1];
            right -= tangent * Vector3.Dot(right, tangent);

            if (right.sqrMagnitude < 0.000001f)
                right = Vector3.Cross(_ringUps[i - 1], tangent);

            _ringRights[i] = right.normalized;
            _ringUps[i] = Vector3.Cross(tangent, _ringRights[i]).normalized;
        }
    }

    private int[] BuildTriangles(int ringCount)
    {
        int quadCount = (ringCount - 1) * _radialSegments;
        int[] triangles = new int[quadCount * 6];
        int index = 0;

        for (int ringIndex = 0; ringIndex < ringCount - 1; ringIndex++)
        {
            for (int radialIndex = 0; radialIndex < _radialSegments; radialIndex++)
            {
                int nextRadialIndex = (radialIndex + 1) % _radialSegments;
                int current = ringIndex * _radialSegments + radialIndex;
                int next = ringIndex * _radialSegments + nextRadialIndex;
                int currentAbove = (ringIndex + 1) * _radialSegments + radialIndex;
                int nextAbove = (ringIndex + 1) * _radialSegments + nextRadialIndex;

                triangles[index++] = current;
                triangles[index++] = currentAbove;
                triangles[index++] = next;

                triangles[index++] = next;
                triangles[index++] = currentAbove;
                triangles[index++] = nextAbove;
            }
        }

        return triangles;
    }

    private void OnValidate()
    {
        _ropeRadius = Mathf.Max(0.001f, _ropeRadius);
        _radialSegments = Mathf.Clamp(_radialSegments, 3, 32);
        _visualTwistMultiplier = Mathf.Max(0f, _visualTwistMultiplier);
    }
}
