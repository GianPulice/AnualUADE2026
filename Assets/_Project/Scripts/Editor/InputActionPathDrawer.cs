#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Draws an <see cref="InputActionPathAttribute"/> string as a dropdown of the project-wide actions,
/// grouped by map ("Player/Sprint"). With no project-wide asset it falls back to the text field.
///
/// A value that no longer matches any action is kept and shown as-is with "(no existe)", never
/// snapped to another action.
/// </summary>
[CustomPropertyDrawer(typeof(InputActionPathAttribute))]
public class InputActionPathDrawer : PropertyDrawer
{
    private const string EmptyLabel = "(ninguna)";

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.LabelField(position, label.text, "[InputActionPath] solo sirve sobre un string.");
            return;
        }

        InputActionAsset asset = InputSystem.actions;
        if (asset == null)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        List<string> paths = new List<string>();
        foreach (InputActionMap map in asset.actionMaps)
            foreach (InputAction action in map.actions)
                paths.Add($"{map.name}/{action.name}");

        string current = property.stringValue;
        List<string> options = new List<string> { EmptyLabel };
        options.AddRange(paths);

        int index = 0;
        if (!string.IsNullOrEmpty(current))
        {
            index = paths.IndexOf(current) + 1;
            if (index == 0)
            {
                options.Add($"{current} (no existe)");
                index = options.Count - 1;
            }
        }

        EditorGUI.BeginProperty(position, label, property);
        EditorGUI.BeginChangeCheck();
        int picked = EditorGUI.Popup(position, label.text, index, options.ToArray());
        if (EditorGUI.EndChangeCheck() && picked != index)
        {
            if (picked == 0) property.stringValue = string.Empty;
            else if (picked <= paths.Count) property.stringValue = paths[picked - 1];
        }
        EditorGUI.EndProperty();
    }
}
#endif
