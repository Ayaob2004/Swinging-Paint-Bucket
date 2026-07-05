using Seb.Fluid.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class SimulationUIController : MonoBehaviour
{
    [Header("Simulation References")]
    [SerializeField] private SimulationCoordinator simulationCoordinator;
    [SerializeField] private RopeVerlet3D rope;
    [SerializeField] private BucketMotionController bucketMotion;
    [SerializeField] private FluidSim fluidSim;
    [SerializeField] private Spawner3D spawner;
    [SerializeField] private PaintCanvasPainter paintCanvasPainter;
    [SerializeField] private PaintGpuImpactDetector paintImpactDetector;
    [SerializeField] private CanvasMaterialManager canvasMaterialManager;
    [SerializeField] private Transform canvasTransform;

    [Header("Main Buttons")]
    [SerializeField] private Button applySettingsButton;
    [SerializeField] private Button startButton;
    [SerializeField] private Button stopButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private Button clearCanvasButton;
    [SerializeField] private Button savePaintingButton;

    [Header("Rope And Bucket UI")]
    [SerializeField] private Slider ropeLengthSlider;
    [SerializeField] private TMP_Text ropeLengthValueText;

    [SerializeField] private Slider bucketMassSlider;
    [SerializeField] private TMP_Text bucketMassValueText;

    [SerializeField] private Slider forwardBackAngleSlider;
    [SerializeField] private TMP_Text forwardBackAngleValueText;

    [SerializeField] private Slider leftRightAngleSlider;
    [SerializeField] private TMP_Text leftRightAngleValueText;

    [SerializeField] private TMP_InputField velocityXInput;
    [SerializeField] private TMP_InputField velocityYInput;
    [SerializeField] private TMP_InputField velocityZInput;

    [Header("Fluid And Paint UI")]
    [SerializeField] private Slider gravitySlider;
    [SerializeField] private TMP_Text gravityValueText;

    [SerializeField] private Slider viscositySlider;
    [SerializeField] private TMP_Text viscosityValueText;

    [SerializeField] private Slider particleDensitySlider;
    [SerializeField] private TMP_Text particleDensityValueText;

    [SerializeField] private Slider paintMassSlider;
    [SerializeField] private TMP_Text paintMassValueText;

    [SerializeField] private Slider particleDiameterSlider;
    [SerializeField] private TMP_Text particleDiameterValueText;

    [SerializeField] private TMP_Dropdown paintRegionDropdown;
    [SerializeField] private Slider paintRedSlider;
    [SerializeField] private Slider paintGreenSlider;
    [SerializeField] private Slider paintBlueSlider;
    [SerializeField] private Image paintColorPreview;

    [Header("Canvas UI")]
    [SerializeField] private TMP_Dropdown canvasTypeDropdown;

    [SerializeField] private Slider canvasWidthSlider;
    [SerializeField] private TMP_Text canvasWidthValueText;

    [SerializeField] private Slider canvasHeightSlider;
    [SerializeField] private TMP_Text canvasHeightValueText;

    [SerializeField] private Slider canvasInclineSlider;
    [SerializeField] private TMP_Text canvasInclineValueText;

    [Header("Runtime Result UI")]
    [SerializeField] private TMP_Text runningStateText;
    [SerializeField] private TMP_Text simulationTimeText;
    [SerializeField] private TMP_Text particleCountText;
    [SerializeField] private TMP_Text impactCountText;
    [SerializeField] private TMP_Text currentValuesText;

    [Header("Behaviour")]
    [SerializeField] private bool resetSimulationAfterApply = true;
    [SerializeField] private bool applySettingsBeforeStart = true;

    private float simulationTimer;
    private bool listenersRegistered;
    private int selectedPaintRegionIndex;

    private void Awake()
    {
        ResolveMissingReferences();
    }

    private void Start()
    {
        SetupCanvasTypeDropdown();
        SetupPaintRegionDropdown();
        LoadValuesFromScene();
        RegisterListeners();
        RefreshAllValueLabels();
        RefreshRuntimeTexts();
    }

    private void Update()
    {
        if (simulationCoordinator != null && simulationCoordinator.IsRunning)
            simulationTimer += Time.deltaTime;

        RefreshRuntimeTexts();
    }

    private void OnDestroy()
    {
        UnregisterListeners();
    }

    private void ResolveMissingReferences()
    {
        if (simulationCoordinator == null)
            simulationCoordinator = FindAnyObjectByType<SimulationCoordinator>();

        if (rope == null)
            rope = FindAnyObjectByType<RopeVerlet3D>();

        if (bucketMotion == null)
            bucketMotion = FindAnyObjectByType<BucketMotionController>();

        if (fluidSim == null)
            fluidSim = FindAnyObjectByType<FluidSim>();

        if (spawner == null)
            spawner = FindAnyObjectByType<Spawner3D>();

        if (paintCanvasPainter == null)
            paintCanvasPainter = FindAnyObjectByType<PaintCanvasPainter>();

        if (paintImpactDetector == null)
            paintImpactDetector = FindAnyObjectByType<PaintGpuImpactDetector>();

        if (canvasMaterialManager == null)
            canvasMaterialManager = FindAnyObjectByType<CanvasMaterialManager>();

        if (canvasTransform == null && canvasMaterialManager != null)
            canvasTransform = canvasMaterialManager.transform;
    }

    private void RegisterListeners()
    {
        if (listenersRegistered)
            return;

        listenersRegistered = true;

        AddButtonListener(applySettingsButton, ApplySettings);
        AddButtonListener(startButton, StartSimulation);
        AddButtonListener(stopButton, StopSimulation);
        AddButtonListener(resetButton, ResetSimulation);
        AddButtonListener(clearCanvasButton, ClearCanvas);
        AddButtonListener(savePaintingButton, SavePainting);

        AddSliderListener(ropeLengthSlider, _ => RefreshAllValueLabels());
        AddSliderListener(bucketMassSlider, _ => RefreshAllValueLabels());
        AddSliderListener(forwardBackAngleSlider, _ => RefreshAllValueLabels());
        AddSliderListener(leftRightAngleSlider, _ => RefreshAllValueLabels());
        AddSliderListener(gravitySlider, _ => RefreshAllValueLabels());
        AddSliderListener(viscositySlider, _ => RefreshAllValueLabels());
        AddSliderListener(particleDensitySlider, _ => RefreshAllValueLabels());
        AddSliderListener(paintMassSlider, _ => RefreshAllValueLabels());
        AddSliderListener(particleDiameterSlider, _ => RefreshAllValueLabels());
        AddSliderListener(canvasWidthSlider, _ => RefreshAllValueLabels());
        AddSliderListener(canvasHeightSlider, _ => RefreshAllValueLabels());
        AddSliderListener(canvasInclineSlider, _ => RefreshAllValueLabels());

        AddSliderListener(paintRedSlider, _ => RefreshPaintColorPreview());
        AddSliderListener(paintGreenSlider, _ => RefreshPaintColorPreview());
        AddSliderListener(paintBlueSlider, _ => RefreshPaintColorPreview());
        if (paintRegionDropdown != null)
        paintRegionDropdown.onValueChanged.AddListener(OnPaintRegionChanged);
    }

    private void UnregisterListeners()
    {
        if (!listenersRegistered)
            return;

        listenersRegistered = false;

        if (applySettingsButton != null) applySettingsButton.onClick.RemoveListener(ApplySettings);
        if (startButton != null) startButton.onClick.RemoveListener(StartSimulation);
        if (stopButton != null) stopButton.onClick.RemoveListener(StopSimulation);
        if (resetButton != null) resetButton.onClick.RemoveListener(ResetSimulation);
        if (clearCanvasButton != null) clearCanvasButton.onClick.RemoveListener(ClearCanvas);
        if (savePaintingButton != null) savePaintingButton.onClick.RemoveListener(SavePainting);
        if (paintRegionDropdown != null) paintRegionDropdown.onValueChanged.RemoveListener(OnPaintRegionChanged);
        RemoveAllSliderListeners(ropeLengthSlider);
        RemoveAllSliderListeners(bucketMassSlider);
        RemoveAllSliderListeners(forwardBackAngleSlider);
        RemoveAllSliderListeners(leftRightAngleSlider);
        RemoveAllSliderListeners(gravitySlider);
        RemoveAllSliderListeners(viscositySlider);
        RemoveAllSliderListeners(particleDensitySlider);
        RemoveAllSliderListeners(paintMassSlider);
        RemoveAllSliderListeners(particleDiameterSlider);
        RemoveAllSliderListeners(paintRedSlider);
        RemoveAllSliderListeners(paintGreenSlider);
        RemoveAllSliderListeners(paintBlueSlider);
        RemoveAllSliderListeners(canvasWidthSlider);
        RemoveAllSliderListeners(canvasHeightSlider);
        RemoveAllSliderListeners(canvasInclineSlider);
    }

    private void AddButtonListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
            button.onClick.AddListener(action);
    }

    private void AddSliderListener(Slider slider, UnityEngine.Events.UnityAction<float> action)
    {
        if (slider != null)
            slider.onValueChanged.AddListener(action);
    }

    private void RemoveAllSliderListeners(Slider slider)
    {
        if (slider != null)
            slider.onValueChanged.RemoveAllListeners();
    }

    private void SetupCanvasTypeDropdown()
    {
        if (canvasTypeDropdown == null)
            return;

        canvasTypeDropdown.ClearOptions();

        canvasTypeDropdown.AddOptions(new System.Collections.Generic.List<string>
        {
            "Paper",
            "Fabric",
            "Wood",
            "Metal"
        });
    }
    private void SetupPaintRegionDropdown()
    {
        if (paintRegionDropdown == null)
            return;

        paintRegionDropdown.ClearOptions();

        int count = spawner != null ? spawner.SpawnRegionCount : 0;

        if (count <= 0)
        {
            paintRegionDropdown.AddOptions(new System.Collections.Generic.List<string>
            {
                "No Paint Regions"
            });

            paintRegionDropdown.interactable = false;
            selectedPaintRegionIndex = 0;
            return;
        }

        System.Collections.Generic.List<string> options = new System.Collections.Generic.List<string>();

        for (int i = 0; i < count; i++)
        {
            options.Add("Paint Region " + i);
        }

        paintRegionDropdown.AddOptions(options);
        paintRegionDropdown.interactable = true;

        selectedPaintRegionIndex = Mathf.Clamp(selectedPaintRegionIndex, 0, count - 1);
        paintRegionDropdown.SetValueWithoutNotify(selectedPaintRegionIndex);
        paintRegionDropdown.RefreshShownValue();

        LoadSelectedPaintRegionColorToUI();
    }
    private void OnPaintRegionChanged(int index)
    {
        selectedPaintRegionIndex = index;
        LoadSelectedPaintRegionColorToUI();
        RefreshPaintColorPreview();
    }

    private void LoadSelectedPaintRegionColorToUI()
    {
        if (spawner == null || spawner.SpawnRegionCount <= 0)
            return;

        selectedPaintRegionIndex = Mathf.Clamp(
            selectedPaintRegionIndex,
            0,
            spawner.SpawnRegionCount - 1
        );

        Color color = spawner.GetSpawnRegionColor(selectedPaintRegionIndex);

        SetSliderValueSilent(paintRedSlider, color.r);
        SetSliderValueSilent(paintGreenSlider, color.g);
        SetSliderValueSilent(paintBlueSlider, color.b);

        RefreshPaintColorPreview();
    }
    private void LoadValuesFromScene()
    {
        if (rope != null)
        {
            SetSliderValueSilent(ropeLengthSlider, rope.BaseRopeLength);
        }

        if (bucketMotion != null)
        {
            SetSliderValueSilent(bucketMassSlider, bucketMotion.BucketMass);
            SetSliderValueSilent(forwardBackAngleSlider, bucketMotion.InitialForwardBackSwingAngle);
            SetSliderValueSilent(leftRightAngleSlider, bucketMotion.InitialLeftRightSwingAngle);

            SetInputText(velocityXInput, bucketMotion.InitialLinearVelocity.x);
            SetInputText(velocityYInput, bucketMotion.InitialLinearVelocity.y);
            SetInputText(velocityZInput, bucketMotion.InitialLinearVelocity.z);
        }

        if (fluidSim != null)
        {
            SetSliderValueSilent(gravitySlider, fluidSim.gravity);
            SetSliderValueSilent(viscositySlider, fluidSim.viscosityStrength);
        }

        if (spawner != null)
        {
            SetSliderValueSilent(particleDensitySlider, spawner.ParticleSpawnDensity);

            SetupPaintRegionDropdown();
            LoadSelectedPaintRegionColorToUI();
        }

        if (paintImpactDetector != null)
        {
            SetSliderValueSilent(paintMassSlider, paintImpactDetector.paintMass);
            SetSliderValueSilent(particleDiameterSlider, paintImpactDetector.particleDiameter);
            SetSliderValueSilent(canvasInclineSlider, paintImpactDetector.inclineAngleDegrees);
        }

        if (canvasMaterialManager != null && canvasTypeDropdown != null)
        {
            canvasTypeDropdown.SetValueWithoutNotify((int)canvasMaterialManager.currentType);
            canvasTypeDropdown.RefreshShownValue();
        }

        if (canvasTransform != null)
        {
            SetSliderValueSilent(canvasWidthSlider, canvasTransform.localScale.x);
            SetSliderValueSilent(canvasHeightSlider, canvasTransform.localScale.z);
        }

        RefreshPaintColorPreview();
    }

    private void SetSliderValueSilent(Slider slider, float value)
    {
        if (slider != null)
            slider.SetValueWithoutNotify(value);
    }

    private void SetInputText(TMP_InputField input, float value)
    {
        if (input != null)
            input.text = value.ToString("0.###");
    }

    public void ApplySettings()
    {
        ResolveMissingReferences();

        ApplyRopeAndBucketSettings();
        ApplyFluidAndPaintSettings();
        ApplyCanvasSettings();

        if (resetSimulationAfterApply && simulationCoordinator != null)
        {
            simulationTimer = 0f;
            simulationCoordinator.ResetToInitialState();
        }

        RefreshAllValueLabels();
        RefreshRuntimeTexts();
    }

    private void ApplyRopeAndBucketSettings()
    {
        if (rope != null)
        {
            float ropeLength = GetSliderValue(ropeLengthSlider, rope.BaseRopeLength);
            rope.SetBaseRopeLength(ropeLength);
        }

        if (bucketMotion != null)
        {
            float mass = GetSliderValue(bucketMassSlider, bucketMotion.BucketMass);
            float forwardBack = GetSliderValue(forwardBackAngleSlider, bucketMotion.InitialForwardBackSwingAngle);
            float leftRight = GetSliderValue(leftRightAngleSlider, bucketMotion.InitialLeftRightSwingAngle);

            float vx = GetInputFloat(velocityXInput, bucketMotion.InitialLinearVelocity.x);
            float vy = GetInputFloat(velocityYInput, bucketMotion.InitialLinearVelocity.y);
            float vz = GetInputFloat(velocityZInput, bucketMotion.InitialLinearVelocity.z);

            bucketMotion.SetBucketMass(mass);
            bucketMotion.SetInitialSwingAngles(forwardBack, leftRight);
            bucketMotion.SetInitialLinearVelocity(new Vector3(vx, vy, vz));
        }
    }

    private void ApplyFluidAndPaintSettings()
    {
        if (fluidSim != null)
        {
            float gravity = GetSliderValue(gravitySlider, fluidSim.gravity);
            float viscosity = GetSliderValue(viscositySlider, fluidSim.viscosityStrength);

            fluidSim.gravity = gravity;
            fluidSim.viscosityStrength = Mathf.Max(0f, viscosity);

            if (rope != null)
                rope.SetGravityAcceleration(new Vector3(0f, gravity, 0f));
        }

        if (spawner != null)
        {
            int density = Mathf.RoundToInt(GetSliderValue(particleDensitySlider, spawner.ParticleSpawnDensity));
            Color paintColor = GetPaintColorFromUI();

            spawner.SetParticleSpawnDensity(density);

            if (spawner.SpawnRegionCount > 0)
            {
                selectedPaintRegionIndex = Mathf.Clamp(
                    selectedPaintRegionIndex,
                    0,
                    spawner.SpawnRegionCount - 1
                );

                spawner.SetSpawnRegionColor(selectedPaintRegionIndex, paintColor);
            }
        }

        if (paintImpactDetector != null)
        {
            paintImpactDetector.paintMass = Mathf.Max(0.001f, GetSliderValue(paintMassSlider, paintImpactDetector.paintMass));
            paintImpactDetector.particleDiameter = Mathf.Max(0.001f, GetSliderValue(particleDiameterSlider, paintImpactDetector.particleDiameter));
        }
    }

    private void ApplyCanvasSettings()
    {
        if (canvasMaterialManager != null && canvasTypeDropdown != null)
        {
            canvasMaterialManager.SetCanvasType(canvasTypeDropdown.value);
        }

        if (canvasTransform != null)
        {
            float width = Mathf.Max(0.1f, GetSliderValue(canvasWidthSlider, canvasTransform.localScale.x));
            float height = Mathf.Max(0.1f, GetSliderValue(canvasHeightSlider, canvasTransform.localScale.z));

            Vector3 scale = canvasTransform.localScale;
            scale.x = width;
            scale.z = height;
            canvasTransform.localScale = scale;
        }

        if (paintImpactDetector != null)
        {
            float incline = GetSliderValue(canvasInclineSlider, paintImpactDetector.inclineAngleDegrees);
            paintImpactDetector.inclineAngleDegrees = incline;
        }
    }

    public void StartSimulation()
    {
        if (simulationCoordinator == null)
            return;

        if (applySettingsBeforeStart)
            ApplySettings();

        simulationTimer = 0f;
        simulationCoordinator.ApplyInitialMotion();
        RefreshRuntimeTexts();
    }

    public void StopSimulation()
    {
        if (simulationCoordinator == null)
            return;

        simulationCoordinator.StopSimulation();
        RefreshRuntimeTexts();
    }

    public void ResetSimulation()
    {
        if (simulationCoordinator == null)
            return;

        simulationTimer = 0f;
        simulationCoordinator.ResetToInitialState();
        RefreshRuntimeTexts();
    }

    public void ClearCanvas()
    {
        if (simulationCoordinator != null)
        {
            simulationCoordinator.ClearPaintingCanvas();
        }
        else if (paintCanvasPainter != null)
        {
            paintCanvasPainter.ClearPaint();

            if (paintImpactDetector != null)
                paintImpactDetector.ResetPreviousPositions();
        }

        RefreshRuntimeTexts();
    }

    public void SavePainting()
    {
        if (paintCanvasPainter != null)
            paintCanvasPainter.SavePaintingNow();
    }

    private float GetSliderValue(Slider slider, float fallback)
    {
        return slider != null ? slider.value : fallback;
    }

    private float GetInputFloat(TMP_InputField input, float fallback)
    {
        if (input == null)
            return fallback;

        if (float.TryParse(input.text, out float value))
            return value;

        return fallback;
    }

    private Color GetPaintColorFromUI()
    {
        float r = GetSliderValue(paintRedSlider, 1f);
        float g = GetSliderValue(paintGreenSlider, 0f);
        float b = GetSliderValue(paintBlueSlider, 0f);

        return new Color(r, g, b, 1f);
    }

    private void RefreshPaintColorPreview()
    {
        if (paintColorPreview != null)
            paintColorPreview.color = GetPaintColorFromUI();
    }

    private void RefreshAllValueLabels()
    {
        SetText(
            ropeLengthValueText,
            "Rope Length: " + GetSliderValue(ropeLengthSlider, 0f).ToString("0.00")
        );

        SetText(
            bucketMassValueText,
            "Bucket Mass: " + GetSliderValue(bucketMassSlider, 0f).ToString("0.00")
        );

        SetText(
            forwardBackAngleValueText,
            "Forward Angle: " + GetSliderValue(forwardBackAngleSlider, 0f).ToString("0.0") + "°"
        );

        SetText(
            leftRightAngleValueText,
            "Left/Right Angle: " + GetSliderValue(leftRightAngleSlider, 0f).ToString("0.0") + "°"
        );

        SetText(
            gravityValueText,
            "Gravity: " + GetSliderValue(gravitySlider, 0f).ToString("0.00")
        );

        SetText(
            viscosityValueText,
            "Viscosity: " + GetSliderValue(viscositySlider, 0f).ToString("0.00")
        );

        SetText(
            particleDensityValueText,
            "Paint Amount: " + Mathf.RoundToInt(GetSliderValue(particleDensitySlider, 0f)).ToString()
        );

        SetText(
            paintMassValueText,
            "Paint Mass: " + GetSliderValue(paintMassSlider, 0f).ToString("0.00")
        );

        SetText(
            particleDiameterValueText,
            "Particle Diameter: " + GetSliderValue(particleDiameterSlider, 0f).ToString("0.000")
        );

        SetText(
            canvasWidthValueText,
            "Canvas Width: " + GetSliderValue(canvasWidthSlider, 0f).ToString("0.00")
        );

        SetText(
            canvasHeightValueText,
            "Canvas Height: " + GetSliderValue(canvasHeightSlider, 0f).ToString("0.00")
        );

        SetText(
            canvasInclineValueText,
            "Canvas Incline: " + GetSliderValue(canvasInclineSlider, 0f).ToString("0.0") + "°"
        );

        RefreshPaintColorPreview();
    }

    private void RefreshRuntimeTexts()
    {
        bool isRunning = simulationCoordinator != null && simulationCoordinator.IsRunning;

        SetText(runningStateText, isRunning ? "Running" : "Stopped");
        SetText(simulationTimeText, simulationTimer.ToString("0.00") + " s");

        if (fluidSim != null)
            SetText(particleCountText, fluidSim.ParticleCount.ToString());

        if (paintCanvasPainter != null)
            SetText(impactCountText, paintCanvasPainter.totalImpacts.ToString());

        if (currentValuesText != null)
        {
            string canvasType = canvasMaterialManager != null
                ? canvasMaterialManager.currentType.ToString()
                : "Unknown";

            float gravity = fluidSim != null ? fluidSim.gravity : 0f;
            float viscosity = fluidSim != null ? fluidSim.viscosityStrength : 0f;
            float ropeLength = rope != null ? rope.BaseRopeLength : 0f;

            currentValuesText.text =
                "Current Experiment Values\n" +
                "Rope Length: " + ropeLength.ToString("0.00") + "\n" +
                "Gravity: " + gravity.ToString("0.00") + "\n" +
                "Viscosity: " + viscosity.ToString("0.00") + "\n" +
                "Canvas Type: " + canvasType + "\n" +
                "Selected Paint Region: " + selectedPaintRegionIndex + "\n" +
                "Paint Color: #" + ColorUtility.ToHtmlStringRGB(GetPaintColorFromUI());
        }
    }

    private void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value;
    }
}