#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Transition step: the channel-change transition (<see cref="UISignalTransition"/>) on each signal
/// panel of the profile —
///   - a CanvasGroup on the panel, which the flicker drives;
///   - two overlay children, "SignalStatic" (static burst) and "SignalScanBar" (the sweep), each with
///     a CanvasGroup that ignores the panel's, so the flicker does not dim them;
///   - the trigger: <see cref="UISignalOnEnable"/> (shown screens, Settings tabs) or
///     <see cref="ItemSelectionTransitionTrigger"/> (the inventory's detail panel, which also types
///     its labels out with <see cref="TMPTypewriterReveal"/>).
///
/// The overlays go just under the panel's BevelFrame, which stays the last child: the frame is the
/// edge of the panel, and the static should read as inside it. With fullScreenOverlays they go on
/// the canvas root instead, last, so a screen being shown catches the signal edge to edge while only
/// its panel flickers.
///
/// A panel that carries a BaseScreenView is refused: the view fades that same CanvasGroup, and the
/// flicker and the fade would fight. Put the transition on a child window instead.
/// </summary>
public static class UIStyleTransition
{
    private const float ScanBarHeight = 3f;
    private const float ScanBarAlpha = 0.35f;

    public static void Apply(UIStyleContext ctx)
    {
        foreach (SO_UIStyleProfile.SignalPanelEntry entry in ctx.Profile.signalPanels)
        {
            Transform panel = ctx.Find(entry.path);
            if (panel.GetComponent<BaseScreenView>() != null)
            {
                ctx.Report.AppendLine($"  SKIPPED signal '{entry.path}' — it has a BaseScreenView, whose fade owns that CanvasGroup. Use a child window.");
                continue;
            }

            Wire(ctx, panel, entry);
        }
    }

    private static void Wire(UIStyleContext ctx, Transform panel, SO_UIStyleProfile.SignalPanelEntry entry)
    {
        UIStyleTools.GetOrAdd<CanvasGroup>(panel.gameObject);

        // Where the static and the scan line live: on the panel, or over the whole screen. The scan
        // line sweeps the height of whatever it hangs from, so the root makes it cross the screen.
        Transform host = entry.fullScreenOverlays ? ctx.Root.transform : panel;
        if (host != panel) RemoveOverlays(ctx, panel, entry.path);

        RawImage staticNoise = EnsureOverlay<RawImage>(host, UIStyleTools.StaticName);
        UIStyleTools.Stretch(staticNoise.rectTransform);
        staticNoise.color = new Color(1f, 1f, 1f, 0f);
        staticNoise.raycastTarget = false;

        Image scanBar = EnsureOverlay<Image>(host, UIStyleTools.ScanBarName);
        ConfigureScanBar(scanBar, ctx.Theme);

        // The frame is the edge of the panel; the overlays read as inside it.
        Transform frame = host.Find(UIStyleTools.FrameName);
        if (frame != null) frame.SetAsLastSibling();

        UISignalTransition signal = UIStyleTools.GetOrAdd<UISignalTransition>(panel.gameObject);
        SerializedObject signalData = new SerializedObject(signal);
        signalData.FindProperty("staticNoise").objectReferenceValue = staticNoise;
        signalData.FindProperty("scanBar").objectReferenceValue = scanBar;
        signalData.ApplyModifiedPropertiesWithoutUndo();

        switch (entry.trigger)
        {
            case SO_UIStyleProfile.SignalTrigger.OnEnable:
                UIStyleTools.GetOrAdd<UISignalOnEnable>(panel.gameObject);
                if (entry.typewriters.Count > 0)
                    ctx.Report.AppendLine($"  IGNORED typewriters on '{entry.path}' — only the ItemSelection trigger plays them.");
                ctx.Report.AppendLine($"  signal: '{entry.path}' on enable" + (entry.fullScreenOverlays ? ", static over the whole screen" : ""));
                break;

            case SO_UIStyleProfile.SignalTrigger.ItemSelection:
                List<TMPTypewriterReveal> typewriters = new List<TMPTypewriterReveal>();
                foreach (string label in entry.typewriters)
                    typewriters.Add(UIStyleTools.GetOrAdd<TMPTypewriterReveal>(ctx.Find(SO_UIStyleProfile.Join(entry.path, label)).gameObject));

                ItemSelectionTransitionTrigger trigger = UIStyleTools.GetOrAdd<ItemSelectionTransitionTrigger>(panel.gameObject);
                SerializedObject triggerData = new SerializedObject(trigger);
                triggerData.FindProperty("signal").objectReferenceValue = signal;
                SerializedProperty list = triggerData.FindProperty("typewriters");
                list.arraySize = typewriters.Count;
                for (int i = 0; i < typewriters.Count; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = typewriters[i];
                triggerData.ApplyModifiedPropertiesWithoutUndo();

                ctx.Report.AppendLine($"  signal: '{entry.path}' on item selection + {typewriters.Count} typewriter(s)");
                break;
        }
    }

    /// <summary>
    /// The overlays this step put on the panel before they moved to the whole screen. Left there, the
    /// scan line — authored visible, and no longer switched off by the transition — would sit on the
    /// panel for good. The only nodes any step removes, and only its own.
    /// </summary>
    private static void RemoveOverlays(UIStyleContext ctx, Transform panel, string path)
    {
        foreach (string name in new[] { UIStyleTools.StaticName, UIStyleTools.ScanBarName })
        {
            Transform old = panel.Find(name);
            if (old == null) continue;

            Object.DestroyImmediate(old.gameObject);
            ctx.Report.AppendLine($"  removed '{SO_UIStyleProfile.Join(path, name)}' — the overlays moved to the canvas root");
        }
    }

    private static T EnsureOverlay<T>(Transform panel, string name) where T : Graphic
    {
        Transform existing = panel.Find(name);
        RectTransform rt = existing != null ? (RectTransform)existing : UIStyleTools.CreateUIChild(name, panel);
        rt.SetAsLastSibling();

        // Its own group, deaf to the panel's: the panel's alpha flickers during the transition, and
        // the overlays are what covers that flicker — they must not dim with it.
        CanvasGroup group = UIStyleTools.GetOrAdd<CanvasGroup>(rt.gameObject);
        group.ignoreParentGroups = true;
        group.blocksRaycasts = false;
        group.interactable = false;

        // A panel that lays out its children would stack the overlays into its column.
        if (panel.GetComponent<LayoutGroup>() != null) UIStyleTools.GetOrAdd<LayoutElement>(rt.gameObject).ignoreLayout = true;

        return UIStyleTools.GetOrAdd<T>(rt.gameObject);
    }

    private static void ConfigureScanBar(Image bar, SO_UIThemeConfig theme)
    {
        RectTransform rt = bar.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, ScanBarHeight);
        rt.anchoredPosition = Vector2.zero;
        bar.raycastTarget = false;

        // The alpha is the sweep's peak brightness, not part of the token.
        Color color = bar.color;
        color.a = ScanBarAlpha;
        bar.color = color;
        UIStyleTheme.Paint(bar, theme, UIThemeRole.TextPrimary, preserveAlpha: true);
    }
}
#endif
