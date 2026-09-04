#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws a <see cref="SoundIdAttribute"/> string as a dropdown of the sound ids that exist in the
/// project, grouped by category.
///
/// Modelled on <see cref="PuzzleIdDrawer"/>, with two differences that come from the data:
///   - Sound ids all live on ONE asset type, so <c>t:SO_SoundData</c> finds them directly. The
///     puzzle drawer has to scan every ScriptableObject and filter by type name because its five
///     puzzle SOs share no base class.
///   - The list is grouped into submenus by <c>SoundCategory</c>. There are already more sounds
///     than puzzles and the gap only widens as UI audio lands, so a flat list of thirty entries
///     would be worse than the text box this replaces.
///
/// <b>The blank-id fallback is the subtle part.</b> <c>SO_SoundData.Id</c> returns the asset NAME
/// when its <c>id</c> field is empty, and several assets in the project rely on that. Reading only
/// the serialized field would silently omit exactly those sounds from the dropdown — present at
/// runtime, invisible in the inspector. <see cref="ResolveId"/> mirrors that fallback.
///
/// Two escape hatches matter as much as the list does:
///   - <b>(vacío)</b> clears the field. Most of these are optional ("leave empty for silence"), and
///     a dropdown with no way back to empty would make that unreachable.
///   - <b>(escribir a mano…)</b> falls back to the raw text field, so an id whose asset does not
///     exist yet — audio someone is still importing — is not blocked by the tool meant to help.
///
/// A value already set that no longer matches any asset is shown as-is and kept selected, with a
/// warning next to it. Silently snapping it to the first id in the list would be the one failure
/// this drawer must never cause: rewriting a designer's wiring nobody asked to change.
/// </summary>
[CustomPropertyDrawer(typeof(SoundIdAttribute))]
public class SoundIdDrawer : PropertyDrawer
{
    private const string EmptyLabel = "(vacío)";
    private const string CustomLabel = "(escribir a mano…)";

    /// <summary>Set per-field once the user picks "escribir a mano". Not persisted: it is a view
    /// mode, not data, and it should reset the next time the inspector is opened fresh.
    ///
    /// Keyed by (object, propertyPath) rather than by an instance id on purpose: the id
    /// accessors are a moving target (GetInstanceID is deprecated in favour of GetEntityId),
    /// and the pair identifies the field just as precisely without depending on either.</summary>
    private static readonly HashSet<(UnityEngine.Object target, string path)> ManualEntry =
        new HashSet<(UnityEngine.Object, string)>();

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.LabelField(position, label.text, "[SoundId] solo sirve sobre un string.");
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

        string current = property.stringValue;

        // Two parallel lists rather than index arithmetic over one: options[i] is what the popup
        // shows, values[i] is what gets written. A null value means "this entry is not an id" —
        // today only the manual-entry row. Keeping them aligned is what stops the special rows
        // (empty / orphan / manual) from having to be reasoned about by position.
        List<string> options = new List<string>();
        List<string> values  = new List<string>();

        options.Add(EmptyLabel);
        values.Add(string.Empty);

        bool found = false;
        foreach (SoundEntry entry in CollectSounds())
        {
            options.Add(entry.Display);
            values.Add(entry.Id);

            if (entry.Id == current) found = true;
        }

        if (!string.IsNullOrWhiteSpace(current) && !found)
        {
            options.Add($"{current}  ⚠ no existe");
            values.Add(current);
        }

        options.Add(CustomLabel);
        values.Add(null);

        int selected = string.IsNullOrWhiteSpace(current) ? 0 : values.IndexOf(current);
        if (selected < 0) selected = 0;

        int picked = EditorGUI.Popup(position, label.text, selected, options.ToArray());

        if (picked != selected)
        {
            if (values[picked] == null) ManualEntry.Add(key);
            else                        property.stringValue = values[picked];
        }

        EditorGUI.EndProperty();
    }

    /// <summary>Raw text field plus a way back to the dropdown, so the manual mode is never a
    /// one-way door.</summary>
    private static void DrawManualField(Rect position, SerializedProperty property,
                                        GUIContent label,
                                        (UnityEngine.Object, string) key)
    {
        const float buttonWidth = 22f;

        Rect fieldRect = new Rect(position.x, position.y,
                                  position.width - buttonWidth - 2f, position.height);
        Rect buttonRect = new Rect(position.xMax - buttonWidth, position.y,
                                   buttonWidth, position.height);

        property.stringValue = EditorGUI.TextField(fieldRect, label.text, property.stringValue);

        if (GUI.Button(buttonRect, new GUIContent("▾", "Volver a la lista"))) ManualEntry.Remove(key);
    }

    private readonly struct SoundEntry
    {
        public readonly string Id;
        public readonly string Display;

        public SoundEntry(string id, string display)
        {
            Id = id;
            Display = display;
        }
    }

    /// <summary>
    /// Every sound id declared by an SO_SoundData in the project, sorted by category then id, with
    /// the category as a popup submenu.
    ///
    /// Not cached: this runs only while an inspector with one of these fields is being drawn, and a
    /// cache would go stale exactly when it matters — right after someone imports the clip they are
    /// about to wire up.
    /// </summary>
    private static List<SoundEntry> CollectSounds()
    {
        // Sorted by "Category/id" so the submenu groups come out contiguous, which is what
        // EditorGUI.Popup needs to render them as groups rather than repeated headers.
        SortedDictionary<string, string> byDisplay = new SortedDictionary<string, string>();

        foreach (string guid in AssetDatabase.FindAssets("t:SO_SoundData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            SO_SoundData sound = AssetDatabase.LoadAssetAtPath<SO_SoundData>(path);
            if (sound == null) continue;

            string id = ResolveId(sound);
            if (string.IsNullOrWhiteSpace(id)) continue;

            // A '/' inside an id would be read by EditorGUI.Popup as a submenu separator and split
            // the entry in two, so it is shown flat. The value written is the real id either way.
            string display = id.Contains("/") ? id : $"{sound.Category}/{id}";

            byDisplay[display] = id;
        }

        List<SoundEntry> entries = new List<SoundEntry>(byDisplay.Count);
        foreach (KeyValuePair<string, string> pair in byDisplay)
            entries.Add(new SoundEntry(pair.Value, pair.Key));

        return entries;
    }

    /// <summary>
    /// Mirrors <c>SO_SoundData.Id</c>: the serialized <c>id</c> field, falling back to the asset
    /// name when it is blank. Read through the property rather than the accessor so a null or
    /// half-imported asset cannot throw while an inspector is mid-draw.
    /// </summary>
    private static string ResolveId(SO_SoundData sound)
    {
        SerializedProperty idProperty = new SerializedObject(sound).FindProperty("id");

        if (idProperty != null && !string.IsNullOrWhiteSpace(idProperty.stringValue))
            return idProperty.stringValue;

        return sound.name;
    }
}
#endif
