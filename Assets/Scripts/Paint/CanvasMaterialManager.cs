using UnityEngine;

public class CanvasMaterialManager : MonoBehaviour
{
    public enum CanvasType 
    { 
        Paper, 
        Fabric, 
        Wood, 
        Metal 
    }

    [System.Serializable]
    public class CanvasSurfaceSettings
    {
        public CanvasType type;
        public Material material;

        [Header("Paint Behaviour")]
        [Range(0f, 1f)] public float absorption = 0.5f;
        [Range(0f, 1f)] public float spread = 0.5f;
        [Range(0f, 1f)] public float adhesion = 0.8f;
        [Range(0f, 1f)] public float roughness = 0.5f;
        [Range(0f, 1f)] public float gloss = 0.2f;
        [Range(0f, 1f)] public float dryingRate = 0.5f;
        [Range(0f, 1f)] public float satelliteStrength = 0.4f;
        [Range(0f, 1f)] public float fingerStrength = 0.4f;
        [Range(0f, 1f)] public float anisotropy = 0f;
    }

    [Header("Canvas Settings")]
    public CanvasType currentType = CanvasType.Fabric;

    [Header("Surface Settings")]
    public CanvasSurfaceSettings paper = new CanvasSurfaceSettings
    {
        type = CanvasType.Paper,
        absorption = 0.8f,
        spread = 0.6f,
        adhesion = 0.8f,
        roughness = 0.4f,
        gloss = 0.1f,
        dryingRate = 0.8f,
        satelliteStrength = 0.25f,
        fingerStrength = 0.35f,
        anisotropy = 0f
    };

    public CanvasSurfaceSettings fabric = new CanvasSurfaceSettings
    {
        type = CanvasType.Fabric,
        absorption = 0.9f,
        spread = 0.8f,
        adhesion = 0.85f,
        roughness = 0.8f,
        gloss = 0.05f,
        dryingRate = 0.45f,
        satelliteStrength = 0.4f,
        fingerStrength = 0.7f,
        anisotropy = 0.5f
    };

    public CanvasSurfaceSettings wood = new CanvasSurfaceSettings
    {
        type = CanvasType.Wood,
        absorption = 0.45f,
        spread = 0.55f,
        adhesion = 0.7f,
        roughness = 0.55f,
        gloss = 0.35f,
        dryingRate = 0.5f,
        satelliteStrength = 0.25f,
        fingerStrength = 0.5f,
        anisotropy = 0.8f
    };

    public CanvasSurfaceSettings metal = new CanvasSurfaceSettings
    {
        type = CanvasType.Metal,
        absorption = 0.05f,
        spread = 0.25f,
        adhesion = 0.35f,
        roughness = 0.15f,
        gloss = 0.9f,
        dryingRate = 0.15f,
        satelliteStrength = 0.65f,
        fingerStrength = 0.15f,
        anisotropy = 0f
    };

    private MeshRenderer meshRenderer;

    public CanvasSurfaceSettings CurrentSurfaceSettings { get; private set; }

    void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
    }

    void Start()
    {
        ApplyMaterial(currentType);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) SetCanvasType(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SetCanvasType(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SetCanvasType(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SetCanvasType(3);
    }

    public void SetCanvasType(int typeIndex)
    {
        currentType = (CanvasType)typeIndex;
        ApplyMaterial(currentType);
    }

    public void SetCanvasType(CanvasType type)
    {
        currentType = type;
        ApplyMaterial(currentType);
    }

    private void ApplyMaterial(CanvasType type)
    {
        if (meshRenderer == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        CurrentSurfaceSettings = GetSettings(type);

        if (meshRenderer != null && CurrentSurfaceSettings != null && CurrentSurfaceSettings.material != null)
        {
            meshRenderer.material = CurrentSurfaceSettings.material;
        }

        Debug.Log(
            $"Canvas changed to: {type} | " +
            $"Absorption={CurrentSurfaceSettings.absorption}, " +
            $"Spread={CurrentSurfaceSettings.spread}, " +
            $"Roughness={CurrentSurfaceSettings.roughness}"
        );
    }

    public CanvasSurfaceSettings GetSettings(CanvasType type)
    {
        switch (type)
        {
            case CanvasType.Paper:
                return paper;

            case CanvasType.Fabric:
                return fabric;

            case CanvasType.Wood:
                return wood;

            case CanvasType.Metal:
                return metal;

            default:
                return fabric;
        }
    }

    public CanvasSurfaceSettings GetCurrentSettings()
    {
        if (CurrentSurfaceSettings == null)
        {
            CurrentSurfaceSettings = GetSettings(currentType);
        }

        return CurrentSurfaceSettings;
    }
}