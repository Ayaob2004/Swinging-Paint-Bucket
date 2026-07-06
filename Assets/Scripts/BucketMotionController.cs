using UnityEngine;

/// <summary>
/// Owns all bucket-side state: initial conditions, Inspector launch/reset commands,
/// rope torsion, handle-axis hinge motion, pitch, roll and final Quaternion composition.
/// </summary>
[DisallowMultipleComponent]
public sealed class BucketMotionController : MonoBehaviour
{
    public enum VelocitySpace
    {
        WorldSpace,
        AnchorLocalSpace
    }

    [Header("Required References")]
    [SerializeField] private RopeVerlet3D _rope;
    [SerializeField] private Transform _handle;
    [SerializeField] private Transform _bucket;
    [SerializeField] private Transform _attachLeft;
    [SerializeField] private Transform _attachRight;
    [SerializeField] private Transform _centerOfMass;

    [Header("Initial Rest State")]
    [SerializeField, Range(-85f, 85f)] private float _initialForwardBackSwingAngle;
    [SerializeField, Range(-85f, 85f)] private float _initialLeftRightSwingAngle;
    [SerializeField, Range(-180f, 180f)] private float _initialRopeTwistAngle;
    [SerializeField, Range(-180f, 180f)] private float _initialHingeAngle;
    [SerializeField, Range(-90f, 90f)] private float _initialPitchAngle;
    [SerializeField, Range(-90f, 90f)] private float _initialRollAngle;

    [Header("Initial Motion - Applied Once By Inspector")]
    [SerializeField] private VelocitySpace _velocitySpace = VelocitySpace.WorldSpace;
    [SerializeField] private Vector3 _initialLinearVelocity;
    [SerializeField] private float _initialTwistAngularVelocity;
    [SerializeField] private float _initialHingeAngularVelocity;
    [SerializeField] private float _initialPitchAngularVelocity;
    [SerializeField] private float _initialRollAngularVelocity;

    [Header("Payload")]
    [SerializeField, Min(0.01f)] private float _bucketMass = 1f;

    [Header("Rope Twist Physics")]
    [SerializeField, Min(1f)] private float _maximumTwistAngle = 720f;
    [SerializeField, Min(0.0001f)] private float _twistInertia = 1f;
    [SerializeField, Min(0f)] private float _twistStiffness = 12f;
    [SerializeField, Min(0f)] private float _twistDamping = 1.5f;
    [SerializeField, Range(0f, 0.95f)] private float _twistShorteningFactor = 0.08f;
    [SerializeField, Range(0.05f, 1f)] private float _minimumRopeLengthRatio = 0.75f;

    [Header("Handle Hinge Physics - Axis Derived From Attach Points")]
    [SerializeField, Min(0.0001f)] private float _hingeInertia = 1f;
    [SerializeField, Min(0f)] private float _hingeStiffness = 0f;
    [SerializeField, Min(0f)] private float _hingeDamping = 0.6f;
    [SerializeField] private Vector2 _hingeLimits = new Vector2(-110f, 110f);

    [Header("Bucket Pitch Physics - Local X Axis")]
    [SerializeField, Min(0.0001f)] private float _pitchInertia = 1f;
    [SerializeField, Min(0f)] private float _pitchStiffness = 8f;
    [SerializeField, Min(0f)] private float _pitchDamping = 1.2f;
    [SerializeField] private Vector2 _pitchLimits = new Vector2(-35f, 35f);

    [Header("Bucket Roll Physics - Local Y Axis")]
    [SerializeField, Min(0.0001f)] private float _rollInertia = 1f;
    [SerializeField, Min(0f)] private float _rollStiffness = 8f;
    [SerializeField, Min(0f)] private float _rollDamping = 1.2f;
    [SerializeField] private Vector2 _rollLimits = new Vector2(-35f, 35f);

    [Header("Debug Gizmos")]
    [SerializeField] private bool _showGizmos = true;
    [SerializeField, Min(0.05f)] private float _gizmoAxisLength = 0.75f;

    private Quaternion _baseHandleRotation;
    private Quaternion _baseBucketLocalRotation;
    private Vector3 _baseBucketLocalPosition;
    private Vector3 _hingeAxisLocal;
    private Quaternion _transportRotation;
    private Vector3 _lastRopeDirection;
    private Vector3 _lastEndpointVelocity;
    private bool _basePoseCaptured;
    private bool _simulationRunning;

    private float _twistAngle;
    private float _twistAngularVelocity;
    private float _hingeAngle;
    private float _hingeAngularVelocity;
    private float _pitchAngle;
    private float _pitchAngularVelocity;
    private float _rollAngle;
    private float _rollAngularVelocity;

    public bool IsSimulationRunning => _simulationRunning;
    public float CurrentTwistAngle => _twistAngle;
    public float CurrentHingeAngle => _hingeAngle;
    public float CurrentPitchAngle => _pitchAngle;
    public float CurrentRollAngle => _rollAngle;
    public float BucketMass => _bucketMass;
    public float InitialForwardBackSwingAngle => _initialForwardBackSwingAngle;
    public float InitialLeftRightSwingAngle => _initialLeftRightSwingAngle;
    public float InitialRopeTwistAngle => _initialRopeTwistAngle;
    public Vector3 InitialLinearVelocity => _initialLinearVelocity;
    public float InitialTwistAngularVelocity => _initialTwistAngularVelocity;
    public float InitialHingeAngularVelocity => _initialHingeAngularVelocity;
    public float InitialPitchAngularVelocity => _initialPitchAngularVelocity;
    public float InitialRollAngularVelocity => _initialRollAngularVelocity;

    public float InitialHingeAngle => _initialHingeAngle;
    public float InitialPitchAngle => _initialPitchAngle;
    public float InitialRollAngle => _initialRollAngle;

    public void SetInitialOrientationAngles(
        float forwardBackAngle,
        float leftRightAngle,
        float ropeTwistAngle,
        float hingeAngle,
        float pitchAngle,
        float rollAngle)
    {
        _initialForwardBackSwingAngle = Mathf.Clamp(forwardBackAngle, -85f, 85f);
        _initialLeftRightSwingAngle = Mathf.Clamp(leftRightAngle, -85f, 85f);

        _initialRopeTwistAngle = Mathf.Clamp(ropeTwistAngle, -180f, 180f);
        _initialHingeAngle = Mathf.Clamp(hingeAngle, -180f, 180f);
        _initialPitchAngle = Mathf.Clamp(pitchAngle, -180f, 180f);
        _initialRollAngle = Mathf.Clamp(rollAngle, -180f, 180f);
    }
    public void SetBucketMass(float mass)
    {
        _bucketMass = Mathf.Max(0.01f, mass);

        if (_rope != null)
            _rope.SetPayloadMass(_bucketMass);
    }

    public void SetInitialSwingAngles(float forwardBackAngle, float leftRightAngle)
    {
        _initialForwardBackSwingAngle = Mathf.Clamp(forwardBackAngle, -85f, 85f);
        _initialLeftRightSwingAngle = Mathf.Clamp(leftRightAngle, -85f, 85f);
    }

    public void SetInitialRopeTwistAngle(float twistAngle)
    {
        _initialRopeTwistAngle = Mathf.Clamp(twistAngle, -180f, 180f);
    }

    public void SetInitialLinearVelocity(Vector3 velocity)
    {
        _initialLinearVelocity = velocity;
    }

    public void SetInitialAngularVelocities(
        float twistAngularVelocity,
        float hingeAngularVelocity,
        float pitchAngularVelocity,
        float rollAngularVelocity)
    {
        _initialTwistAngularVelocity = twistAngularVelocity;
        _initialHingeAngularVelocity = hingeAngularVelocity;
        _initialPitchAngularVelocity = pitchAngularVelocity;
        _initialRollAngularVelocity = rollAngularVelocity;
    }
    private void Awake()
    {
        CaptureBasePose();
    }

    /// <summary>
    /// Advances bucket-side rotational physics by one externally controlled simulation step.
    /// RopeVerlet3D must be advanced first for the same step.
    /// </summary>
    public void SimulateStep(float deltaTime)
    {
        if (!_simulationRunning || !HasRequiredReferences() || deltaTime <= 0f)
            return;

        Vector3 ropeDirection = GetSafeRopeDirection();
        UpdateTransportFrame(ropeDirection);

        // First place the visible hierarchy using the previous joint state so the current
        // center of mass can be used to calculate physical torques for this step.
        ApplyPose(ropeDirection);

        UpdateTwist(deltaTime);
        UpdateJointMotion(deltaTime);

        // Twist shortening is sent to the rope as one source of truth.
        _rope.SetLengthRatio(GetCurrentRopeLengthRatio());

        ropeDirection = GetSafeRopeDirection();
        ApplyPose(ropeDirection);
    }

    /// <summary>
    /// Captures the authored pose once and prepares the initial paused state.
    /// SimulationCoordinator calls this during scene initialization.
    /// </summary>
    public void InitializeState()
    {
        CaptureBasePose();
        ResetToInitialState();
    }

    public void ResetToInitialState()
    {
        CaptureBasePose();

        if (!HasRequiredReferences())
        {
            Debug.LogError("BucketMotionController needs Rope, Handle, Bucket, Attach Left, Attach Right and Center Of Mass references.", this);
            return;
        }

        _simulationRunning = false;
        _rope.StopSimulation();
        _rope.SetPayloadMass(_bucketMass);

        _twistAngle = Mathf.Clamp(_initialRopeTwistAngle, -_maximumTwistAngle, _maximumTwistAngle);
        _twistAngularVelocity = 0f;
        _hingeAngle = ClampToLimits(_initialHingeAngle, _hingeLimits);
        _hingeAngularVelocity = 0f;
        _pitchAngle = ClampToLimits(_initialPitchAngle, _pitchLimits);
        _pitchAngularVelocity = 0f;
        _rollAngle = ClampToLimits(_initialRollAngle, _rollLimits);
        _rollAngularVelocity = 0f;

        _rope.SetLengthRatio(GetCurrentRopeLengthRatio());

        Vector3 initialDirection = GetInitialRopeDirection();
        Vector3 endpointPosition = _rope.GetAnchorPointPosition() + initialDirection * _rope.EffectiveRopeLength;
        _rope.ResetSimulation(endpointPosition);

        _transportRotation = CreateInitialTransportRotation(initialDirection);
        _lastRopeDirection = initialDirection;
        _lastEndpointVelocity = Vector3.zero;

        ApplyPose(initialDirection);
    }

    public void ApplyInitialMotion(float simulationStepDeltaTime)
    {
        if (_simulationRunning || !HasRequiredReferences())
            return;

        Vector3 linearVelocity = _velocitySpace == VelocitySpace.AnchorLocalSpace
            ? _rope.AnchorPoint.TransformDirection(_initialLinearVelocity)
            : _initialLinearVelocity;

        _twistAngularVelocity = _initialTwistAngularVelocity;
        _hingeAngularVelocity = _initialHingeAngularVelocity;
        _pitchAngularVelocity = _initialPitchAngularVelocity;
        _rollAngularVelocity = _initialRollAngularVelocity;

        _rope.SetPayloadMass(_bucketMass);
        _rope.SetEndpointVelocity(linearVelocity, simulationStepDeltaTime);
        _rope.BeginSimulation();
        _lastEndpointVelocity = linearVelocity;
        _simulationRunning = true;
    }

    /// <summary>
    /// Compatibility overload. SimulationCoordinator should pass its exact substep duration.
    /// </summary>
    public void ApplyInitialMotion()
    {
        ApplyInitialMotion(Time.fixedDeltaTime);
    }

    public void StopSimulation()
    {
        _simulationRunning = false;
        if (_rope != null)
            _rope.StopSimulation();
    }

    public void ResetAndApplyInitialMotion(float simulationStepDeltaTime)
    {
        ResetToInitialState();
        ApplyInitialMotion(simulationStepDeltaTime);
    }

    private void CaptureBasePose()
    {
        if (_basePoseCaptured || _handle == null || _bucket == null || _attachLeft == null || _attachRight == null)
            return;

        _baseHandleRotation = _handle.rotation;
        _baseBucketLocalRotation = _bucket.localRotation;
        _baseBucketLocalPosition = _bucket.localPosition;

        Vector3 hingeAxisWorld = _attachRight.position - _attachLeft.position;
        _hingeAxisLocal = hingeAxisWorld.sqrMagnitude > 0.000001f
            ? _handle.InverseTransformDirection(hingeAxisWorld.normalized).normalized
            : Vector3.forward;

        _basePoseCaptured = true;
    }

    private void UpdateTransportFrame(Vector3 ropeDirection)
    {
        if (_lastRopeDirection.sqrMagnitude < 0.000001f)
        {
            _transportRotation = CreateInitialTransportRotation(ropeDirection);
            _lastRopeDirection = ropeDirection;
            return;
        }

        Quaternion directionChange = Quaternion.FromToRotation(_lastRopeDirection, ropeDirection);
        _transportRotation = directionChange * _transportRotation;
        _lastRopeDirection = ropeDirection;
    }

    private Quaternion CreateInitialTransportRotation(Vector3 ropeDirection)
    {
        Vector3 baseRopeDirection = _baseHandleRotation * Vector3.down;
        return Quaternion.FromToRotation(baseRopeDirection, ropeDirection) * _baseHandleRotation;
    }

    private void UpdateTwist(float dt)
    {
        float restoringTorque = -_twistAngle * _twistStiffness;
        float dampingTorque = -_twistAngularVelocity * _twistDamping;
        float angularAcceleration = (restoringTorque + dampingTorque) / _twistInertia;

        _twistAngularVelocity += angularAcceleration * dt;
        _twistAngle += _twistAngularVelocity * dt;

        ClampAngularState(ref _twistAngle, ref _twistAngularVelocity, -_maximumTwistAngle, _maximumTwistAngle);
    }

    private void UpdateJointMotion(float dt)
    {
        Vector3 endpointVelocity = _rope.LastVelocity;
        Vector3 endpointAcceleration = (endpointVelocity - _lastEndpointVelocity) / Mathf.Max(dt, 0.000001f);
        _lastEndpointVelocity = endpointVelocity;

        Vector3 hingePivot = (_attachLeft.position + _attachRight.position) * 0.5f;
        Vector3 centerOfMassOffset = _centerOfMass.position - hingePivot;
        Vector3 effectiveAcceleration = _rope.GravityAcceleration - endpointAcceleration;
        Vector3 torque = Vector3.Cross(centerOfMassOffset, effectiveAcceleration * _bucketMass);

        Vector3 hingeAxis = (_attachRight.position - _attachLeft.position).normalized;
        if (hingeAxis.sqrMagnitude < 0.000001f)
            hingeAxis = _handle.TransformDirection(_hingeAxisLocal).normalized;

        Vector3 pitchAxis = _handle.TransformDirection(Vector3.right).normalized;
        Vector3 rollAxis = _handle.TransformDirection(Vector3.up).normalized;

        IntegrateJointAxis(
            ref _hingeAngle,
            ref _hingeAngularVelocity,
            Vector3.Dot(torque, hingeAxis),
            _hingeInertia,
            _hingeStiffness,
            _hingeDamping,
            _hingeLimits,
            dt);

        IntegrateJointAxis(
            ref _pitchAngle,
            ref _pitchAngularVelocity,
            Vector3.Dot(torque, pitchAxis),
            _pitchInertia,
            _pitchStiffness,
            _pitchDamping,
            _pitchLimits,
            dt);

        IntegrateJointAxis(
            ref _rollAngle,
            ref _rollAngularVelocity,
            Vector3.Dot(torque, rollAxis),
            _rollInertia,
            _rollStiffness,
            _rollDamping,
            _rollLimits,
            dt);
    }

    private void IntegrateJointAxis(
        ref float angle,
        ref float angularVelocity,
        float externalTorque,
        float inertia,
        float stiffness,
        float damping,
        Vector2 limits,
        float dt)
    {
        float restoringTorque = -stiffness * angle;
        float dampingTorque = -damping * angularVelocity;
        float angularAcceleration = (externalTorque + restoringTorque + dampingTorque) / Mathf.Max(inertia, 0.0001f);

        angularVelocity += angularAcceleration * dt;
        angle += angularVelocity * dt;

        ClampAngularState(ref angle, ref angularVelocity, limits.x, limits.y);
    }

    private void ApplyPose(Vector3 ropeDirection)
    {
        if (_rope == null || _handle == null || _bucket == null)
            return;

        _handle.position = _rope.LastPosition;
        Quaternion axialTwist = Quaternion.AngleAxis(_twistAngle, ropeDirection);
        _handle.rotation = axialTwist * _transportRotation;

        Quaternion hingeRotation = Quaternion.AngleAxis(_hingeAngle, _hingeAxisLocal);
        Quaternion pitchRotation = Quaternion.AngleAxis(_pitchAngle, Vector3.right);
        Quaternion rollRotation = Quaternion.AngleAxis(_rollAngle, Vector3.up);

        _bucket.localPosition = _baseBucketLocalPosition;
        _bucket.localRotation = _baseBucketLocalRotation * hingeRotation * pitchRotation * rollRotation;
    }

    private float GetCurrentRopeLengthRatio()
    {
        float normalizedTwist = Mathf.Clamp01(Mathf.Abs(_twistAngle) / _maximumTwistAngle);
        float shortenedRatio = 1f - normalizedTwist * _twistShorteningFactor;
        return Mathf.Max(_minimumRopeLengthRatio, shortenedRatio);
    }

    private Vector3 GetInitialRopeDirection()
    {
        Transform anchor = _rope.AnchorPoint;
        Vector3 right = anchor != null ? anchor.right : Vector3.right;
        Vector3 forward = anchor != null ? anchor.forward : Vector3.forward;

        Quaternion sideRotation = Quaternion.AngleAxis(_initialLeftRightSwingAngle, forward);
        Quaternion forwardRotation = Quaternion.AngleAxis(_initialForwardBackSwingAngle, right);
        Vector3 direction = sideRotation * forwardRotation * Vector3.down;

        return direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.down;
    }

    private Vector3 GetSafeRopeDirection()
    {
        Vector3 direction = _rope.EndpointTangent;
        if (direction.sqrMagnitude < 0.000001f)
            direction = _lastRopeDirection.sqrMagnitude > 0.000001f ? _lastRopeDirection : Vector3.down;

        return direction.normalized;
    }

    private bool HasRequiredReferences()
    {
        return _rope != null && _handle != null && _bucket != null &&
               _attachLeft != null && _attachRight != null && _centerOfMass != null;
    }

    private static void ClampAngularState(ref float angle, ref float angularVelocity, float minimum, float maximum)
    {
        float clampedAngle = Mathf.Clamp(angle, minimum, maximum);
        if (!Mathf.Approximately(angle, clampedAngle))
        {
            bool pushingBeyondUpperLimit = angle > maximum && angularVelocity > 0f;
            bool pushingBeyondLowerLimit = angle < minimum && angularVelocity < 0f;
            if (pushingBeyondUpperLimit || pushingBeyondLowerLimit)
                angularVelocity = 0f;
        }

        angle = clampedAngle;
    }

    private static float ClampToLimits(float value, Vector2 limits)
    {
        return Mathf.Clamp(value, Mathf.Min(limits.x, limits.y), Mathf.Max(limits.x, limits.y));
    }

    private void OnValidate()
    {
        _bucketMass = Mathf.Max(0.01f, _bucketMass);
        _maximumTwistAngle = Mathf.Max(1f, _maximumTwistAngle);
        _twistInertia = Mathf.Max(0.0001f, _twistInertia);
        _hingeInertia = Mathf.Max(0.0001f, _hingeInertia);
        _pitchInertia = Mathf.Max(0.0001f, _pitchInertia);
        _rollInertia = Mathf.Max(0.0001f, _rollInertia);
        _minimumRopeLengthRatio = Mathf.Clamp(_minimumRopeLengthRatio, 0.05f, 1f);
    }

    private void OnDrawGizmosSelected()
    {
        if (!_showGizmos || _handle == null || _bucket == null)
            return;

        Vector3 pivot = _attachLeft != null && _attachRight != null
            ? (_attachLeft.position + _attachRight.position) * 0.5f
            : _bucket.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(pivot, pivot + _handle.TransformDirection(_hingeAxisLocal) * _gizmoAxisLength);

        Gizmos.color = Color.red;
        Gizmos.DrawLine(pivot, pivot + _handle.right * _gizmoAxisLength);

        Gizmos.color = Color.green;
        Gizmos.DrawLine(pivot, pivot + _handle.up * _gizmoAxisLength);

        if (_centerOfMass != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(_centerOfMass.position, _gizmoAxisLength * 0.08f);
            Gizmos.DrawLine(pivot, _centerOfMass.position);
        }
    }
}
