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
            MainColor = HexToColor("#801A1A"),        // Rojo oscuro
            BackgroundColor = HexToColor("#801A1A"),
            ButtonColor = HexToColor("#801A1A"),
            TextColor = HexToColor("#999999"),        // Gris medio
            SelectedTextColor = HexToColor("#E0E0E0"),// Gris claro al seleccionar
            SelectedBGColor = HexToColor("#A62222"),  // Rojo más claro al seleccionar
            GroupLabel = "— key",
            TagLabel = "Key"
        };

        Component = new CategoryVisuals
        {
            MainColor = HexToColor("#1A661A"),        // Verde oscuro
            BackgroundColor = HexToColor("#1A661A"),
            ButtonColor = HexToColor("#1A661A"),
            TextColor = HexToColor("#999999"),
            SelectedTextColor = HexToColor("#E0E0E0"),
            SelectedBGColor = HexToColor("#248F24"),  // Verde más claro al seleccionar
            GroupLabel = "— component",
            TagLabel = "Puzzle Component"
        };

        Note = new CategoryVisuals
        {
            MainColor = HexToColor("#1A1A80"),        // Azul oscuro
            BackgroundColor = HexToColor("#1A1A80"),
            ButtonColor = HexToColor("#1A1A80"),
            TextColor = HexToColor("#999999"),
            SelectedTextColor = HexToColor("#E0E0E0"),
            SelectedBGColor = HexToColor("#2424B3"),  // Azul más claro al seleccionar
            GroupLabel = "— note",
            TagLabel = "Notes"
        };

        Special = new CategoryVisuals
        {
            MainColor = HexToColor("#8C5319"),        // Naranja oscuro / Marrón
            BackgroundColor = HexToColor("#8C5319"),
            ButtonColor = HexToColor("#8C5319"),
            TextColor = HexToColor("#999999"),
            SelectedTextColor = HexToColor("#E0E0E0"),
            SelectedBGColor = HexToColor("#BF7326"),  // Naranja más claro al seleccionar
            GroupLabel = "— essential",              // Actualizado para coincidir con la UI
            TagLabel = "Essential"
        };

        Default = new CategoryVisuals
        {
            MainColor = HexToColor("#333333"),        // Gris oscuro
            BackgroundColor = HexToColor("#333333"),
            ButtonColor = HexToColor("#333333"),
            TextColor = HexToColor("#999999"),
            SelectedTextColor = HexToColor("#E0E0E0"),
            SelectedBGColor = HexToColor("#4D4D4D"),  // Gris medio al seleccionar
            GroupLabel = "— Others",
            TagLabel = "Ítem"
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
    public Color MainColor;              // Color principal del slot
    public Color BackgroundColor;        // Fondo del icono
    public Color ButtonColor;            // Color del boton
    public Color TextColor;              // Texto normal
    public Color SelectedBGColor;        // Fondo al seleccionar
    public Color SelectedTextColor;      // Fondo texto al seleccionar

    [Header("UI")]
    public string GroupLabel;            // "— llaves"
    public string TagLabel;              // "Llave de acceso"
}
