using UnityEngine;


[CreateAssetMenu(fileName = "ItemCategoryConfig", menuName = "Scriptable Objects/SO_ItemCategoryConfig")]
public class SO_ItemCategoryConfig : ScriptableObject
{
    [Header("Access keys")]
    public CategoryVisuals Key;

    [Header("Puzzle components")]
    public CategoryVisuals Component;

    [Header("Clues and information")]
    public CategoryVisuals Note;

    [Header("Special items")]
    public CategoryVisuals Special;

    [Header("Fallback (unknown category)")]
    public CategoryVisuals Default;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the visuals of the requested category.
    /// If the category does not exist, returns Default.
    /// </summary>
    public CategoryVisuals Get(ItemCategory category) => category switch
    {
        ItemCategory.Key => Key,
        ItemCategory.Component => Component,
        ItemCategory.Note => Note,
        ItemCategory.Special => Special,
        _ => Default
    };

    // ── Default values (call from the Inspector's Reset button) ───────────────

    /// <summary>
    /// Fills every field with WIRED's original design values.
    /// Useful for resetting back to the base values if something breaks.
    /// Call manually from the Inspector with the Reset button or via editor code.
    /// </summary>
    [ContextMenu("Reset to WIRED design values")]
    public void ResetToDesignDefaults()
    {
        Key = new CategoryVisuals
        {
            MainColor          = HexToColor("#801A1A"),
            BackgroundColor    = HexToColor("#801A1A"),
            ButtonColor        = HexToColor("#801A1A"),
            TextColor          = HexToColor("#999999"),
            SelectedTextColor  = HexToColor("#E0E0E0"),
            SelectedBGColor    = HexToColor("#A62222"),
            GroupLabel         = "// KEYS",
            TagLabel           = "KEY",
            // §4.4 Color Spec — ItemPSX 3D tint
            shaderTintColor    = HexToColor("#37474F"),
            shaderEmissionColor = Color.black,
        };

        Component = new CategoryVisuals
        {
            MainColor          = HexToColor("#1A661A"),
            BackgroundColor    = HexToColor("#1A661A"),
            ButtonColor        = HexToColor("#1A661A"),
            TextColor          = HexToColor("#999999"),
            SelectedTextColor  = HexToColor("#E0E0E0"),
            SelectedBGColor    = HexToColor("#248F24"),
            GroupLabel         = "// COMPONENTS",
            TagLabel           = "CMP",
            shaderTintColor    = HexToColor("#4E342E"),
            shaderEmissionColor = HexToColor("#FFC850"), // faint amber — only visible with _EmissionIntensity > 0
        };

        Note = new CategoryVisuals
        {
            MainColor          = HexToColor("#1A1A80"),
            BackgroundColor    = HexToColor("#1A1A80"),
            ButtonColor        = HexToColor("#1A1A80"),
            TextColor          = HexToColor("#999999"),
            SelectedTextColor  = HexToColor("#E0E0E0"),
            SelectedBGColor    = HexToColor("#2424B3"),
            GroupLabel         = "// NOTES",
            TagLabel           = "DOC",
            shaderTintColor    = HexToColor("#263238"),
            shaderEmissionColor = Color.black,
        };

        Special = new CategoryVisuals
        {
            MainColor          = HexToColor("#8C5319"),
            BackgroundColor    = HexToColor("#8C5319"),
            ButtonColor        = HexToColor("#8C5319"),
            TextColor          = HexToColor("#999999"),
            SelectedTextColor  = HexToColor("#E0E0E0"),
            SelectedBGColor    = HexToColor("#BF7326"),
            GroupLabel         = "// ESSENTIAL",
            TagLabel           = "ESS",
            shaderTintColor    = HexToColor("#1A237E"),
            shaderEmissionColor = Color.black,
        };

        Default = new CategoryVisuals
        {
            MainColor          = HexToColor("#333333"),
            BackgroundColor    = HexToColor("#333333"),
            ButtonColor        = HexToColor("#333333"),
            TextColor          = HexToColor("#999999"),
            SelectedTextColor  = HexToColor("#E0E0E0"),
            SelectedBGColor    = HexToColor("#4D4D4D"),
            GroupLabel         = "// OTHER",
            TagLabel           = "ITM",
            shaderTintColor    = HexToColor("#37474F"),
            shaderEmissionColor = Color.black,
        };

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log("[SO_ItemCategoryConfig] UI design values restored.");
#endif
    }
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Color HexToColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }
}
    public enum ItemCategory
{
    Key,        // Access keys — dark red
    Component,  // Puzzle components — dark green
    Note,       // Clues and information — dark blue
    Special     // Special items — dark yellow
}
[System.Serializable]
public struct CategoryVisuals
{
    public Color MainColor;
    public Color BackgroundColor;
    public Color ButtonColor;
    public Color TextColor;
    public Color SelectedBGColor;
    public Color SelectedTextColor;

    [Header("UI")]
    public string GroupLabel;
    public string TagLabel;

    [Header("3D Shader (ItemPSX) — §4.4 Color Spec")]
    [Tooltip("Tint color written to _TintColor of the ItemPSX shader.")]
    public Color shaderTintColor;
    [Tooltip("Emission color written to _EmissionColor. Black = no emission (except Components: amber).")]
    public Color shaderEmissionColor;
}
