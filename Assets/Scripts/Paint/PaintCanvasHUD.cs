using UnityEngine;
using UnityEngine.UI;

public class PaintCanvasHUD : MonoBehaviour
{
    [Header("References")]
    public PaintCanvasPainter painter;
    public CanvasMaterialManager canvasMaterialManager;
    public Text uiText;

    [Header("Fallback OnGUI")]
    public bool drawOnGUIIfNoText = true;

    string cachedText = "";

    void Reset()
    {
        painter = GetComponent<PaintCanvasPainter>();
        canvasMaterialManager = GetComponent<CanvasMaterialManager>();
    }

    void Awake()
    {
        if (painter == null)
        {
            painter = GetComponent<PaintCanvasPainter>();
        }

        if (canvasMaterialManager == null)
        {
            canvasMaterialManager = GetComponent<CanvasMaterialManager>();
        }
    }

    void Update()
    {
        UpdateText();
    }

    void UpdateText()
    {
        string surfaceName = "None";
        float absorption = 0f;
        float spread = 0f;
        float roughness = 0f;

        if (canvasMaterialManager != null)
        {
            CanvasMaterialManager.CanvasSurfaceSettings settings = canvasMaterialManager.GetCurrentSettings();

            if (settings != null)
            {
                surfaceName = settings.type.ToString();
                absorption = settings.absorption;
                spread = settings.spread;
                roughness = settings.roughness;
            }
        }

        int impacts = painter != null ? painter.totalImpacts : 0;
        float speed = painter != null ? painter.lastSpeed : 0f;
        float radius = painter != null ? painter.lastRadiusUV : 0f;
        Vector2 uv = painter != null ? painter.lastHitUV : Vector2.zero;

        cachedText =
            $"Paint Canvas Model\n" +
            $"Surface: {surfaceName}\n" +
            $"Absorption: {absorption:F2}\n" +
            $"Spread: {spread:F2}\n" +
            $"Roughness: {roughness:F2}\n" +
            $"Impacts: {impacts}\n" +
            $"Last Speed: {speed:F2}\n" +
            $"Last Radius UV: {radius:F4}\n" +
            $"Last UV: {uv}\n\n" +
            $"Keys:\n" +
            $"1 Paper | 2 Fabric | 3 Wood | 4 Metal\n" +
            $"T center splat | Y random splat | C clear";

        if (uiText != null)
        {
            uiText.text = cachedText;
        }
    }

    void OnGUI()
    {
        if (!drawOnGUIIfNoText || uiText != null)
        {
            return;
        }

        GUI.Box(new Rect(10, 10, 340, 210), cachedText);
    }
}