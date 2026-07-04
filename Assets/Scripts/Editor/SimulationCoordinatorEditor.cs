#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SimulationCoordinator))]
public sealed class SimulationCoordinatorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode. The coordinator initializes rope, bucket, collisions and fluid in one paused state.",
                MessageType.Info);
            return;
        }

        SimulationCoordinator coordinator = (SimulationCoordinator)target;

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("Simulation Commands", EditorStyles.boldLabel);

        if (GUILayout.Button("Reset To Initial State", GUILayout.Height(30f)))
            coordinator.ResetToInitialState();

        using (new EditorGUI.DisabledScope(coordinator.IsRunning))
        {
            if (GUILayout.Button("Apply Initial Motion", GUILayout.Height(30f)))
                coordinator.ApplyInitialMotion();
        }

        if (GUILayout.Button("Reset And Apply Initial Motion", GUILayout.Height(30f)))
            coordinator.ResetAndApplyInitialMotion();

        using (new EditorGUI.DisabledScope(!coordinator.IsRunning))
        {
            if (GUILayout.Button("Stop Simulation", GUILayout.Height(24f)))
                coordinator.StopSimulation();
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Runtime State", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Initialized", coordinator.IsInitialized ? "Yes" : "No");
        EditorGUILayout.LabelField("Running", coordinator.IsRunning ? "Yes" : "No");
        EditorGUILayout.LabelField("Fluid Substeps / Fixed Step", coordinator.FluidSubstepsPerFixedStep.ToString());
    }
}
#endif
