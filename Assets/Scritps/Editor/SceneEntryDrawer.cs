#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(SceneGroupEntry))]
public class SceneEntryDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var labelProp = property.FindPropertyRelative("label");
        var sceneAssetsProp = property.FindPropertyRelative("sceneAssets");

        float lineH = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        float y = position.y;

        // Fila 1: Label del grupo
        EditorGUI.PropertyField(
            new Rect(position.x, y, position.width, lineH),
            labelProp,
            new GUIContent("Label del grupo")
        );
        y += lineH + spacing;

        // Fila 2+: Lista de SceneAssets
        EditorGUI.PropertyField(
            new Rect(position.x, y, position.width, EditorGUI.GetPropertyHeight(sceneAssetsProp, true)),
            sceneAssetsProp,
            new GUIContent("Escenas del grupo"),
            true
        );
        y += EditorGUI.GetPropertyHeight(sceneAssetsProp, true) + spacing;

        // Warnings: escenas no presentes en Build Settings
        for (int i = 0; i < sceneAssetsProp.arraySize; i++)
        {
            var assetProp = sceneAssetsProp.GetArrayElementAtIndex(i);
            var asset = assetProp.objectReferenceValue as SceneAsset;
            if (asset == null) continue;

            string path = AssetDatabase.GetAssetPath(asset);
            if (!IsInBuildSettings(path))
            {
                float warnH = lineH * 1.8f;
                EditorGUI.HelpBox(
                    new Rect(position.x, y, position.width, warnH),
                    $"⚠  '{asset.name}' no está en Build Settings.",
                    MessageType.Warning
                );
                y += warnH + spacing;
            }
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var sceneAssetsProp = property.FindPropertyRelative("sceneAssets");

        float lineH = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;

        // Label + lista
        float height = lineH + spacing;
        height += EditorGUI.GetPropertyHeight(sceneAssetsProp, true) + spacing;

        // Warning por cada escena fuera de Build Settings
        for (int i = 0; i < sceneAssetsProp.arraySize; i++)
        {
            var assetProp = sceneAssetsProp.GetArrayElementAtIndex(i);
            var asset = assetProp.objectReferenceValue as SceneAsset;
            if (asset == null) continue;

            string path = AssetDatabase.GetAssetPath(asset);
            if (!IsInBuildSettings(path))
                height += lineH * 1.8f + spacing;
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