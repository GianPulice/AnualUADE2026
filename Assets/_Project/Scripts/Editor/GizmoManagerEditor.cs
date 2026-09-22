using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The "Todos" / "Ninguno" buttons on top of <see cref="GizmoManager"/>, and the safety net that
/// gives back gizmos a crashed session left hidden. The manager itself lives in WIRED_Zona1_Blockout
/// under ---- SISTEMA ---- (tagged EditorOnly); to add one to another scene, put the component on an
/// empty GameObject.
/// </summary>
[CustomEditor(typeof(GizmoManager))]
public class GizmoManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Todos")) SetAll(true);
            if (GUILayout.Button("Ninguno")) SetAll(false);
        }

        EditorGUILayout.Space();
        DrawPropertiesExcluding(serializedObject, "m_Script");

        // Applying fires the component's OnValidate, which is what actually re-applies the gizmos.
        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>Every bool on the component, so a family added later is covered without touching this.</summary>
    private void SetAll(bool value)
    {
        SerializedProperty property = serializedObject.GetIterator();
        if (!property.NextVisible(true)) return;

        do
        {
            if (property.propertyType == SerializedPropertyType.Boolean) property.boolValue = value;
        }
        while (property.NextVisible(false));
    }

    /// <summary>
    /// The crash case. A manager gives its gizmos back in OnDisable; if the editor died with it in
    /// the scene that never ran, and the next scene opened would inherit the hidden families with no
    /// checkbox anywhere to bring them back. Any time a scene opens — or the editor starts — with no
    /// manager in it, whatever is still recorded as hidden is shown again.
    /// </summary>
    [InitializeOnLoadMethod]
    private static void HookOrphanRestore()
    {
        EditorSceneManager.sceneOpened += (_, _) => EditorApplication.delayCall += RestoreIfNoManager;
        EditorApplication.delayCall += RestoreIfNoManager;
    }

    private static void RestoreIfNoManager()
    {
        if (Object.FindAnyObjectByType<GizmoManager>() == null) GizmoManager.RestoreHiddenGizmos();
    }
}
