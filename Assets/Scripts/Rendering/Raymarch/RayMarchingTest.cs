using UnityEngine;
using Seb.Fluid.Simulation;

namespace Seb.Fluid.Rendering {
    [ImageEffectAllowedInSceneView]
    public class RayMarchingTest : MonoBehaviour {
        public enum RenderQualityProfile {
            Custom,
            Laptop,
            Balanced,
            HighQuality
        }

        [Header("Quality")]
        public RenderQualityProfile renderQualityProfile = RenderQualityProfile.Laptop;
        public bool syncWithSimPerformanceProfile = true;

        [Header("Settings")]
        public float densityOffset = 150;
        public int numRefractions = 4;
        public Vector3 extinctionCoefficients;

        [Header("Paint Look")]
        public Color fluidColor = new Color(1f, 0.12f, 0.02f, 1f); // orange/red paint
        [Range(0, 1)] public float paintOpacity = 1f;
        public float densityMultiplier = 0.001f;
        [Min(0.01f)] public float stepSize = 0.02f;
        public float lightStepSize = 0.4f;
        [Min(1)] public float indexOfRefraction = 1.33f;
        public Vector3 testParams;
        public EnvironmentSettings environmentSettings;

        [Header("References")]
        public FluidSim sim;
        // public Transform cubeTransform;
        public Shader shader;

        Material raymarchMat;

        void Start() {
            raymarchMat = new Material(shader);
            Camera.main.depthTextureMode = DepthTextureMode.Depth;
            ApplyRenderQualityProfile();
            if (sim != null)
                sim.SimulationInitCompleted += _ => ApplyRenderQualityProfile();
        }

        public void ApplyRenderQualityProfile() {
            if (syncWithSimPerformanceProfile && sim != null && sim.performanceProfile != SimulationPerformanceProfile.Custom) {
                switch (sim.performanceProfile) {
                    case SimulationPerformanceProfile.Laptop:
                        renderQualityProfile = RenderQualityProfile.Laptop;
                        break;
                    case SimulationPerformanceProfile.Balanced:
                        renderQualityProfile = RenderQualityProfile.Balanced;
                        break;
                    case SimulationPerformanceProfile.HighQuality:
                        renderQualityProfile = RenderQualityProfile.HighQuality;
                        break;
                }
            }

            switch (renderQualityProfile) {
                case RenderQualityProfile.Laptop:
                    numRefractions = 2;
                    stepSize = 0.03f;
                    lightStepSize = 0.15f;
                    densityOffset = 180f;
                    densityMultiplier = 0.055f;
                    extinctionCoefficients = new Vector3(14f, 5f, 4f);
                    indexOfRefraction = 1.42f;
                    break;
                case RenderQualityProfile.Balanced:
                    numRefractions = 3;
                    stepSize = 0.025f;
                    lightStepSize = 0.2f;
                    densityOffset = 170f;
                    densityMultiplier = 0.05f;
                    extinctionCoefficients = new Vector3(12f, 4f, 4f);
                    indexOfRefraction = 1.38f;
                    break;
                case RenderQualityProfile.HighQuality:
                    numRefractions = 4;
                    stepSize = 0.02f;
                    lightStepSize = 0.1f;
                    densityOffset = 150f;
                    densityMultiplier = 0.05f;
                    extinctionCoefficients = new Vector3(10f, 3.5f, 3.5f);
                    indexOfRefraction = 1.33f;
                    break;
            }
        }

        void OnValidate() {
            if (renderQualityProfile != RenderQualityProfile.Custom)
                ApplyRenderQualityProfile();
        }

        [ImageEffectOpaque]
        void OnRenderImage(RenderTexture src, RenderTexture target) {
            if (sim.DensityMap != null) {
                SetShaderParams();
                Graphics.Blit(src, target, raymarchMat);
            } else {
                Graphics.Blit(src, target);
            }
        }

        void SetShaderParams() {
            SetEnvironmentParams(raymarchMat, environmentSettings);
            raymarchMat.SetTexture("DensityMap", sim.DensityMap);
            raymarchMat.SetVector("boundsSize", sim.Scale);

            Matrix4x4 densityVolumeLocalToWorld =
                Matrix4x4.TRS(sim.transform.position, sim.transform.rotation, Vector3.one);

            Matrix4x4 densityVolumeWorldToLocal =
                densityVolumeLocalToWorld.inverse;

            raymarchMat.SetMatrix("densityVolumeLocalToWorld", densityVolumeLocalToWorld);
            raymarchMat.SetMatrix("densityVolumeWorldToLocal", densityVolumeWorldToLocal);

            raymarchMat.SetFloat("volumeValueOffset", densityOffset);
            raymarchMat.SetVector("testParams", testParams);
            raymarchMat.SetFloat("indexOfRefraction", indexOfRefraction);
            raymarchMat.SetFloat("densityMultiplier", densityMultiplier / 1000);
            raymarchMat.SetFloat("viewMarchStepSize", stepSize);
            raymarchMat.SetFloat("lightStepSize", lightStepSize);
            raymarchMat.SetInt("numRefractions", numRefractions);
            raymarchMat.SetVector("extinctionCoeff", extinctionCoefficients);
            raymarchMat.SetColor("fluidColor", fluidColor);
            raymarchMat.SetFloat("paintOpacity", paintOpacity);
            // raymarchMat.SetMatrix("cubeLocalToWorld", Matrix4x4.TRS(cubeTransform.position, cubeTransform.rotation, cubeTransform.localScale / 2));
            // raymarchMat.SetMatrix("cubeWorldToLocal", Matrix4x4.TRS(cubeTransform.position, cubeTransform.rotation, cubeTransform.localScale / 2).inverse);

            Vector3 floorSize = new Vector3(30, 0.05f, 30);
            float floorHeight = -sim.Scale.y / 2 + sim.transform.position.y - floorSize.y / 2;
            raymarchMat.SetVector("floorPos", new Vector3(0, floorHeight, 0));
            raymarchMat.SetVector("floorSize", floorSize);
        }

        public static void SetEnvironmentParams(Material mat, EnvironmentSettings environmentSettings) {
            // mat.SetColor("tileCol1", environmentSettings.tileCol1);
            // mat.SetColor("tileCol2", environmentSettings.tileCol2);
            // mat.SetColor("tileCol3", environmentSettings.tileCol3);
            // mat.SetColor("tileCol4", environmentSettings.tileCol4);
            // mat.SetVector("tileColVariation", environmentSettings.tileColVariation);
            // mat.SetFloat("tileScale", environmentSettings.tileScale);
            // mat.SetFloat("tileDarkOffset", environmentSettings.tileDarkOffset);
            mat.SetVector("dirToSun", -environmentSettings.light.transform.forward);
        }

        [System.Serializable]
        public struct EnvironmentSettings {
            // public Color tileCol1;
            // public Color tileCol2;
            // public Color tileCol3;
            // public Color tileCol4;
            // public Vector3 tileColVariation;
            // public float tileScale;
            // public float tileDarkOffset;
            public Light light;
        }
    }
}