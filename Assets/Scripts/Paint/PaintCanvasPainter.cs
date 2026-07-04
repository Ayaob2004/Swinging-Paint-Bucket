using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using UnityEngine.Rendering;

public enum CanvasUVPlane
{
    LocalXZ,
    LocalXY
}
public struct GpuPaintImpact
{
    public Vector4 centerRadius;
    public Vector4 color;
    public Vector4 parameters0;
    public Vector4 parameters1;

    public const int Stride = sizeof(float) * 16;

    public static GpuPaintImpact Create(
        Vector2 centerUV,
        Color color,
        float radiusU,
        float radiusV,
        float angleRad,
        float intensity,
        float edgeNoise,
        float wetnessAmount)
    {
        color.a = 1f;

        return new GpuPaintImpact
        {
            centerRadius = new Vector4(centerUV.x, centerUV.y, radiusU, radiusV),
            color = new Vector4(color.r, color.g, color.b, color.a),
            parameters0 = new Vector4(angleRad, intensity, edgeNoise, wetnessAmount),
            parameters1 = new Vector4(1f, 0f, 0f, 0f)
        };
    }
}
public class PaintCanvasPainter : MonoBehaviour
{
    [Header("References")]
    public CanvasMaterialManager canvasMaterialManager;

    [Header("Canvas Mapping")]
    public CanvasUVPlane uvPlane = CanvasUVPlane.LocalXZ;

    public Vector2 localURange = new Vector2(-0.5f, 0.5f);
    public Vector2 localVRange = new Vector2(-0.5f, 0.5f);

    [Header("GPU Painting")]
    public bool useGpuPainting = true;
    public ComputeShader paintCompute;

    [Min(64)]
    public int textureResolution = 512;

    [Header("Wetness / Drying")]
    public bool enableDrying = true;
    public float dryingUpdateInterval = 0.08f;
    public float dryingSpeedMultiplier = 0.35f;

    [Range(0f, 1f)]
    public float wetVisualBoost = 0.25f;

    [Header("Overlay")]
    public bool createOverlayAutomatically = true;
    public MeshRenderer overlayRenderer;
    public float overlayOffset = 0.015f;
    public float localSurfaceOffset = 0.5f;
    public string overlayObjectName = "PaintOverlay";

    [Header("CPU Test Input")]
    public bool allowCpuTestImpacts = true;
    public int maxCpuTestImpactsPerFrame = 128;

    [Header("Debug")]
    public bool logImpacts = true;
    public bool drawGizmos = true;
    public Vector3 lastHitWorldPoint;
    public Vector2 lastHitUV;
    public float lastRadiusUV;
    public float lastSpeed;
    public int totalImpacts;

    [Header("Manual Export")]
    public bool enableKeyboardSave = true;
    public KeyCode saveKey = KeyCode.P;
    public string exportFolderName = "SavedPaintings";
    public string exportFilePrefix = "painting";
    public bool includeCanvasBaseColor = true;
    public bool exportTransparentOverlayOnly = false;
    public Color fallbackBaseColor = new Color(0.9f, 0.9f, 0.8f, 1f);

    [Header("Export Performance")]
    public bool useAsyncGpuReadbackForExport = true;
    bool exportInProgress = false;

    const int MaxInternalImpacts = 256;

    RenderTexture stainRT;
    RenderTexture wetnessRT;
    RenderTexture displayRT;

    ComputeBuffer internalImpactsBuffer;

    int clearKernel;
    int paintKernel;
    int dryKernel;

    Material overlayMaterial;
    System.Random random;
    float dryingTimer;

    readonly List<GpuPaintImpact> pendingCpuTestImpacts = new List<GpuPaintImpact>();

    public RenderTexture PaintRenderTexture => displayRT;
    public bool IsUsingGpuPainting => useGpuPainting;

    // فقط لتجنب كسر سكربتات قديمة. في نظام GPU لا نستخدم Texture2D للرسم.
    public Texture2D PaintTexture => null;

    void Reset()
    {
        canvasMaterialManager = GetComponent<CanvasMaterialManager>();
    }

    void Awake()
    {
        random = new System.Random();

        if (canvasMaterialManager == null)
        {
            canvasMaterialManager = GetComponent<CanvasMaterialManager>();
        }

        InitializeGpuPainting();
        EnsureOverlay();
    }

    void Start()
    {
        ClearPaint();
    }

    void Update()
    {
        if (enableKeyboardSave && Input.GetKeyDown(saveKey))
        {
            SavePaintingNow();
        }

        if (!useGpuPainting || !enableDrying)
        {
            return;
        }

        dryingTimer += Time.deltaTime;

        if (dryingTimer >= dryingUpdateInterval)
        {
            DispatchDrying(dryingTimer);
            dryingTimer = 0f;
        }
    }

    void LateUpdate()
    {
        if (!useGpuPainting)
        {
            return;
        }

        if (allowCpuTestImpacts)
        {
            DispatchPendingCpuTestImpacts();
        }
    }

    void InitializeGpuPainting()
    {
        if (!useGpuPainting)
        {
            Debug.LogWarning("[PaintCanvasPainter] GPU painting is disabled.");
            return;
        }

        if (paintCompute == null)
        {
            Debug.LogError("[PaintCanvasPainter] Paint Compute Shader is not assigned.");
            return;
        }

        stainRT = CreateRenderTexture("Paint Stain RT", RenderTextureFormat.ARGB32);
        wetnessRT = CreateRenderTexture("Paint Wetness RT", RenderTextureFormat.RFloat);
        displayRT = CreateRenderTexture("Paint Display RT", RenderTextureFormat.ARGB32);

        clearKernel = paintCompute.FindKernel("ClearTexture");
        paintKernel = paintCompute.FindKernel("PaintSplats");
        dryKernel = paintCompute.FindKernel("DryWetness");

        internalImpactsBuffer = new ComputeBuffer(MaxInternalImpacts, GpuPaintImpact.Stride);

        DispatchClear();

        Debug.Log("[PaintCanvasPainter] GPU painting initialized.");
    }

    RenderTexture CreateRenderTexture(string rtName, RenderTextureFormat format)
    {
        RenderTexture rt = new RenderTexture(textureResolution, textureResolution, 0, format);
        rt.name = rtName;
        rt.enableRandomWrite = true;
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.filterMode = FilterMode.Bilinear;
        rt.Create();
        return rt;
    }

    void EnsureOverlay()
    {
        if (!createOverlayAutomatically && overlayRenderer == null)
        {
            Debug.LogWarning("[PaintCanvasPainter] No overlay renderer assigned.");
            return;
        }

        if (overlayRenderer == null)
        {
            GameObject overlay = GameObject.CreatePrimitive(PrimitiveType.Quad);
            overlay.name = overlayObjectName;
            overlay.transform.SetParent(transform, false);

            if (uvPlane == CanvasUVPlane.LocalXZ)
            {
                overlay.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                overlay.transform.localPosition = new Vector3(0f, localSurfaceOffset + overlayOffset, 0f);
            }
            else
            {
                overlay.transform.localRotation = Quaternion.identity;
                overlay.transform.localPosition = new Vector3(0f, 0f, -(localSurfaceOffset + overlayOffset));
            }

            overlay.transform.localScale = new Vector3(
                Mathf.Abs(localURange.y - localURange.x),
                Mathf.Abs(localVRange.y - localVRange.x),
                1f
            );

            Collider col = overlay.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            overlayRenderer = overlay.GetComponent<MeshRenderer>();
        }

        Shader shader = Shader.Find("Unlit/Transparent");

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        overlayMaterial = new Material(shader);
        overlayMaterial.name = "Runtime Paint Overlay Material";

        if (displayRT != null)
        {
            overlayMaterial.mainTexture = displayRT;
        }

        overlayRenderer.material = overlayMaterial;
    }

    public void ClearPaint()
    {
        if (!useGpuPainting || paintCompute == null)
        {
            Debug.LogWarning("[PaintCanvasPainter] Cannot clear. GPU painting is disabled or compute shader missing.");
            return;
        }

        DispatchClear();

        totalImpacts = 0;
        lastHitWorldPoint = Vector3.zero;
        lastHitUV = Vector2.zero;
        lastRadiusUV = 0f;
        lastSpeed = 0f;

        Debug.Log("[PaintCanvasPainter] GPU paint texture cleared.");
    }

    void DispatchClear()
    {
        if (paintCompute == null || stainRT == null || wetnessRT == null || displayRT == null)
        {
            return;
        }

        paintCompute.SetInt("textureResolution", textureResolution);

        paintCompute.SetTexture(clearKernel, "StainTexture", stainRT);
        paintCompute.SetTexture(clearKernel, "WetnessTexture", wetnessRT);
        paintCompute.SetTexture(clearKernel, "DisplayTexture", displayRT);

        int groups = Mathf.CeilToInt(textureResolution / 8f);
        paintCompute.Dispatch(clearKernel, groups, groups, 1);
    }

    public bool TryWorldToUV(Vector3 worldPoint, out Vector2 uv)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);

        float uValue;
        float vValue;

        if (uvPlane == CanvasUVPlane.LocalXZ)
        {
            uValue = local.x;
            vValue = local.z;
        }
        else
        {
            uValue = local.x;
            vValue = local.y;
        }

        float u = Mathf.InverseLerp(localURange.x, localURange.y, uValue);
        float v = Mathf.InverseLerp(localVRange.x, localVRange.y, vValue);

        uv = new Vector2(u, v);

        return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
    }

    // هذه الدالة بقيت كي يعمل PaintImpactTester.
    // أما السائل الحقيقي فسيستخدم PaintGpuImpactDetector مباشرة.
    public void ApplyImpact(PaintImpactInput input)
    {
        if (!allowCpuTestImpacts)
        {
            return;
        }

        if (!TryWorldToUV(input.worldPoint, out Vector2 uv))
        {
            if (logImpacts)
            {
                Debug.LogWarning($"[PaintCanvasPainter] Test impact outside UV area. World={input.worldPoint}, UV={uv}");
            }

            return;
        }

        CanvasMaterialManager.CanvasSurfaceSettings settings = null;

        if (canvasMaterialManager != null)
        {
            settings = canvasMaterialManager.GetCurrentSettings();
        }

        PaintImpactResult result = PaintImpactModel.Evaluate(input, settings);

        Color impactColor = input.color;
        impactColor.a = 1f;

        AddCpuTestMainImpact(uv, impactColor, result);
        AddCpuTestSatellites(uv, impactColor, result);

        totalImpacts++;
        lastHitWorldPoint = input.worldPoint;
        lastHitUV = uv;
        lastRadiusUV = result.centralRadiusUV;
        lastSpeed = result.debugSpeed;

        if (logImpacts)
        {
            string surfaceName = settings != null ? settings.type.ToString() : "No Surface Settings";

            Debug.Log(
                $"[PaintCanvasPainter] Test GPU Impact #{totalImpacts} | Surface={surfaceName} | UV={uv} | Speed={result.debugSpeed:F2}"
            );
        }
    }

    void AddCpuTestMainImpact(Vector2 uv, Color color, PaintImpactResult result)
    {
        if (pendingCpuTestImpacts.Count >= maxCpuTestImpactsPerFrame)
        {
            return;
        }

        pendingCpuTestImpacts.Add(GpuPaintImpact.Create(
            uv,
            color,
            result.radiusU,
            result.radiusV,
            result.angleRad,
            result.centralIntensity,
            result.edgeNoise,
            1f - result.absorption * 0.45f
        ));
    }

    void AddCpuTestSatellites(Vector2 centerUV, Color color, PaintImpactResult result)
    {
        for (int i = 0; i < result.satelliteCount; i++)
        {
            if (pendingCpuTestImpacts.Count >= maxCpuTestImpactsPerFrame)
            {
                return;
            }

            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

            float logR = PaintImpactModel.LogNormal(random, -1.6f, 0.55f);
            float distance = result.satelliteDistanceUV * Mathf.Clamp(logR, 0.15f, 2.5f);

            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            Vector2 uv = centerUV + offset;

            if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
            {
                continue;
            }

            float radius = UnityEngine.Random.Range(result.satelliteMinRadiusUV, result.satelliteMaxRadiusUV);
            float intensity = result.centralIntensity * UnityEngine.Random.Range(0.2f, 0.65f);

            pendingCpuTestImpacts.Add(GpuPaintImpact.Create(
                uv,
                color,
                radius,
                radius,
                0f,
                intensity,
                result.edgeNoise * 0.6f,
                1f - result.absorption * 0.35f
            ));
        }
    }

    void DispatchPendingCpuTestImpacts()
    {
        if (pendingCpuTestImpacts.Count == 0)
        {
            return;
        }

        int count = Mathf.Min(pendingCpuTestImpacts.Count, MaxInternalImpacts);

        GpuPaintImpact[] data = new GpuPaintImpact[count];
        pendingCpuTestImpacts.CopyTo(0, data, 0, count);

        internalImpactsBuffer.SetData(data);

        DispatchGpuImpactBuffer(internalImpactsBuffer, count);

        pendingCpuTestImpacts.Clear();
    }

    public void DispatchGpuImpactBuffer(ComputeBuffer impactBuffer, int maxImpactCount)
    {
        if (!useGpuPainting || paintCompute == null)
        {
            return;
        }

        if (impactBuffer == null || maxImpactCount <= 0)
        {
            return;
        }

        paintCompute.SetInt("textureResolution", textureResolution);
        paintCompute.SetInt("maxImpactCount", maxImpactCount);

        paintCompute.SetBuffer(paintKernel, "Impacts", impactBuffer);
        paintCompute.SetTexture(paintKernel, "StainTexture", stainRT);
        paintCompute.SetTexture(paintKernel, "WetnessTexture", wetnessRT);
        paintCompute.SetTexture(paintKernel, "DisplayTexture", displayRT);

        int groups = Mathf.CeilToInt(textureResolution / 8f);
        paintCompute.Dispatch(paintKernel, groups, groups, 1);
    }

    void DispatchDrying(float dt)
    {
        if (paintCompute == null || stainRT == null || wetnessRT == null || displayRT == null)
        {
            return;
        }

        CanvasMaterialManager.CanvasSurfaceSettings settings = null;

        if (canvasMaterialManager != null)
        {
            settings = canvasMaterialManager.GetCurrentSettings();
        }

        float surfaceDrying = settings != null ? settings.dryingRate : 0.5f;
        float dryAmount = dt * dryingSpeedMultiplier * Mathf.Lerp(0.15f, 1.2f, surfaceDrying);

        paintCompute.SetInt("textureResolution", textureResolution);
        paintCompute.SetFloat("dryingAmount", dryAmount);
        paintCompute.SetFloat("wetVisualBoost", wetVisualBoost);

        paintCompute.SetTexture(dryKernel, "StainTexture", stainRT);
        paintCompute.SetTexture(dryKernel, "WetnessTexture", wetnessRT);
        paintCompute.SetTexture(dryKernel, "DisplayTexture", displayRT);

        int groups = Mathf.CeilToInt(textureResolution / 8f);
        paintCompute.Dispatch(dryKernel, groups, groups, 1);
    }
    [ContextMenu("Save Painting Now")]
    public void SavePaintingNow()
    {
        if (exportInProgress)
        {
            Debug.LogWarning("[PaintCanvasPainter] Export already in progress.");
            return;
        }

        if (displayRT == null)
        {
            Debug.LogError("[PaintCanvasPainter] Display RenderTexture is null.");
            return;
        }

        if (useAsyncGpuReadbackForExport)
        {
            SavePaintingAsync();
        }
        else
        {
            SavePaintingSync();
        }
    }
    void SavePaintingAsync()
    {
        exportInProgress = true;

        AsyncGPUReadback.Request(displayRT, 0, TextureFormat.RGBA32, request =>
        {
            exportInProgress = false;

            if (request.hasError)
            {
                Debug.LogError("[PaintCanvasPainter] AsyncGPUReadback failed.");
                return;
            }

            Texture2D paintTex = new Texture2D(
                displayRT.width,
                displayRT.height,
                TextureFormat.RGBA32,
                false
            );

            paintTex.LoadRawTextureData(request.GetData<byte>());
            paintTex.Apply(false);

            Texture2D finalTexture = BuildFinalExportTexture(paintTex);

            Destroy(paintTex);

            if (finalTexture == null)
            {
                Debug.LogError("[PaintCanvasPainter] Failed to build final export texture.");
                return;
            }

            SaveTextureToPng(finalTexture);

            Destroy(finalTexture);
        });
    }
    void SavePaintingSync()
    {
        Texture2D paintTex = ReadRenderTexture(displayRT);

        if (paintTex == null)
        {
            Debug.LogError("[PaintCanvasPainter] Failed to read RenderTexture.");
            return;
        }

        Texture2D finalTexture = BuildFinalExportTexture(paintTex);

        Destroy(paintTex);

        if (finalTexture == null)
        {
            Debug.LogError("[PaintCanvasPainter] Failed to build final export texture.");
            return;
        }

        SaveTextureToPng(finalTexture);

        Destroy(finalTexture);
    }
    Texture2D BuildFinalExportTexture(Texture2D paintTex)
{
    int width = paintTex.width;
    int height = paintTex.height;

    Color[] paintPixels = paintTex.GetPixels();
    Color[] finalPixels = new Color[paintPixels.Length];

    Color baseColor = GetBaseColor();

    for (int i = 0; i < paintPixels.Length; i++)
    {
        Color paint = paintPixels[i];

        if (exportTransparentOverlayOnly && !includeCanvasBaseColor)
        {
            finalPixels[i] = paint;
        }
        else
        {
            finalPixels[i] = AlphaBlend(baseColor, paint);
        }
    }

    Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);
    result.SetPixels(finalPixels);
    result.Apply(false);

    return result;
}
void SaveTextureToPng(Texture2D texture)
{
    string directoryPath = Path.GetFullPath(
        Path.Combine(Application.dataPath, "..", exportFolderName)
    );

    Directory.CreateDirectory(directoryPath);

    string surfaceName = "UnknownSurface";

    if (canvasMaterialManager != null)
    {
        surfaceName = canvasMaterialManager.currentType.ToString();
    }

    string timeStamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
    string fileName = $"{exportFilePrefix}_{surfaceName}_{timeStamp}.png";
    string fullPath = Path.Combine(directoryPath, fileName);

    byte[] pngBytes = texture.EncodeToPNG();
    File.WriteAllBytes(fullPath, pngBytes);

    Debug.Log($"[PaintCanvasPainter] Saved painting to:\n{fullPath}");
}
    Texture2D GetPaintTextureForExport()
    {
        if (displayRT == null)
        {
            Debug.LogError("[PaintCanvasPainter] Display RenderTexture is null.");
            return null;
        }

        return ReadRenderTexture(displayRT);
    }

    Texture2D ReadRenderTexture(RenderTexture rt)
{
    RenderTexture previous = RenderTexture.active;

    RenderTexture.active = rt;

    Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
    tex.Apply(false);

    RenderTexture.active = previous;

    return tex;
}

    Color GetBaseColor()
    {
        Renderer canvasRenderer = GetComponent<Renderer>();

        if (canvasRenderer != null && canvasRenderer.material != null)
        {
            Material mat = canvasRenderer.material;

            if (mat.HasProperty("_Color"))
            {
                return mat.color;
            }
        }

        return fallbackBaseColor;
    }

    Color AlphaBlend(Color dst, Color src)
    {
        float outA = src.a + dst.a * (1f - src.a);

        if (outA <= 0.0001f)
        {
            return Color.clear;
        }

        Color outColor = new Color
        {
            r = (src.r * src.a + dst.r * dst.a * (1f - src.a)) / outA,
            g = (src.g * src.a + dst.g * dst.a * (1f - src.a)) / outA,
            b = (src.b * src.a + dst.b * dst.a * (1f - src.a)) / outA,
            a = Mathf.Clamp01(outA)
        };

        return outColor;
    }
    void OnDrawGizmos()
    {
        if (!drawGizmos || totalImpacts <= 0)
        {
            return;
        }

        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(lastHitWorldPoint, 0.08f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(lastHitWorldPoint, 0.18f);
    }

    void OnDestroy()
    {
        if (internalImpactsBuffer != null)
        {
            internalImpactsBuffer.Release();
            internalImpactsBuffer = null;
        }

        ReleaseRT(ref stainRT);
        ReleaseRT(ref wetnessRT);
        ReleaseRT(ref displayRT);
    }

    void ReleaseRT(ref RenderTexture rt)
    {
        if (rt == null)
        {
            return;
        }

        rt.Release();
        rt = null;
    }
}