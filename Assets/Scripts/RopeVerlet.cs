using UnityEngine;
using System.Collections.Generic;
using Unity.Hierarchy;

[RequireComponent(typeof(LineRenderer))]
public class RopeVerlet3D : MonoBehaviour {
    [Header("Rope")]
    [SerializeField] private int _numOfRopeSegments = 50;
    [SerializeField] private float _ropeSegmentLength = 0.225f;

    [Header("Anchor")]
    [SerializeField] private Transform _anchorPoint;

    [Header("End Ball")]
/*    [SerializeField] private Transform _endBall;*/

    [Header("Physics")]
    [SerializeField] private Vector3 _gravityForce = new Vector3(0f, -50.0f, 0f);
    [SerializeField] private float _dampingFactor = 0.995f;
    [SerializeField] private float _bounceFactor = 0.3f;

    [Header("Twist Physics")]
    [SerializeField] private float _twistAmount = 10f;
    [SerializeField] private float _maxTwistAmount = 10f;
    [SerializeField] private float _twistShorteningFactor = 0.015f;
    [SerializeField] private float _twistVelocity = 0f;
    [SerializeField] private float _twistStiffness = 20f;
    [SerializeField] private float _twistDamping = 2f;

    [Header("Collision")]
    [SerializeField] private LayerMask _collisionMask;
    [SerializeField] private float _collisionRadius = 0.1f;

    [Header("Constraints")]
    [SerializeField] private int _numOfConstraintRuns = 80;

    [Header("Optimizations")]
    [SerializeField] private int _collisionSegmentInterval = 2;

    private LineRenderer _lineRenderer;
    private readonly List<RopeSegment> _ropeSegments = new List<RopeSegment>();
    public IReadOnlyList<RopeSegment> RopeSegments => _ropeSegments;
    public float TwistAmount => _twistAmount;

    private void Awake() {
        _lineRenderer = GetComponent<LineRenderer>();
        _lineRenderer.positionCount = _numOfRopeSegments;

        Vector3 startPoint = _anchorPoint != null ? _anchorPoint.position : transform.position;

        for (int i = 0; i < _numOfRopeSegments; i++) {
            _ropeSegments.Add(new RopeSegment(startPoint));
            startPoint.y -= _ropeSegmentLength;
        }
    }

    private void Update() {
        DrawRope();
/*        UpdateEndBall();*/

        if (Input.GetKeyDown(KeyCode.S)) {
            AddImpulseToLastSegment(Vector3.right * 1f);
        }

        if (Input.GetKeyDown(KeyCode.W)) {
            AddImpulseToLastSegment(Vector3.left * 1f);
        }

        if (Input.GetKeyDown(KeyCode.D)) {
            AddImpulseToLastSegment(Vector3.forward * 1f);
        }

        if (Input.GetKeyDown(KeyCode.A)) {
            AddImpulseToLastSegment(Vector3.back * 1f);
        }
    }

    private void FixedUpdate() {
        SimulateTwist();

        Simulate();

        for (int i = 0; i < _numOfConstraintRuns; i++) {
            ApplyConstraints();

            if (i % _collisionSegmentInterval == 0) {
                HandleCollisions();
            }
        }
    }

    private void Simulate() {
        for (int i = 0; i < _ropeSegments.Count; i++) {
            RopeSegment segment = _ropeSegments[i];

            Vector3 velocity = (segment.CurrentPosition - segment.OldPosition) * _dampingFactor;

            segment.OldPosition = segment.CurrentPosition;
            segment.CurrentPosition += velocity;
            segment.CurrentPosition += _gravityForce * Time.fixedDeltaTime * Time.fixedDeltaTime;


            _ropeSegments[i] = segment;
        }
    }

    private void ApplyConstraints() {
        RopeSegment firstSegment = _ropeSegments[0];
        firstSegment.CurrentPosition = _anchorPoint != null ? _anchorPoint.position : transform.position;
        _ropeSegments[0] = firstSegment;

        for (int i = 0; i < _numOfRopeSegments - 1; i++) {
            RopeSegment currentSeg = _ropeSegments[i];
            RopeSegment nextSeg = _ropeSegments[i + 1];

            Vector3 delta = currentSeg.CurrentPosition - nextSeg.CurrentPosition;
            float dist = delta.magnitude;

            if (dist == 0f)
                continue;

            float currentSegmentLength = GetCurrentSegmentLength();
            float difference = dist - currentSegmentLength;
            Vector3 changeDir = delta.normalized;
            Vector3 changeVector = changeDir * difference;

            if (i != 0) {
                currentSeg.CurrentPosition -= changeVector * 0.5f;
                nextSeg.CurrentPosition += changeVector * 0.5f;
            } else {
                nextSeg.CurrentPosition += changeVector;
            }

            _ropeSegments[i] = currentSeg;
            _ropeSegments[i + 1] = nextSeg;
        }


    }

    private void HandleCollisions() {
        for (int i = 1; i < _ropeSegments.Count; i++) {
            RopeSegment segment = _ropeSegments[i];

            Vector3 velocity = segment.CurrentPosition - segment.OldPosition;

            Collider[] colliders = Physics.OverlapSphere(
                segment.CurrentPosition,
                _collisionRadius,
                _collisionMask
            );

            foreach (Collider collider in colliders) {
                Vector3 closestPoint = collider.ClosestPoint(segment.CurrentPosition);
                float distance = Vector3.Distance(segment.CurrentPosition, closestPoint);

                if (distance < _collisionRadius) {
                    Vector3 normal = (segment.CurrentPosition - closestPoint).normalized;

                    if (normal == Vector3.zero) {
                        normal = (segment.CurrentPosition - collider.transform.position).normalized;
                    }

                    float depth = _collisionRadius - distance;
                    segment.CurrentPosition += normal * depth;

                    velocity = Vector3.Reflect(velocity, normal) * _bounceFactor;
                }
            }

            segment.OldPosition = segment.CurrentPosition - velocity;
            _ropeSegments[i] = segment;
        }
    }

    private void DrawRope() {
        Vector3[] ropePositions = new Vector3[_numOfRopeSegments];

        for (int i = 0; i < _ropeSegments.Count; i++) {
            ropePositions[i] = _ropeSegments[i].CurrentPosition;
        }

        _lineRenderer.SetPositions(ropePositions);
    }

    public struct RopeSegment {
        public Vector3 CurrentPosition;
        public Vector3 OldPosition;

        public RopeSegment(Vector3 position) {
            CurrentPosition = position;
            OldPosition = position;
        }
    }

/*    private void UpdateEndBall() {
        if (_endBall == null) return;

        RopeSegment lastSegment = _ropeSegments[_ropeSegments.Count - 1];
        _endBall.position = lastSegment.CurrentPosition;
    }*/

    private float GetCurrentSegmentLength() {
        float normalizedTwist = Mathf.Clamp01(Mathf.Abs(_twistAmount) / _maxTwistAmount);

        float shortening = normalizedTwist * _twistShorteningFactor;

        return _ropeSegmentLength * (1f - shortening);
    }

    private void SimulateTwist() {
        float restoringForce = -_twistAmount * _twistStiffness;
        float dampingForce = -_twistVelocity * _twistDamping;

        float twistAcceleration = restoringForce + dampingForce;

        _twistVelocity += twistAcceleration * Time.fixedDeltaTime;
        _twistAmount += _twistVelocity * Time.fixedDeltaTime;

        _twistAmount = Mathf.Clamp(TwistAmount, -_maxTwistAmount, _maxTwistAmount);
    }

    public void AddTwist(float amount) {
        _twistAmount = Mathf.Clamp(_twistAmount + amount, -_maxTwistAmount, _maxTwistAmount);
    }

    public void AddImpulseToLastSegment(Vector3 impulse) {
        if (_ropeSegments.Count == 0) {
            return;
        }

        int lastIndex = _ropeSegments.Count - 1;

        RopeSegment lastSegment = _ropeSegments[lastIndex];

        lastSegment.OldPosition -= impulse;

        _ropeSegments[lastIndex] = lastSegment;
    }

    public Vector3 GetLastSegmentPosition()
    {
        return _ropeSegments[_ropeSegments.Count - 1].CurrentPosition;
    }

    public Vector3 GetAnchorPointPosition()
    {
        return _anchorPoint.position;
    }
}