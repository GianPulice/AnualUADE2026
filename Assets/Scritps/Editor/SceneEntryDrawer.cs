#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(SceneEntry))]
public class SceneEntryDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var sceneAssetProp = property.FindPropertyRelative("sceneAsset");
        var scenePathProp  = property.FindPropertyRelative("scenePath");
        var sceneNameProp  = property.FindPropertyRelative("sceneName");
        var labelProp      = property.FindPropertyRelative("label");

        float lineH   = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;

        // Fila 1: SceneAsset
        Rect sceneRect = new Rect(position.x, position.y, position.width, lineH);
        EditorGUI.BeginChangeCheck();
        EditorGUI.PropertyField(sceneRect, sceneAssetProp, new GUIContent("Scene"));
        if (EditorGUI.EndChangeCheck())
        {
            // Sincronizar path y nombre cuando cambia el asset
            var asset = sceneAssetProp.objectReferenceValue as SceneAsset;
            if (asset != null)
            {
                scenePathProp.stringValue = AssetDatabase.GetAssetPath(asset);
                sceneNameProp.stringValue = asset.name;
            }
            else
            {
                scenePathProp.stringValue = "";
                sceneNameProp.stringValue = "";
            }
        }

        // Fila 2: Label opcional
        Rect labelRect = new Rect(position.x, position.y + lineH + spacing, position.width, lineH);
        EditorGUI.PropertyField(labelRect, labelProp, new GUIContent("Label (opcional)"));

        // Fila 3: Path (readonly, informativo)
        if (!string.IsNullOrEmpty(scenePathProp.stringValue))
        {
            Rect pathRect = new Rect(position.x, position.y + (lineH + spacing) * 2, position.width, lineH);
            GUI.enabled = false;
            EditorGUI.TextField(pathRect, "Path", scenePathProp.stringValue);
            GUI.enabled = true;

            // Warning si la escena no está en Build Settings
            var asset = sceneAssetProp.objectReferenceValue as SceneAsset;
            if (asset != null && !IsInBuildSettings(scenePathProp.stringValue))
            {
                Rect warnRect = new Rect(position.x, position.y + (lineH + spacing) * 3, position.width, lineH);
                EditorGUI.HelpBox(warnRect, "⚠ Esta escena no está en Build Settings.", MessageType.Warning);
            }
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var scenePathProp  = property.FindPropertyRelative("scenePath");
        var sceneAssetProp = property.FindPropertyRelative("sceneAsset");

        float lineH   = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;

        // Scene + Label + Path + (warning opcional)
        float height = (lineH + spacing) * 2; // Scene + Label siempre
        if (!string.IsNullOrEmpty(scenePathProp.stringValue))
        {
            height += lineH + spacing; // Path

            var asset = sceneAssetProp.objectReferenceValue as SceneAsset;
            if (asset != null && !IsInBuildSettings(scenePathProp.stringValue))
                height += lineH + spacing * 2; // Warning
        }

        return height;
    }

    private bool IsInBuildSettings(string path)
    {
        foreach (var scene in EditorBuildSettings.scenes)
            if (scene.path == path) return true;
        return false;
    }
}
#endif
