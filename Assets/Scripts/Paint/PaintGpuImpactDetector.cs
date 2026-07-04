using UnityEngine;
using Seb.Fluid.Simulation;

public class PaintGpuImpactDetector : MonoBehaviour
{
    [Header("References")]
    public FluidSim fluidSim;
    public PaintCanvasPainter painter;
    public CanvasMaterialManager canvasMaterialManager;
    public Transform canvasTransform;

    [Header("Compute")]
    public ComputeShader impactDetectorCompute;

    [Header("Canvas Plane")]
    public CanvasUVPlane canvasPlane = CanvasUVPlane.LocalXZ;

    [Header("Detection")]
    public bool enableDetection = true;

    [Tooltip("ننفذ الكشف كل N frames لتخفيف الضغط.")]
    [Min(1)]
    public int checkEveryNFrames = 2;

    public float particleDiameter = 0.06f;
    public float paintMass = 0.35f;
    public float minImpactSpeed = 0.35f;

    [Header("Inclined Canvas Model")]
    [Tooltip("إذا مفعّل، يأخذ زاوية الميلان من هذه القيم بدل حسابها من دوران اللوحة.")]
    public bool useManualIncline = true;

    [Range(0f, 75f)]
    public float inclineAngleDegrees = 0f;

    [Tooltip("اتجاه النزول على UV. 0 يعني باتجاه +U، 90 يعني باتجاه +V.")]
    [Range(0f, 360f)]
    public float downhillDirectionDegrees = 90f;

    [Tooltip("إذا مفعّل، يطبّق الميلان على دوران اللوحة نفسها في المشهد.")]
    public bool applyInclineToCanvasTransform = false;

    public enum InclineRotationAxis
    {
        LocalU,
        LocalV
    }

    public InclineRotationAxis inclineRotationAxis = InclineRotationAxis.LocalU;

    [Tooltip("قوة تمدد البقعة باتجاه النزول.")]
    [Range(0f, 2.5f)]
    public float inclineStretchStrength = 1.25f;

    [Tooltip("قوة إزاحة مركز البقعة باتجاه النزول.")]
    [Range(0f, 1.5f)]
    public float inclineShiftStrength = 0.45f;

    [Tooltip("قوة الذيل أو السيلان باتجاه النزول.")]
    [Range(0f, 3f)]
    public float inclineTailStrength = 1.35f;

    [Tooltip("كم يقل عدد الرذاذ/fingers مع زيادة الميلان.")]
    [Range(0f, 1f)]
    public float inclineFingerDamping = 0.65f;

    Quaternion initialCanvasRotation;
    bool hasInitialRotation;

    [Header("GPU Impact Buffer")]
    public int maxImpactCount = 512;

    [Tooltip("عدد الرذاذ الأقصى لكل particle hit. كلما زاد الرقم زادت الكلفة.")]
    [Range(0, 12)]
    public int maxSatellitesPerHit = 4;

    [Header("Debug")]
    public bool logInitialization = true;

    ComputeBuffer previousPositionsBuffer;
    ComputeBuffer depositedBuffer;
    ComputeBuffer impactBuffer;
    ComputeBuffer impactCounterBuffer;

    int clearKernel;
    int detectKernel;
    int copyKernel;
    int clearDepositedKernel;

    int frameCounter;
    int cachedParticleCount;
    bool initializedPreviousPositions;

    public ComputeBuffer ImpactBuffer => impactBuffer;
    public int MaxImpactCount => maxImpactCount;

    void Reset()
    {
        fluidSim = FindAnyObjectByType<FluidSim>();
        painter = FindAnyObjectByType<PaintCanvasPainter>();
        canvasMaterialManager = painter != null ? painter.GetComponent<CanvasMaterialManager>() : null;
        canvasTransform = painter != null ? painter.transform : null;
    }

    void Awake()
    {
        if (fluidSim == null)
        {
            fluidSim = FindAnyObjectByType<FluidSim>();
        }

        if (painter == null)
        {
            painter = FindAnyObjectByType<PaintCanvasPainter>();
        }

        if (canvasMaterialManager == null && painter != null)
        {
            canvasMaterialManager = painter.GetComponent<CanvasMaterialManager>();
        }

        if (canvasTransform == null && painter != null)
        {
            canvasTransform = painter.transform;
        }

        if (canvasTransform != null && !hasInitialRotation)
        {
            initialCanvasRotation = canvasTransform.rotation;
            hasInitialRotation = true;
        }

        if (impactDetectorCompute != null)
        {
            clearKernel = impactDetectorCompute.FindKernel("ClearImpacts");
            detectKernel = impactDetectorCompute.FindKernel("DetectCanvasImpacts");
            copyKernel = impactDetectorCompute.FindKernel("CopyCurrentToPrevious");
            clearDepositedKernel = impactDetectorCompute.FindKernel("ClearDeposited");

        }
    }

    void Update()
    {
        ApplyCanvasInclineIfNeeded();
    }

    void LateUpdate()
    {
        if (!enableDetection)
        {
            return;
        }

        if (fluidSim == null || painter == null || impactDetectorCompute == null)
        {
            return;
        }

        if (fluidSim.positionBuffer == null || fluidSim.velocityBuffer == null || fluidSim.colorBuffer == null)
        {
            return;
        }

        frameCounter++;

        if (frameCounter % checkEveryNFrames != 0)
        {
            return;
        }

        int particleCount = fluidSim.positionBuffer.count;

        EnsureBuffers(particleCount);

        if (!initializedPreviousPositions)
        {
            DispatchCopyCurrentToPrevious(particleCount);
            initializedPreviousPositions = true;
            return;
        }

        DispatchClearImpacts();
        DispatchDetectImpacts(particleCount);

        painter.DispatchGpuImpactBuffer(impactBuffer, maxImpactCount);

        DispatchCopyCurrentToPrevious(particleCount);
    }

    void ApplyCanvasInclineIfNeeded()
    {
        if (!applyInclineToCanvasTransform || canvasTransform == null)
        {
            return;
        }

        if (!hasInitialRotation)
        {
            initialCanvasRotation = canvasTransform.rotation;
            hasInitialRotation = true;
        }

        Vector3 axis;

        if (inclineRotationAxis == InclineRotationAxis.LocalU)
        {
            axis = canvasTransform.right;
        }
        else
        {
            if (canvasPlane == CanvasUVPlane.LocalXZ)
            {
                axis = canvasTransform.forward;
            }
            else
            {
                axis = canvasTransform.up;
            }
        }

        canvasTransform.rotation = Quaternion.AngleAxis(inclineAngleDegrees, axis) * initialCanvasRotation;
    }

    void EnsureBuffers(int particleCount)
    {
        bool needsRebuild =
            previousPositionsBuffer == null ||
            impactBuffer == null ||
            impactCounterBuffer == null ||
            cachedParticleCount != particleCount;

        if (!needsRebuild)
        {
            return;
        }

        ReleaseBuffers();

        cachedParticleCount = particleCount;

        previousPositionsBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
        depositedBuffer = new ComputeBuffer(particleCount, sizeof(uint));
        impactBuffer = new ComputeBuffer(maxImpactCount, GpuPaintImpact.Stride);
        impactCounterBuffer = new ComputeBuffer(1, sizeof(uint));

        initializedPreviousPositions = false;

        DispatchClearDeposited(particleCount);

        if (logInitialization)
        {
            Debug.Log(
                $"[PaintGpuImpactDetector] Buffers created. Particles={particleCount}, MaxImpacts={maxImpactCount}"
            );
        }
    }

    void DispatchClearImpacts()
    {
        impactDetectorCompute.SetInt("maxImpactCount", maxImpactCount);

        impactDetectorCompute.SetBuffer(clearKernel, "ImpactBuffer", impactBuffer);
        impactDetectorCompute.SetBuffer(clearKernel, "ImpactCounter", impactCounterBuffer);

        int groups = Mathf.CeilToInt(maxImpactCount / 64f);
        impactDetectorCompute.Dispatch(clearKernel, groups, 1, 1);
    }

    void DispatchDetectImpacts(int particleCount)
    {
        CanvasMaterialManager.CanvasSurfaceSettings settings = null;

        if (canvasMaterialManager != null)
        {
            settings = canvasMaterialManager.GetCurrentSettings();
        }

        if (settings == null)
        {
            Debug.LogWarning("[PaintGpuImpactDetector] Missing canvas surface settings.");
            return;
        }

        Vector2 downhillUV;
        float effectiveInclineAngle;

        ResolveInclineData(out effectiveInclineAngle, out downhillUV);

        impactDetectorCompute.SetInt("particleCount", particleCount);
        impactDetectorCompute.SetInt("maxImpactCount", maxImpactCount);
        impactDetectorCompute.SetInt("canvasPlane", canvasPlane == CanvasUVPlane.LocalXZ ? 0 : 1);
        impactDetectorCompute.SetInt("maxSatellitesPerHit", maxSatellitesPerHit);

        impactDetectorCompute.SetMatrix("worldToCanvas", canvasTransform.worldToLocalMatrix);

        impactDetectorCompute.SetVector("localURange", painter.localURange);
        impactDetectorCompute.SetVector("localVRange", painter.localVRange);
        impactDetectorCompute.SetFloat("localSurfaceOffset", painter.localSurfaceOffset);

        impactDetectorCompute.SetFloat("minImpactSpeed", minImpactSpeed);
        impactDetectorCompute.SetFloat("paintMass", paintMass);
        impactDetectorCompute.SetFloat("particleDiameter", particleDiameter);

        impactDetectorCompute.SetFloat("absorption", settings.absorption);
        impactDetectorCompute.SetFloat("spread", settings.spread);
        impactDetectorCompute.SetFloat("roughness", settings.roughness);
        impactDetectorCompute.SetFloat("satelliteStrength", settings.satelliteStrength);
        impactDetectorCompute.SetFloat("fingerStrength", settings.fingerStrength);
        impactDetectorCompute.SetFloat("anisotropy", settings.anisotropy);

        impactDetectorCompute.SetFloat("inclineAngleDegrees", effectiveInclineAngle);
        impactDetectorCompute.SetVector("downhillUV", downhillUV);
        impactDetectorCompute.SetFloat("inclineStretchStrength", inclineStretchStrength);
        impactDetectorCompute.SetFloat("inclineShiftStrength", inclineShiftStrength);
        impactDetectorCompute.SetFloat("inclineTailStrength", inclineTailStrength);
        impactDetectorCompute.SetFloat("inclineFingerDamping", inclineFingerDamping);

        impactDetectorCompute.SetBuffer(detectKernel, "Positions", fluidSim.positionBuffer);
        impactDetectorCompute.SetBuffer(detectKernel, "Velocities", fluidSim.velocityBuffer);
        impactDetectorCompute.SetBuffer(detectKernel, "Colors", fluidSim.colorBuffer);

        impactDetectorCompute.SetBuffer(detectKernel, "PreviousPositionsRead", previousPositionsBuffer);
        impactDetectorCompute.SetBuffer(detectKernel, "DepositedBuffer", depositedBuffer);

        impactDetectorCompute.SetBuffer(detectKernel, "ImpactBuffer", impactBuffer);
        impactDetectorCompute.SetBuffer(detectKernel, "ImpactCounter", impactCounterBuffer);

        int groups = Mathf.CeilToInt(particleCount / 64f);
        impactDetectorCompute.Dispatch(detectKernel, groups, 1, 1);
    }
    void DispatchClearDeposited(int particleCount)
    {
        if (impactDetectorCompute == null || depositedBuffer == null)
        {
            return;
        }

        impactDetectorCompute.SetInt("particleCount", particleCount);
        impactDetectorCompute.SetBuffer(clearDepositedKernel, "DepositedBuffer", depositedBuffer);

        int groups = Mathf.CeilToInt(particleCount / 64f);
        impactDetectorCompute.Dispatch(clearDepositedKernel, groups, 1, 1);
    }
    void ResolveInclineData(out float angleDegrees, out Vector2 downhillUV)
    {
        if (useManualIncline)
        {
            angleDegrees = inclineAngleDegrees;

            float rad = downhillDirectionDegrees * Mathf.Deg2Rad;
            downhillUV = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

            if (downhillUV.sqrMagnitude < 0.0001f)
            {
                downhillUV = Vector2.up;
            }

            downhillUV.Normalize();
            return;
        }

        Vector3 gravityWorld = Physics.gravity;

        if (gravityWorld.sqrMagnitude < 0.0001f || canvasTransform == null)
        {
            angleDegrees = 0f;
            downhillUV = Vector2.up;
            return;
        }

        Vector3 gravityLocal = canvasTransform.InverseTransformDirection(gravityWorld.normalized);

        Vector3 normalLocal;

        if (canvasPlane == CanvasUVPlane.LocalXZ)
        {
            normalLocal = Vector3.up;
        }
        else
        {
            normalLocal = -Vector3.forward;
        }

        float normalDot = Mathf.Abs(Vector3.Dot(-gravityLocal.normalized, normalLocal.normalized));
        normalDot = Mathf.Clamp01(normalDot);

        angleDegrees = Mathf.Acos(normalDot) * Mathf.Rad2Deg;

        Vector2 dir;

        if (canvasPlane == CanvasUVPlane.LocalXZ)
        {
            dir = new Vector2(gravityLocal.x, gravityLocal.z);
        }
        else
        {
            dir = new Vector2(gravityLocal.x, gravityLocal.y);
        }

        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = Vector2.up;
        }

        dir.Normalize();
        downhillUV = dir;
    }

    void DispatchCopyCurrentToPrevious(int particleCount)
    {
        impactDetectorCompute.SetInt("particleCount", particleCount);

        impactDetectorCompute.SetBuffer(copyKernel, "Positions", fluidSim.positionBuffer);
        impactDetectorCompute.SetBuffer(copyKernel, "PreviousPositionsWrite", previousPositionsBuffer);

        int groups = Mathf.CeilToInt(particleCount / 64f);
        impactDetectorCompute.Dispatch(copyKernel, groups, 1, 1);
    }

    [ContextMenu("Reset Previous Positions")]
    public void ResetPreviousPositions()
    {
        initializedPreviousPositions = false;

        if (cachedParticleCount > 0)
        {
            DispatchClearDeposited(cachedParticleCount);
        }

        Debug.Log("[PaintGpuImpactDetector] Previous positions and deposited flags will be reset on next update.");
    }

    void OnValidate()
    {
        inclineAngleDegrees = Mathf.Clamp(inclineAngleDegrees, 0f, 75f);
        maxImpactCount = Mathf.Max(1, maxImpactCount);
        checkEveryNFrames = Mathf.Max(1, checkEveryNFrames);
    }

    void OnDestroy()
    {
        ReleaseBuffers();
    }

    void ReleaseBuffers()
    {
        if (previousPositionsBuffer != null)
        {
            previousPositionsBuffer.Release();
            previousPositionsBuffer = null;
        }
        if (depositedBuffer != null)
        {
            depositedBuffer.Release();
            depositedBuffer = null;
        }
        if (impactBuffer != null)
        {
            impactBuffer.Release();
            impactBuffer = null;
        }

        if (impactCounterBuffer != null)
        {
            impactCounterBuffer.Release();
            impactCounterBuffer = null;
        }
    }
}