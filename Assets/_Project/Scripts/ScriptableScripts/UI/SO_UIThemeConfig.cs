using UnityEngine;

/// <summary>
/// Single source of truth for the colors of the WIRED UI ("grey 1995 application" look).
///
/// The palette was NOT invented here: it was extracted from CanvasSettings.prefab, which was
/// already built to the target mockup. Keeping it in one asset stops the next panel from
/// diverging the way the inventory did.
///
/// Usage: drop a <see cref="UIThemeApplier"/> on any Image / TextMeshProUGUI that must follow
/// the theme and pick a role. Do not hardcode colors in prefabs any more.
///
/// Note on the accent: docs/Materials-System.md reserves #CC1A1A for danger in the 3D world
/// (emergency lights, exploded modules, traps). In the UI it is the single accent color, which
/// is what CanvasSettings.prefab already does and what the inventory spec asks for in the
/// selected-item border. The 3D rule is unaffected.
/// </summary>
[CreateAssetMenu(fileName = "UITheme", menuName = "Scriptable Objects/SO_UIThemeConfig")]
public class SO_UIThemeConfig : ScriptableObject
{
    [Header("Surfaces")]
    public Color SurfaceScreen;
    public Color SurfacePanel;
    public Color SurfaceRaised;
    public Color SurfaceFooter;
    public Color SurfaceTabs;
    [Tooltip("Full-screen dim behind a modal. Carries its own alpha.")]
    public Color Dim;

    [Header("Borders")]
    public Color BorderHairline;
    public Color BorderStrong;
    public Color Divider;

    [Header("Text")]
    public Color TextPrimary;
    public Color TextSecondary;
    public Color TextMuted;
    public Color TextDisabled;

    [Header("Accent")]
    public Color Accent;
    public Color AccentHover;
    public Color AccentBgSubtle;
    public Color AccentBgDeep;
    public Color AccentBorder;

    // -- Public API -------------------

    /// <summary>Returns the color of the requested role. Unknown roles fall back to TextPrimary.</summary>
    public Color Get(UIThemeRole role) => role switch
    {
        UIThemeRole.SurfaceScreen  => SurfaceScreen,
        UIThemeRole.SurfacePanel   => SurfacePanel,
        UIThemeRole.SurfaceRaised  => SurfaceRaised,
        UIThemeRole.SurfaceFooter  => SurfaceFooter,
        UIThemeRole.SurfaceTabs    => SurfaceTabs,
        UIThemeRole.Dim            => Dim,
        UIThemeRole.BorderHairline => BorderHairline,
        UIThemeRole.BorderStrong   => BorderStrong,
        UIThemeRole.Divider        => Divider,
        UIThemeRole.TextPrimary    => TextPrimary,
        UIThemeRole.TextSecondary  => TextSecondary,
        UIThemeRole.TextMuted      => TextMuted,
        UIThemeRole.TextDisabled   => TextDisabled,
        UIThemeRole.Accent         => Accent,
        UIThemeRole.AccentHover    => AccentHover,
        UIThemeRole.AccentBgSubtle => AccentBgSubtle,
        UIThemeRole.AccentBgDeep   => AccentBgDeep,
        UIThemeRole.AccentBorder   => AccentBorder,
        _ => TextPrimary
    };

    // -- Default values (call from the Inspector's context menu) -------------------

    /// <summary>
    /// Fills every field with the values read out of CanvasSettings.prefab and the
    /// options_menu_v2_wired.html mockup. Use it to get back to the base palette.
    /// </summary>
    [ContextMenu("Reset to WIRED design values")]
    public void ResetToDesignDefaults()
    {
        // Surfaces
        SurfaceScreen  = HexToColor("#111111"); // outermost frame
        SurfacePanel   = HexToColor("#1E1E1E"); // Panel_Brightness / Controls / Screen / Volume
        SurfaceRaised  = HexToColor("#242424"); // pause box, key badges, raised rows
        SurfaceFooter  = HexToColor("#1A1A1A"); // Footer, topbar
        SurfaceTabs    = HexToColor("#191919"); // tab rail
        Dim            = HexToColor("#222222B2"); // BG_dim — 70% alpha

        // Borders
        BorderHairline = HexToColor("#2E2E2E");
        BorderStrong   = HexToColor("#3A3A3A"); // idle button border, hover on hairline
        Divider        = HexToColor("#272727");

        // Text
        TextPrimary    = HexToColor("#E0E0E0");
        TextSecondary  = HexToColor("#BBBBBB");
        TextMuted      = HexToColor("#888888");
        TextDisabled   = HexToColor("#555555");

        // Accent — the only chromatic color in the whole UI
        Accent         = HexToColor("#CC1A1A"); // ActiveIndicator_*, selected row bar
        AccentHover    = HexToColor("#EE3333");
        AccentBgSubtle = HexToColor("#1A0808"); // selected row background
        AccentBgDeep   = HexToColor("#140606"); // BtnApply background
        AccentBorder   = HexToColor("#4B0C0C"); // BtnReset border

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log("[SO_UIThemeConfig] UI design values restored.");
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor only: lets every live UIThemeApplier repaint as soon as a token changes,
    /// so the palette can be tuned without entering Play mode.
    /// </summary>
    public static event System.Action OnThemeChanged;

    private void OnValidate() => OnThemeChanged?.Invoke();
#endif

    // -- Helpers -------------------

    private static Color HexToColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }
}

public enum UIThemeRole
{
    SurfaceScreen,
    SurfacePanel,
    SurfaceRaised,
    SurfaceFooter,
    SurfaceTabs,
    Dim,

    BorderHairline,
    BorderStrong,
    Divider,

    TextPrimary,
    TextSecondary,
    TextMuted,
    TextDisabled,

    Accent,
    AccentHover,
    AccentBgSubtle,
    AccentBgDeep,
    AccentBorder
}
