using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class RopeMeshRenderer : MonoBehaviour {
    [SerializeField] private RopeVerlet3D _rope;

    [Header("Mesh Settings")]
    [SerializeField] private float _ropeRadius = 0.05f;
    [SerializeField] private int _radialSegments = 8;

    [Header("Twist Visual")]
    [SerializeField] private float _twistTurns = 3f;
    [SerializeField] private float _twistSpeed = 0f;

    [Header("Twist Visual From Physics")]
    [SerializeField] private float _visualTwistMultplier = 0.5f;

    private Mesh _mesh;

    private Vector3[] _vertices;
    private Vector2[] _uvs;
    private int[] _triangles;

    private void Awake() {
        _mesh = new Mesh();
        _mesh.name = "Procedural Rope Mesh";

        GetComponent<MeshFilter>().mesh = _mesh;

        if (_rope == null)
            _rope = GetComponent<RopeVerlet3D>();
    }

    private void LateUpdate() {
        if (_rope == null) return;
        if (_rope.RopeSegments == null) return;
        if (_rope.RopeSegments.Count < 2) return;

        BuildMesh();
    }

    private void BuildMesh() {
        int ringCount = _rope.RopeSegments.Count;
        int vertexCount = ringCount * _radialSegments;

        if (_vertices == null || _vertices.Length != vertexCount) {
            _vertices = new Vector3[vertexCount];
            _uvs = new Vector2[vertexCount];
            _triangles = BuildTriangles(ringCount);
        }

        for (int i = 0; i < ringCount; i++) {
            Vector3 center = _rope.RopeSegments[i].CurrentPosition;

            Vector3 forward;

            if (i < ringCount - 1)
                forward = (_rope.RopeSegments[i + 1].CurrentPosition - center).normalized;
            else
                forward = (center - _rope.RopeSegments[i - 1].CurrentPosition).normalized;

            Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;

            if (right == Vector3.zero)
                right = Vector3.Cross(forward, Vector3.right).normalized;

            Vector3 up = Vector3.Cross(right, forward).normalized;

            for (int j = 0; j < _radialSegments; j++) {
                float baseAngle = (j / (float)_radialSegments) * Mathf.PI * 2f;

                float physicsTwistTurns = _rope.TwistAmount * _visualTwistMultplier;

                float twistAngle = (i / (float)(ringCount - 1)) * physicsTwistTurns * Mathf.PI * 2f;

                twistAngle += Time.time * _twistSpeed;


                float angle = baseAngle + twistAngle;
                Vector3 offset =
                    Mathf.Cos(angle) * right * _ropeRadius +
                    Mathf.Sin(angle) * up * _ropeRadius;

                _vertices[i * _radialSegments + j] = center + offset;

                float u = j / (float)_radialSegments;
                float v = i / (float)(ringCount - 1);

                _uvs[i * _radialSegments + j] = new Vector2(u, v);
            }
        }

        _mesh.Clear();
        _mesh.vertices = _vertices;
        _mesh.triangles = _triangles;
        _mesh.uv = _uvs;
        _mesh.RecalculateNormals();
    }

    private int[] BuildTriangles(int ringCount) {
        int quadCount = (ringCount - 1) * _radialSegments;
        int[] triangles = new int[quadCount * 6];

        int index = 0;

        for (int i = 0; i < ringCount - 1; i++) {
            for (int j = 0; j < _radialSegments; j++) {
                int current = i * _radialSegments + j;
                int next = i * _radialSegments + (j + 1) % _radialSegments;

                int currentAbove = (i + 1) * _radialSegments + j;
                int nextAbove = (i + 1) * _radialSegments + (j + 1) % _radialSegments;

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
}