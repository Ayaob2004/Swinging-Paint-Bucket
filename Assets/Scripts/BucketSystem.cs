using UnityEngine;

public class BucketSystem : MonoBehaviour
{
    [Header("Rope Reference")]
    public RopeVerlet3D rope;
    public Transform handle;

    [Header("Bucket")]
    public Transform bucket;
    public Transform attachLeft;
    public Transform attachRight;


    [Header("Bucket Swing Physics")]
    [SerializeField] private float _swingStiffness = 40f;
    [SerializeField] private float _swingDamping = 6f;
    [SerializeField] private float _maxSwingAngle = 60f;

    [Header("Twist")]
    [SerializeField] private float _twistRotationMultiplier = 180f;

    [Header("Twist Input")]
    [SerializeField] private KeyCode _twistLeftKey = KeyCode.Q;
    [SerializeField] private KeyCode _twistRightKey = KeyCode.E;
    [SerializeField] private float _twistInputRate = 5f;

    private float _swingAngle;
    private float _swingVelocity;

    private float _twistStable;
    private Quaternion _handleBaseRotation;

    private const float ThresholdAngle = 0.05f;
    private const float ThresholdVelocity = 0.05f;

    void Start()
    {
        if (handle != null)
            _handleBaseRotation = handle.rotation;
    }

    void Update()
    {
        HandleTwistInput();

        if (rope == null || handle == null || bucket == null) return;


        handle.position = rope.GetLastSegmentPosition();

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        var segments = rope.RopeSegments;
        if (segments == null || segments.Count < 2)
            return;


        Vector3 rightDir = (attachRight.position - attachLeft.position).normalized;
        Vector3 lastPos = segments[^1].CurrentPosition;
        Vector3 prevPos = segments[^2].CurrentPosition;
        Vector3 ropeDir = (lastPos - prevPos).normalized;

        Vector3 ropeDirPlanar = ropeDir - rightDir * Vector3.Dot(ropeDir, rightDir);

        if (ropeDirPlanar == Vector3.zero)
            return;

        ropeDirPlanar.Normalize();


        float targetAngle = Vector3.SignedAngle(Vector3.down, ropeDirPlanar, rightDir);
        float error = targetAngle - _swingAngle;


        float springForce = error * _swingStiffness;
        float dampingForce = -_swingVelocity * _swingDamping;
        float swingAcceleration = springForce + dampingForce;

        _swingVelocity += swingAcceleration * dt;
        _swingAngle += _swingVelocity * dt;


        if (Mathf.Abs(error) < ThresholdAngle && Mathf.Abs(_swingVelocity) < ThresholdVelocity)
        {
            _swingAngle = targetAngle;
            _swingVelocity = 0f;
        }

        _swingAngle = Mathf.Clamp(_swingAngle, -_maxSwingAngle, _maxSwingAngle);

        Vector3 localAxis = bucket.parent.InverseTransformDirection(rightDir);
        Quaternion swingRotation = Quaternion.AngleAxis(_swingAngle, localAxis);

        bucket.localRotation = Quaternion.Slerp(bucket.localRotation, swingRotation, 25f * dt);

        _twistStable = Mathf.Lerp(_twistStable, rope.TwistAmount, 15f * dt);
        float twistAngle = _twistStable * _twistRotationMultiplier;

        Vector3 twistAxis = handle.up;
        if (twistAxis == Vector3.zero) twistAxis = Vector3.up;
        Quaternion twistRotation = Quaternion.AngleAxis(twistAngle, twistAxis);

        handle.rotation = Quaternion.Slerp(handle.rotation, twistRotation * _handleBaseRotation, 20f * dt);
    }

    private void HandleTwistInput()
    {
        if (rope == null)
            return;

        if (Input.GetKey(_twistLeftKey))
        {
            rope.AddTwist(_twistInputRate * Time.deltaTime);
        }
        else if (Input.GetKey(_twistRightKey))
        {
            rope.AddTwist(-_twistInputRate * Time.deltaTime);
        }
    }
}