using UnityEngine;

public struct PaintImpactInput
{
    public Vector3 worldPoint;
    public Vector3 velocity;
    public Color color;
    public float mass;
    public float particleDiameter;
}

public struct PaintImpactResult
{
    public float centralRadiusUV;
    public float radiusU;
    public float radiusV;
    public float angleRad;

    public float centralIntensity;
    public int satelliteCount;
    public float satelliteMinRadiusUV;
    public float satelliteMaxRadiusUV;
    public float satelliteDistanceUV;

    public float edgeNoise;
    public float fingerStrength;
    public float absorption;
    public float dryingRate;

    public float debugSpeed;
    public float debugSplashStrength;
}

public static class PaintImpactModel
{
    public static PaintImpactResult Evaluate(
        PaintImpactInput input,
        CanvasMaterialManager.CanvasSurfaceSettings surface,
        float uvScale = 1f)
    {
        if (surface == null)
        {
            Debug.LogWarning("[PaintImpactModel] Surface settings is null. Using safe defaults.");

            return new PaintImpactResult
            {
                centralRadiusUV = 0.04f,
                radiusU = 0.04f,
                radiusV = 0.04f,
                angleRad = 0f,
                centralIntensity = Mathf.Clamp01(input.mass),
                satelliteCount = 4,
                satelliteMinRadiusUV = 0.003f,
                satelliteMaxRadiusUV = 0.01f,
                satelliteDistanceUV = 0.08f,
                edgeNoise = 0.2f,
                fingerStrength = 0.3f,
                absorption = 0.5f,
                dryingRate = 0.5f,
                debugSpeed = input.velocity.magnitude,
                debugSplashStrength = 0.3f
            };
        }

        float speed = input.velocity.magnitude;
        float speed01 = Mathf.InverseLerp(0.2f, 8f, speed);

        // Radius inspired by impact strength + spread + absorption + roughness.
        float baseRadius =
            Mathf.Lerp(0.012f, 0.075f, speed01) *
            Mathf.Lerp(0.65f, 1.55f, surface.spread) *
            Mathf.Lerp(1.15f, 0.65f, surface.absorption) *
            Mathf.Lerp(1.05f, 0.75f, surface.roughness);

        baseRadius *= uvScale;
        baseRadius = Mathf.Clamp(baseRadius, 0.004f, 0.2f);

        // Splash strength: more speed, less roughness damping, more satellite strength.
        float splashStrength =
            speed01 *
            Mathf.Lerp(1.1f, 0.65f, surface.roughness) *
            Mathf.Lerp(0.8f, 1.25f, surface.satelliteStrength);

        splashStrength = Mathf.Clamp01(splashStrength);

        int satellites = Mathf.RoundToInt(Mathf.Lerp(0, 10, splashStrength));
        satellites = Mathf.Clamp(satellites, 0, 40);

        // Absorbent surfaces leave less glossy/intense deposits.
        float intensity =
            Mathf.Max(0.01f, input.mass) *
            Mathf.Lerp(1.15f, 0.65f, surface.absorption);

        intensity = Mathf.Clamp01(intensity);

        // Directionality:
        // Wood/Fabric can stretch paint along one dominant local direction.
        float anisotropy = Mathf.Clamp01(surface.anisotropy);

        float radiusU = baseRadius * Mathf.Lerp(1f, 2.2f, anisotropy);
        float radiusV = baseRadius * Mathf.Lerp(1f, 0.65f, anisotropy);

        // Use tangent velocity direction if available, otherwise default to U direction.
        Vector3 v = input.velocity;
        Vector2 tangent = new Vector2(v.x, v.z);

        float angle = 0f;
        if (tangent.sqrMagnitude > 0.0001f)
        {
            angle = Mathf.Atan2(tangent.y, tangent.x);
        }

        // Edge noise mostly from roughness and fabric-like behavior.
        float edgeNoise =
            surface.roughness * 0.45f +
            surface.fingerStrength * splashStrength * 0.25f;

        edgeNoise = Mathf.Clamp01(edgeNoise);

        return new PaintImpactResult
        {
            centralRadiusUV = baseRadius,
            radiusU = Mathf.Clamp(radiusU, 0.003f, 0.25f),
            radiusV = Mathf.Clamp(radiusV, 0.003f, 0.25f),
            angleRad = angle,

            centralIntensity = intensity,
            satelliteCount = satellites,
            satelliteMinRadiusUV = baseRadius * 0.08f,
            satelliteMaxRadiusUV = baseRadius * 0.28f,
            satelliteDistanceUV = baseRadius * Mathf.Lerp(1.2f, 4.5f, splashStrength),

            edgeNoise = edgeNoise,
            fingerStrength = surface.fingerStrength * splashStrength,
            absorption = surface.absorption,
            dryingRate = surface.dryingRate,

            debugSpeed = speed,
            debugSplashStrength = splashStrength
        };
    }

    public static float Gaussian01(System.Random random)
    {
        double u1 = 1.0 - random.NextDouble();
        double u2 = 1.0 - random.NextDouble();

        double randStdNormal =
            System.Math.Sqrt(-2.0 * System.Math.Log(u1)) *
            System.Math.Sin(2.0 * System.Math.PI * u2);

        return (float)randStdNormal;
    }

    public static float LogNormal(System.Random random, float mu, float sigma)
    {
        float z = Gaussian01(random);
        return Mathf.Exp(mu + sigma * z);
    }
}