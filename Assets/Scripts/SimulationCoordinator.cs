using Seb.Fluid.Collision;
using Seb.Fluid.Simulation;
using UnityEngine;

/// <summary>
/// The single lifecycle and timing owner for rope, bucket, moving mesh collisions and fluid.
/// No participating simulation component advances itself from Update or FixedUpdate.
/// </summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class SimulationCoordinator : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private RopeVerlet3D _rope;
    [SerializeField] private BucketMotionController _bucketMotion;
    [SerializeField] private FluidSim _fluidSim;
    [SerializeField] private MeshCollisionBuilder _meshCollisionBuilder;
    [SerializeField] private DynamicFluidRenderVolume _renderVolume;

    [Header("Optional Painting Reset")]
    [SerializeField] private PaintCanvasPainter _paintCanvasPainter;
    [SerializeField] private PaintGpuImpactDetector _paintImpactDetector;
    [SerializeField] private bool _clearCanvasOnReset = true;

    [Header("Fixed-Step Synchronization")]
    [SerializeField, Min(1)] private int _fluidSubstepsPerFixedStep = 2;

    private bool _isInitialized;
    private bool _isRunning;

    public bool IsInitialized => _isInitialized;
    public bool IsRunning => _isRunning;
    public int FluidSubstepsPerFixedStep => _fluidSubstepsPerFixedStep;

    private void Start()
    {
        InitializeSimulation();
    }

    private void FixedUpdate()
    {
        if (!_isInitialized || !_isRunning)
            return;

        float substepDeltaTime = Time.fixedDeltaTime / _fluidSubstepsPerFixedStep;

        for (int substep = 0; substep < _fluidSubstepsPerFixedStep; substep++)
        {
            // Preserve the former rope -> bucket execution order for every shared substep.
            _rope.SimulateStep(substepDeltaTime);
            _bucketMotion.SimulateStep(substepDeltaTime);

            // The GPU collision data must describe this exact bucket pose before fluid advances.
            _meshCollisionBuilder.UpdateDynamicObjects();
            _fluidSim.SimulateSubstep(substepDeltaTime);
        }

        // Density baking is rendering work. It is done once after all physics substeps.
        _renderVolume.UpdateBounds(_fluidSim.gravity);
        _fluidSim.BakeDensityMap();
    }

    public void InitializeSimulation()
    {
        if (_isInitialized)
            return;

        ResolveMissingReferences();
        if (!HasRequiredReferences())
        {
            Debug.LogError(
                "SimulationCoordinator needs RopeVerlet3D, BucketMotionController, FluidSim, MeshCollisionBuilder and DynamicFluidRenderVolume references.",
                this);
            return;
        }

        _bucketMotion.InitializeState();
        _renderVolume.ResetHistory();
        _renderVolume.UpdateBounds(_fluidSim.gravity);
        _fluidSim.InitializeSimulation();
        if (!_fluidSim.IsInitialized)
            return;

        _meshCollisionBuilder.SynchronizeDynamicObjects();
        _fluidSim.SetSimulationRunning(false);
        _fluidSim.BakeDensityMap(forceDensityUpdate: true);

        _isInitialized = true;
        _isRunning = false;
    }

    public void ResetToInitialState()
    {
        if (!_isInitialized)
        {
            InitializeSimulation();
            return;
        }

        _isRunning = false;
        _fluidSim.SetSimulationRunning(false);
        _bucketMotion.ResetToInitialState();

        // Reset is a teleport, never a moving collision. Synchronize before respawning fluid.
        _meshCollisionBuilder.SynchronizeDynamicObjects();
        _renderVolume.ResetHistory();
        _renderVolume.UpdateBounds(_fluidSim.gravity);
        _fluidSim.ResetFluidAtCurrentSpawn();

        ResetPaintingCanvas();

        _meshCollisionBuilder.SynchronizeDynamicObjects();
        _renderVolume.UpdateBounds(_fluidSim.gravity);
        _fluidSim.BakeDensityMap(forceDensityUpdate: true);
    }

    public void ApplyInitialMotion()
    {
        if (!_isInitialized)
        {
            InitializeSimulation();
            if (!_isInitialized)
                return;
        }

        if (_isRunning)
            return;

        float substepDeltaTime = Time.fixedDeltaTime / _fluidSubstepsPerFixedStep;
        _bucketMotion.ApplyInitialMotion(substepDeltaTime);
        _fluidSim.SetSimulationRunning(true);
        _isRunning = _bucketMotion.IsSimulationRunning && _fluidSim.IsSimulationRunning;
    }

    public void ResetAndApplyInitialMotion()
    {
        ResetToInitialState();
        ApplyInitialMotion();
    }

    public void StopSimulation()
    {
        if (!_isInitialized)
            return;

        _isRunning = false;
        _bucketMotion.StopSimulation();
        _fluidSim.SetSimulationRunning(false);
        _meshCollisionBuilder.SynchronizeDynamicObjects();
        _renderVolume.UpdateBounds(_fluidSim.gravity);
        _fluidSim.BakeDensityMap(forceDensityUpdate: true);
    }

    private void ResolveMissingReferences()
    {
        if (_bucketMotion == null)
            _bucketMotion = FindAnyObjectByType<BucketMotionController>();

        if (_rope == null && _bucketMotion != null)
            _rope = FindAnyObjectByType<RopeVerlet3D>();

        if (_fluidSim == null)
            _fluidSim = FindAnyObjectByType<FluidSim>();

        if (_meshCollisionBuilder == null && _fluidSim != null)
            _meshCollisionBuilder = _fluidSim.meshCollisionBuilder;

        if (_meshCollisionBuilder == null)
            _meshCollisionBuilder = FindAnyObjectByType<MeshCollisionBuilder>();

        if (_renderVolume == null)
            _renderVolume = FindAnyObjectByType<DynamicFluidRenderVolume>();

        if (_fluidSim != null && _fluidSim.renderVolume == null)
            _fluidSim.renderVolume = _renderVolume;
        
        if (_paintCanvasPainter == null)
            _paintCanvasPainter = FindAnyObjectByType<PaintCanvasPainter>();

        if (_paintImpactDetector == null)
            _paintImpactDetector = FindAnyObjectByType<PaintGpuImpactDetector>();
    }

    private bool HasRequiredReferences()
    {
        return _rope != null && _bucketMotion != null &&
               _fluidSim != null && _meshCollisionBuilder != null && _renderVolume != null;
    }

    private void OnValidate()
    {
        _fluidSubstepsPerFixedStep = Mathf.Max(1, _fluidSubstepsPerFixedStep);
    }

    public void ClearPaintingCanvas()
{
    if (_paintCanvasPainter != null)
        _paintCanvasPainter.ClearPaint();

    if (_paintImpactDetector != null)
        _paintImpactDetector.ResetPreviousPositions();
}

private void ResetPaintingCanvas()
{
    if (!_clearCanvasOnReset)
        return;

    ClearPaintingCanvas();
}

public void SetFluidSubstepsPerFixedStep(int substeps)
{
    _fluidSubstepsPerFixedStep = Mathf.Max(1, substeps);
}

}
