using UnityEngine;


[CreateAssetMenu(fileName = "ItemCategoryConfig", menuName = "Scriptable Objects/SO_ItemCategoryConfig")]
public class SO_ItemCategoryConfig : ScriptableObject
{
    [Header("Llaves de acceso")]
    public CategoryVisuals Key;

    [Header("Componentes de puzzle")]
    public CategoryVisuals Component;

    [Header("Pistas e información")]
    public CategoryVisuals Note;

    [Header("Ítems especiales")]
    public CategoryVisuals Special;

    [Header("Fallback (categoría desconocida)")]
    public CategoryVisuals Default;

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Retorna los visuales de la categoría solicitada.
    /// Si la categoría no existe, retorna Default.
    /// </summary>
    public CategoryVisuals Get(ItemCategory category) => category switch
    {
        ItemCategory.Key => Key,
        ItemCategory.Component => Component,
        ItemCategory.Note => Note,
        ItemCategory.Special => Special,
        _ => Default
    };

    // ── Valores por defecto (llamar desde el botón Reset del Inspector) ────────

    /// <summary>
    /// Rellena todos los campos con los valores de diseño originales de WIRED.
    /// Útil para resetear a los valores base si se rompe algo.
    /// Llamar manualmente desde el Inspector con el botón Reset o via código en editor.
    /// </summary>
    [ContextMenu("Resetear a valores de diseño WIRED")]
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
            GroupLabel         = "— key",
            TagLabel           = "Key",
            // §4.4 Color Spec — tinte 3D ItemPSX
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
            GroupLabel         = "— component",
            TagLabel           = "Puzzle Component",
            shaderTintColor    = HexToColor("#4E342E"),
            shaderEmissionColor = HexToColor("#FFC850"), // ámbar tenue — visible solo con _EmissionIntensity > 0
        };

        Note = new CategoryVisuals
        {
            MainColor          = HexToColor("#1A1A80"),
            BackgroundColor    = HexToColor("#1A1A80"),
            ButtonColor        = HexToColor("#1A1A80"),
            TextColor          = HexToColor("#999999"),
            SelectedTextColor  = HexToColor("#E0E0E0"),
            SelectedBGColor    = HexToColor("#2424B3"),
            GroupLabel         = "— note",
            TagLabel           = "Notes",
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
            GroupLabel         = "— essential",
            TagLabel           = "Essential",
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
            GroupLabel         = "— Others",
            TagLabel           = "Ítem",
            shaderTintColor    = HexToColor("#37474F"),
            shaderEmissionColor = Color.black,
        };

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log("[SO_ItemCategoryConfig] Valores de diseño UI restaurados.");
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
    Key,        // Llaves de acceso — rojo oscuro
    Component,  // Componentes de puzzle — verde oscuro
    Note,       // Pistas e información — azul oscuro
    Special     // Ítems especiales — amarillo oscuro
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

    [Header("Shader 3D (ItemPSX) — §4.4 Color Spec")]
    [Tooltip("Color del tinte en _TintColor del shader ItemPSX.")]
    public Color shaderTintColor;
    [Tooltip("Color de emisión en _EmissionColor. Negro = sin emisión (salvo Componentes: ámbar).")]
    public Color shaderEmissionColor;
}
