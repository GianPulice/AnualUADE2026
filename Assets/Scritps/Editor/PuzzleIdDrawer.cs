#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws a <see cref="PuzzleIdAttribute"/> string as a dropdown of the puzzle ids that exist in
/// the project.
///
/// The ids are read out of the puzzle assets themselves rather than from a hardcoded list, so
/// adding a puzzle adds an entry with nothing else to remember. The five puzzle SO types share no
/// base class, so they are collected by name — see <see cref="PuzzleDataTypeNames"/>.
///
/// Two escape hatches matter as much as the list does:
///   - <b>(vacío)</b> clears the field. Several of these are optional gates ("leave empty and the
///     Nemesis is active from the first frame"), and a dropdown with no way back to empty would
///     make that unreachable.
///   - <b>(escribir a mano…)</b> falls back to the raw text field, so an id that does not exist
///     yet — a puzzle someone else is still building, an id typed from a design doc — is not
///     blocked by the tool meant to help.
///
/// A value already set that no longer matches any asset is shown as-is and kept selected, with a
/// warning next to it. Silently snapping it to the first id in the list would be the one failure
/// this drawer must never cause: rewriting a gate nobody asked to change.
/// </summary>
[CustomPropertyDrawer(typeof(PuzzleIdAttribute))]
public class PuzzleIdDrawer : PropertyDrawer
{
    /// <summary>
    /// The SO types that declare a puzzle id. By name because they have no common base type to
    /// filter on — <c>t:ScriptableObject</c> plus a name check is the cheapest thing that works
    /// and does not require touching the five assets.
    /// </summary>
    private static readonly string[] PuzzleDataTypeNames =
    {
        "SO_PuzzleData",
        "SO_ValvePuzzleData",
        "SO_HubPuzzleData",
        "SO_SequencePuzzleData",
        "SO_ContainerPuzzleData",
    };

    private const string EmptyLabel = "(vacío)";
    private const string CustomLabel = "(escribir a mano…)";

    /// <summary>Set per-field once the user picks "escribir a mano". Not persisted: it is a view
    /// mode, not data, and it should reset the next time the inspector is opened fresh.</summary>
    private static readonly HashSet<string> ManualEntry = new HashSet<string>();

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.LabelField(position, label.text, "[PuzzleId] solo sirve sobre un string.");
            return;
        }

        EditorGUI.BeginProperty(position, label, property);

        string key = property.propertyPath + property.serializedObject.targetObject.GetInstanceID();

        if (ManualEntry.Contains(key))
        {
            DrawManualField(position, property, label, key);
            EditorGUI.EndProperty();
            return;
        }

        List<string> ids = CollectPuzzleIds();
        string current = property.stringValue;

        // Options: empty, every id found, the current value when it matches nothing, then manual.
        List<string> options = new List<string> { EmptyLabel };
        options.AddRange(ids);

        bool isOrphan = !string.IsNullOrWhiteSpace(current) && !ids.Contains(current);
        if (isOrphan) options.Add($"{current}  ⚠ no existe");

        options.Add(CustomLabel);

        int selected =
            string.IsNullOrWhiteSpace(current) ? 0 :
            isOrphan ? options.Count - 2 :
            ids.IndexOf(current) + 1;

        int picked = EditorGUI.Popup(position, label.text, selected, options.ToArray());

        if (picked == selected)
        {
            EditorGUI.EndProperty();
            return;
        }

        if (picked == 0) property.stringValue = string.Empty;
        else if (picked == options.Count - 1) ManualEntry.Add(key);
        else if (!(isOrphan && picked == options.Count - 2)) property.stringValue = ids[picked - 1];

        EditorGUI.EndProperty();
    }

    /// <summary>Raw text field plus a way back to the dropdown, so the manual mode is never a
    /// one-way door.</summary>
    private static void DrawManualField(Rect position, SerializedProperty property,
                                        GUIContent label, string key)
    {
        const float buttonWidth = 22f;

        Rect fieldRect = new Rect(position.x, position.y,
                                  position.width - buttonWidth - 2f, position.height);
        Rect buttonRect = new Rect(position.xMax - buttonWidth, position.y,
                                   buttonWidth, position.height);

        property.stringValue = EditorGUI.TextField(fieldRect, label.text, property.stringValue);

        if (GUI.Button(buttonRect, new GUIContent("▾", "Volver a la lista"))) ManualEntry.Remove(key);
    }

    /// <summary>
    /// Every puzzle id declared by an asset in the project, sorted and de-duplicated.
    ///
    /// Not cached: this runs only while an inspector with one of these fields is being drawn, and
    /// a cache would go stale exactly when it matters — right after someone adds the puzzle they
    /// are about to wire up.
    /// </summary>
    private static List<string> CollectPuzzleIds()
    {
        SortedSet<string> ids = new SortedSet<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset == null) continue;

            if (System.Array.IndexOf(PuzzleDataTypeNames, asset.GetType().Name) < 0) continue;

            SerializedProperty idProperty = new SerializedObject(asset).FindProperty("puzzleId");
            if (idProperty == null || string.IsNullOrWhiteSpace(idProperty.stringValue)) continue;

            ids.Add(idProperty.stringValue);
        }

        return new List<string>(ids);
    }
}
#endif
