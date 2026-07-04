using UnityEngine;

public class PaintImpactTester : MonoBehaviour
{
    [Header("References")]
    public PaintCanvasPainter painter;

    [Header("Test Impact")]
    public Color testPaintColor = new Color(1f, 0.1f, 0.65f, 1f);
    public float testMass = 0.75f;
    public float testParticleDiameter = 0.08f;
    public Vector3 testVelocity = new Vector3(0f, -4f, 0f);

    [Header("Random Test Area")]
    [Range(0f, 0.45f)]
    public float randomUVPadding = 0.08f;

    [Header("Keyboard")]
    public bool enableKeyboardTesting = true;
    public KeyCode centerImpactKey = KeyCode.T;
    public KeyCode randomImpactKey = KeyCode.Y;
    public KeyCode clearKey = KeyCode.C;

    void Reset()
    {
        painter = GetComponent<PaintCanvasPainter>();
    }

    void Awake()
    {
        if (painter == null)
        {
            painter = GetComponent<PaintCanvasPainter>();
        }
    }

    void Update()
    {
        if (!enableKeyboardTesting)
        {
            return;
        }

        if (Input.GetKeyDown(centerImpactKey))
        {
            TestCenterImpact();
        }

        if (Input.GetKeyDown(randomImpactKey))
        {
            TestRandomImpact();
        }

        if (Input.GetKeyDown(clearKey) && painter != null)
        {
            painter.ClearPaint();
        }
    }

    [ContextMenu("Test Center Impact")]
    public void TestCenterImpact()
    {
        SendImpactAtUV(new Vector2(0.5f, 0.5f), "CENTER");
    }

    [ContextMenu("Test Random Impact")]
    public void TestRandomImpact()
    {
        float min = randomUVPadding;
        float max = 1f - randomUVPadding;

        Vector2 randomUV = new Vector2(
            Random.Range(min, max),
            Random.Range(min, max)
        );

        SendImpactAtUV(randomUV, "RANDOM");
    }

    void SendImpactAtUV(Vector2 uv, string source)
    {
        if (painter == null)
        {
            Debug.LogError("[PaintImpactTester] Missing PaintCanvasPainter.");
            return;
        }

        Vector3 worldPoint = UVToWorld(uv);

        PaintImpactInput input = new PaintImpactInput
        {
            worldPoint = worldPoint,
            velocity = testVelocity,
            color = testPaintColor,
            mass = testMass,
            particleDiameter = testParticleDiameter
        };

        Debug.Log($"[PaintImpactTester] {source} impact | UV={uv} | World={worldPoint}");

        painter.ApplyImpact(input);
    }

    Vector3 UVToWorld(Vector2 uv)
    {
        float localU = Mathf.Lerp(painter.localURange.x, painter.localURange.y, uv.x);
        float localV = Mathf.Lerp(painter.localVRange.x, painter.localVRange.y, uv.y);

        Vector3 local;

        if (painter.uvPlane == CanvasUVPlane.LocalXZ)
        {
            local = new Vector3(localU, painter.localSurfaceOffset, localV);
        }
        else
        {
            local = new Vector3(localU, localV, -painter.localSurfaceOffset);
        }

        return painter.transform.TransformPoint(local);
    }
}