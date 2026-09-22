#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dropdown of the pressure-zone ids in the open scene(s), with the same escape hatches as
/// <see cref="PuzzleIdDrawer"/>: (vacío), (escribir a mano…), and an unknown value kept with a warning.
/// </summary>
[CustomPropertyDrawer(typeof(PressureZoneIdAttribute))]
public class PressureZoneIdDrawer : PropertyDrawer
{
    private const string EmptyLabel = "(vacío)";
    private const string CustomLabel = "(escribir a mano…)";

    private static readonly HashSet<(UnityEngine.Object target, string path)> ManualEntry =
        new HashSet<(UnityEngine.Object, string)>();

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.LabelField(position, label.text, "[PressureZoneId] solo sirve sobre un string.");
            return;
        }

        EditorGUI.BeginProperty(position, label, property);

        (UnityEngine.Object, string) key = (property.serializedObject.targetObject, property.propertyPath);

        if (ManualEntry.Contains(key))
        {
            DrawManualField(position, property, label, key);
            EditorGUI.EndProperty();
            return;
        }

        List<string> ids = CollectZoneIds();
        string current = property.stringValue;
        int match = ids.FindIndex(id => string.Equals(id, current, StringComparison.OrdinalIgnoreCase));

        List<string> options = new List<string> { EmptyLabel };
        options.AddRange(ids);

        bool isOrphan = !string.IsNullOrWhiteSpace(current) && match < 0;
        if (isOrphan) options.Add($"{current}  ⚠ no hay zona con ese id");

        options.Add(CustomLabel);

        int selected =
            string.IsNullOrWhiteSpace(current) ? 0 :
            isOrphan ? options.Count - 2 :
            match + 1;

        int picked = EditorGUI.Popup(position, label.text, selected, options.ToArray());

        if (picked != selected)
        {
            if (picked == 0) property.stringValue = string.Empty;
            else if (picked == options.Count - 1) ManualEntry.Add(key);
            else if (!(isOrphan && picked == options.Count - 2)) property.stringValue = ids[picked - 1];
        }

        EditorGUI.EndProperty();
    }

    private static void DrawManualField(Rect position, SerializedProperty property, GUIContent label,
                                        (UnityEngine.Object, string) key)
    {
        const float buttonWidth = 22f;

        Rect fieldRect = new Rect(position.x, position.y, position.width - buttonWidth - 2f, position.height);
        Rect buttonRect = new Rect(position.xMax - buttonWidth, position.y, buttonWidth, position.height);

        property.stringValue = EditorGUI.TextField(fieldRect, label.text, property.stringValue);

        if (GUI.Button(buttonRect, new GUIContent("▾", "Volver a la lista"))) ManualEntry.Remove(key);
    }

    private static List<string> CollectZoneIds()
    {
        SortedSet<string> ids = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (NemesisPressureZone zone in UnityEngine.Object.FindObjectsByType<NemesisPressureZone>(FindObjectsInactive.Include))
        {
            if (!string.IsNullOrWhiteSpace(zone.ZoneId)) ids.Add(zone.ZoneId);
        }

        return new List<string>(ids);
    }
}
#endif
