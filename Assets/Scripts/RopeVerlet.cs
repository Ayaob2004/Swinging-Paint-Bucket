using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Verlet rope simulation. It owns rope particles, constraints and the payload endpoint.
/// It intentionally does not own bucket rotation, twist, input, or rendering.
/// </summary>
[DisallowMultipleComponent]
public sealed class RopeVerlet3D : MonoBehaviour
{
    [Header("Rope Topology")]
    [SerializeField, Min(2)] private int _particleCount = 50;
    [SerializeField, Min(0.01f)] private float _totalRopeLength = 14.7f;
    [SerializeField, Min(0.0001f)] private float _ropeParticleMass = 0.05f;

    [Header("Anchor")]
    [SerializeField] private Transform _anchorPoint;

    [Header("Linear Physics")]
    [SerializeField] private Vector3 _gravityAcceleration = new Vector3(0f, -30f, 0f);
    [SerializeField, Range(0.90f, 1f)] private float _linearDamping = 0.997f;
    [SerializeField, Min(1)] private int _constraintIterations = 32;

    [Header("Optional Rope Collision")]
    [SerializeField] private bool _enableRopeCollision;
    [SerializeField] private LayerMask _collisionMask;
    [SerializeField, Min(0.001f)] private float _collisionRadius = 0.1f;
    [SerializeField, Range(0f, 1f)] private float _collisionBounce = 0.15f;

    private readonly List<RopeParticle> _particles = new List<RopeParticle>();
    private readonly Collider[] _collisionBuffer = new Collider[16];

    private float _payloadMass = 1f;
    private float _lengthRatio = 1f;
    private bool _isSimulating;
    private Vector3 _lastEndpointVelocity;

    public IReadOnlyList<RopeParticle> RopeParticles => _particles;
    public IReadOnlyList<RopeParticle> RopeSegments => _particles; // Compatibility for existing references.
    public Transform AnchorPoint => _anchorPoint;
    public bool IsSimulating => _isSimulating;
    public float BaseRopeLength => _totalRopeLength;
    public float EffectiveRopeLength => _totalRopeLength * _lengthRatio;
    public Vector3 GravityAcceleration => _gravityAcceleration;
    public Vector3 LastPosition => _particles.Count == 0 ? transform.position : _particles[_particles.Count - 1].CurrentPosition;
    public Vector3 LastVelocity => _lastEndpointVelocity;

    public Vector3 EndpointTangent
    {
        get
        {
            if (_particles.Count < 2)
                return Vector3.down;

            Vector3 tangent = _particles[_particles.Count - 1].CurrentPosition - _particles[_particles.Count - 2].CurrentPosition;
            return tangent.sqrMagnitude > 0.000001f ? tangent.normalized : Vector3.down;
        }
    }

    private void Awake()
    {
        BuildParticlesIfNeeded();
    }

    private void FixedUpdate()
    {
        if (!_isSimulating || _particles.Count < 2)
            return;

        SimulateParticles();

        for (int iteration = 0; iteration < _constraintIterations; iteration++)
            SolveDistanceConstraints();

        // Collision is intentionally evaluated once after the solver, not inside every iteration.
        if (_enableRopeCollision && _collisionMask.value != 0)
            ResolveRopeCollisions();

        _lastEndpointVelocity = GetParticleVelocity(_particles.Count - 1);
    }

    public void ResetSimulation(Vector3 endpointPosition)
    {
        BuildParticlesIfNeeded();

        Vector3 anchorPosition = GetAnchorPosition();
        for (int i = 0; i < _particles.Count; i++)
        {
            float t = i / (float)(_particles.Count - 1);
            Vector3 position = Vector3.Lerp(anchorPosition, endpointPosition, t);
            RopeParticle particle = _particles[i];
            particle.CurrentPosition = position;
            particle.PreviousPosition = position;
            _particles[i] = particle;
        }

        _lastEndpointVelocity = Vector3.zero;
        _isSimulating = false;
    }

    public void BeginSimulation()
    {
        _isSimulating = true;
    }

    public void StopSimulation()
    {
        _isSimulating = false;
        _lastEndpointVelocity = Vector3.zero;

        for (int i = 0; i < _particles.Count; i++)
        {
            RopeParticle particle = _particles[i];
            particle.PreviousPosition = particle.CurrentPosition;
            _particles[i] = particle;
        }
    }

    public void SetEndpointVelocity(Vector3 velocity)
    {
        if (_particles.Count == 0)
            return;

        int lastIndex = _particles.Count - 1;
        RopeParticle endpoint = _particles[lastIndex];
        endpoint.PreviousPosition = endpoint.CurrentPosition - velocity * Time.fixedDeltaTime;
        _particles[lastIndex] = endpoint;
        _lastEndpointVelocity = velocity;
    }

    public void SetPayloadMass(float payloadMass)
    {
        _payloadMass = Mathf.Max(0.0001f, payloadMass);
    }

    public void SetLengthRatio(float lengthRatio)
    {
        _lengthRatio = Mathf.Clamp(lengthRatio, 0.05f, 1f);
    }

    public Vector3 GetLastSegmentPosition()
    {
        return LastPosition;
    }

    public Vector3 GetAnchorPointPosition()
    {
        return GetAnchorPosition();
    }

    private void BuildParticlesIfNeeded()
    {
        if (_particles.Count == _particleCount)
            return;

        _particles.Clear();
        Vector3 anchorPosition = GetAnchorPosition();
        Vector3 initialDirection = _anchorPoint != null ? -_anchorPoint.up : Vector3.down;
        float segmentLength = _totalRopeLength / (_particleCount - 1);

        for (int i = 0; i < _particleCount; i++)
        {
            Vector3 position = anchorPosition + initialDirection * segmentLength * i;
            _particles.Add(new RopeParticle(position));
        }

        _lastEndpointVelocity = Vector3.zero;
    }

    private void SimulateParticles()
    {
        float deltaTimeSquared = Time.fixedDeltaTime * Time.fixedDeltaTime;

        for (int i = 1; i < _particles.Count; i++)
        {
            RopeParticle particle = _particles[i];
            Vector3 velocity = (particle.CurrentPosition - particle.PreviousPosition) * _linearDamping;

            particle.PreviousPosition = particle.CurrentPosition;
            particle.CurrentPosition += velocity + _gravityAcceleration * deltaTimeSquared;
            _particles[i] = particle;
        }
    }

    private void SolveDistanceConstraints()
    {
        RopeParticle anchorParticle = _particles[0];
        Vector3 anchorPosition = GetAnchorPosition();
        anchorParticle.CurrentPosition = anchorPosition;
        anchorParticle.PreviousPosition = anchorPosition;
        _particles[0] = anchorParticle;

        float desiredSegmentLength = EffectiveRopeLength / (_particles.Count - 1);

        for (int i = 0; i < _particles.Count - 1; i++)
        {
            RopeParticle first = _particles[i];
            RopeParticle second = _particles[i + 1];

            Vector3 delta = second.CurrentPosition - first.CurrentPosition;
            float distance = delta.magnitude;
            if (distance < 0.000001f)
                continue;

            float error = distance - desiredSegmentLength;
            Vector3 correction = delta * (error / distance);

            float firstInverseMass = GetInverseMass(i);
            float secondInverseMass = GetInverseMass(i + 1);
            float inverseMassSum = firstInverseMass + secondInverseMass;
            if (inverseMassSum <= 0f)
                continue;

            first.CurrentPosition += correction * (firstInverseMass / inverseMassSum);
            second.CurrentPosition -= correction * (secondInverseMass / inverseMassSum);

            _particles[i] = first;
            _particles[i + 1] = second;
        }
    }

    private void ResolveRopeCollisions()
    {
        for (int i = 1; i < _particles.Count; i++)
        {
            RopeParticle particle = _particles[i];
            Vector3 velocity = particle.CurrentPosition - particle.PreviousPosition;

            int colliderCount = Physics.OverlapSphereNonAlloc(
                particle.CurrentPosition,
                _collisionRadius,
                _collisionBuffer,
                _collisionMask,
                QueryTriggerInteraction.Ignore);

            for (int colliderIndex = 0; colliderIndex < colliderCount; colliderIndex++)
            {
                Collider collider = _collisionBuffer[colliderIndex];
                if (collider == null)
                    continue;

                Vector3 closestPoint = collider.ClosestPoint(particle.CurrentPosition);
                Vector3 away = particle.CurrentPosition - closestPoint;
                float distance = away.magnitude;

                if (distance >= _collisionRadius)
                    continue;

                Vector3 normal = distance > 0.000001f
                    ? away / distance
                    : (particle.CurrentPosition - collider.bounds.center).normalized;

                if (normal.sqrMagnitude < 0.000001f)
                    normal = Vector3.up;

                particle.CurrentPosition += normal * (_collisionRadius - distance);
                velocity = Vector3.Reflect(velocity, normal) * _collisionBounce;
            }

            particle.PreviousPosition = particle.CurrentPosition - velocity;
            _particles[i] = particle;
        }
    }

    private float GetInverseMass(int particleIndex)
    {
        if (particleIndex == 0)
            return 0f;

        float mass = particleIndex == _particles.Count - 1 ? _payloadMass : _ropeParticleMass;
        return 1f / Mathf.Max(0.0001f, mass);
    }

    private Vector3 GetParticleVelocity(int particleIndex)
    {
        if (particleIndex < 0 || particleIndex >= _particles.Count || Time.fixedDeltaTime <= 0f)
            return Vector3.zero;

        RopeParticle particle = _particles[particleIndex];
        return (particle.CurrentPosition - particle.PreviousPosition) / Time.fixedDeltaTime;
    }

    private Vector3 GetAnchorPosition()
    {
        return _anchorPoint != null ? _anchorPoint.position : transform.position;
    }

    private void OnValidate()
    {
        _particleCount = Mathf.Max(2, _particleCount);
        _totalRopeLength = Mathf.Max(0.01f, _totalRopeLength);
        _ropeParticleMass = Mathf.Max(0.0001f, _ropeParticleMass);
        _constraintIterations = Mathf.Max(1, _constraintIterations);
        _collisionRadius = Mathf.Max(0.001f, _collisionRadius);
    }

    public struct RopeParticle
    {
        public Vector3 CurrentPosition;
        public Vector3 PreviousPosition;

        public RopeParticle(Vector3 position)
        {
            CurrentPosition = position;
            PreviousPosition = position;
        }
    }
}
