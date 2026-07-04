#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BucketMotionController))]
public sealed class BucketMotionControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode to use the simulation commands. The bucket remains at its initial rest state until Apply Initial Motion is pressed.",
                MessageType.Info);
            return;
        }

        BucketMotionController controller = (BucketMotionController)target;

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("Simulation Commands", EditorStyles.boldLabel);

        if (GUILayout.Button("Reset To Initial State", GUILayout.Height(30f)))
            controller.ResetToInitialState();

        using (new EditorGUI.DisabledScope(controller.IsSimulationRunning))
        {
            if (GUILayout.Button("Apply Initial Motion", GUILayout.Height(30f)))
                controller.ApplyInitialMotion();
        }

        if (GUILayout.Button("Reset And Apply Initial Motion", GUILayout.Height(30f)))
            controller.ResetAndApplyInitialMotion();

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Runtime State", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Simulation Running", controller.IsSimulationRunning ? "Yes" : "No");
        EditorGUILayout.LabelField("Twist Angle", controller.CurrentTwistAngle.ToString("F2") + "°");
        EditorGUILayout.LabelField("Hinge Angle", controller.CurrentHingeAngle.ToString("F2") + "°");
        EditorGUILayout.LabelField("Pitch Angle", controller.CurrentPitchAngle.ToString("F2") + "°");
        EditorGUILayout.LabelField("Roll Angle", controller.CurrentRollAngle.ToString("F2") + "°");
    }
}
#endif
