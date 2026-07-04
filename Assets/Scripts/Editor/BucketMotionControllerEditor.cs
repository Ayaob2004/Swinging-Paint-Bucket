#if UNITY_EDITOR
using UnityEditor;

[CustomEditor(typeof(BucketMotionController))]
public sealed class BucketMotionControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox(
            "Simulation commands are owned by SimulationCoordinator. This component only stores bucket motion parameters and state.",
            MessageType.Info);
    }
}
#endif
