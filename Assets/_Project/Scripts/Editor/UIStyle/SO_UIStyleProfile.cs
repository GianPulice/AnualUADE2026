#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using Style = UIBevelFrame.BevelStyle;

/// <summary>
/// How one UI prefab wears the WIRED look, as data: which node takes which theme colour, which get a
/// Win95 frame, which texts are titles, which windows animate, which panels play the channel-change
/// transition, and whether the canvas goes through the CRT tube.
///
/// One asset per prefab. <see cref="UIStyleTools"/> applies it; <see cref="UIStyleProfileDrafter"/>
/// writes a first draft from the prefab itself, to be reviewed before applying.
///
/// Every path is from the prefab root, "" being the root itself, and is resolved by
/// Transform.Find. Applying stops before saving if any path is missing, so a renamed node is caught
/// instead of silently left on its old look.
///
/// Editor-only: nothing at runtime reads a profile. What it produces — UIThemeApplier, UIBevelFrame,
/// the outline presets, CanvasCRTPresenter — is serialized into the prefab.
/// </summary>
[CreateAssetMenu(fileName = "UIStyle_", menuName = "WIRED/UI/Style Profile")]
public class SO_UIStyleProfile : ScriptableObject
{
    [Serializable]
    public struct ThemeEntry
    {
        public string path;
        public UIThemeRole role;
        [Tooltip("Take only RGB from the token and keep the node's authored alpha.")]
        public bool preserveAlpha;
    }

    [Serializable]
    public struct FrameEntry
    {
        public string path;
        public Style style;
        [Tooltip("Adds UIBevelPressFeedback: the frame sinks while the button is held. Needs a Button.")]
        public bool pressable;
    }

    public enum SignalTrigger
    {
        [Tooltip("UISignalOnEnable: plays whenever the panel is activated (a screen shown, a tab selected).")]
        OnEnable,
        [Tooltip("ItemSelectionTransitionTrigger: plays when the inventory selection changes. Inventory only.")]
        ItemSelection,
    }

    [Serializable]
    public class SignalPanelEntry
    {
        [Tooltip("Gets its own CanvasGroup, so it must not be the node a BaseScreenView fades: the " +
                 "flicker and the fade would fight over the same alpha.")]
        public string path;
        public SignalTrigger trigger;
        [Tooltip("Put the static and the scan line on the canvas root, so the whole screen catches the " +
                 "signal while only the panel flickers. For a screen being shown; a tab switch stays inside its panel.")]
        public bool fullScreenOverlays;
        [Tooltip("Relative to the panel. Typed out with TMPTypewriterReveal; only the ItemSelection trigger plays them.")]
        public List<string> typewriters = new List<string>();
    }

    [Serializable]
    public class CRTSettings
    {
        public bool enabled;

        [Tooltip("The tube renders only while this is active. Empty = always, which is the same as the " +
                 "root, since the presenter lives there.")]
        public string contentPath = "";

        [Tooltip("For screens that stay active and hide by alpha (Result, Win): the tube renders only " +
                 "while this CanvasGroup's alpha is above zero.")]
        public bool useVisibility;
        public string visibilityPath = "";

        [Tooltip("UI_CRT.mat, or a Material Variant of it for a screen that needs other values.")]
        public Material material;
    }

    [Tooltip("The prefab asset this profile styles.")]
    public GameObject prefab;

    [Header("Nested prefabs")]
    [Tooltip("Before styling, revert the colour, font, material, sprite and theme overrides this prefab " +
             "holds on the prefabs nested in it, so each shows the look its own profile gives it. " +
             "Theme entries below that point into a nested prefab are written again right after.")]
    public bool revertNestedStyleOverrides;

    [Header("Theme colours (UIThemeApplier)")]
    public List<ThemeEntry> theme = new List<ThemeEntry>();
    [Tooltip("Images whose sprite is dropped so the theme colour fills the rect flat: chrome sprites " +
             "(rounded, gradient, border) that the Win95 frame replaces. Never icons.")]
    public List<string> flatFills = new List<string>();

    [Header("Win95 frames (UIBevelFrame child)")]
    public List<FrameEntry> frames = new List<FrameEntry>();
    [Tooltip("A Sunken frame covers the edge of its well, and whatever the well lays out right against " +
             "that edge gets cut. Each side of the well's LayoutGroup (its own, or its ScrollRect " +
             "content's) is raised to at least this much. 0 = leave paddings alone.")]
    [Min(0)] public int minWellPadding = 8;

    [Header("Texts")]
    [Tooltip("Off = the text step does not run on this prefab at all.")]
    public bool restyleTexts = true;
    [Tooltip("Oswald with outline. Every other text of the prefab goes to ShareTechMono with outline.")]
    public List<string> titleTexts = new List<string>();
    [Tooltip("Texts the text step leaves exactly as they are.")]
    public List<string> untouchedTexts = new List<string>();

    [Header("Animated window backgrounds")]
    public Material surfaceMaterial;
    public List<string> animatedSurfaces = new List<string>();

    [Header("Channel-change transition (UISignalTransition)")]
    public List<SignalPanelEntry> signalPanels = new List<SignalPanelEntry>();

    [Header("CRT tube (CanvasCRTPresenter)")]
    public CRTSettings crt = new CRTSettings();

    /// <summary>Every path the profile names, with what names it — for the check before applying.</summary>
    public IEnumerable<(string path, string usedBy)> AllPaths()
    {
        foreach (ThemeEntry e in theme) yield return (e.path, "theme");
        foreach (string p in flatFills) yield return (p, "flatFills");
        foreach (FrameEntry e in frames) yield return (e.path, "frames");
        foreach (string p in titleTexts) yield return (p, "titleTexts");
        foreach (string p in untouchedTexts) yield return (p, "untouchedTexts");
        foreach (string p in animatedSurfaces) yield return (p, "animatedSurfaces");

        foreach (SignalPanelEntry panel in signalPanels)
        {
            yield return (panel.path, "signalPanels");
            foreach (string label in panel.typewriters)
                yield return (Join(panel.path, label), "signalPanels.typewriters");
        }

        if (!crt.enabled) yield break;
        yield return (crt.contentPath, "crt.content");
        if (crt.useVisibility) yield return (crt.visibilityPath, "crt.visibility");
    }

    public static string Join(string parent, string child) =>
        string.IsNullOrEmpty(parent) ? child : string.IsNullOrEmpty(child) ? parent : parent + "/" + child;
}
#endif
