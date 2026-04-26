using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;

[CustomEditor(typeof(SO_ItemCategoryConfig))]
public class SO_ItemCategoryConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(20);

        SO_ItemCategoryConfig config = (SO_ItemCategoryConfig)target;
        GUI.backgroundColor = new Color(0.8f, 0.2f, 0.2f); // Un tono rojizo oscuro

        if (GUILayout.Button("Resetear a valores de diseño WIRED", GUILayout.Height(35)))
        {
            config.ResetToDesignDefaults();
            EditorUtility.SetDirty(config);
        }

        GUI.backgroundColor = Color.white;
    }
}
#endif