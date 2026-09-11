#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wires the detail panel's selection transition:
///   - a CanvasGroup and <see cref="UISignalTransition"/> on Item Selection,
///   - two overlay children for it — "SignalStatic" (static burst) and "SignalScanBar" (the sweep) —
///     each with a CanvasGroup that ignores the panel's, so the flicker does not dim them,
///   - <see cref="TMPTypewriterReveal"/> on the item name and the description,
///   - <see cref="ItemSelectionTransitionTrigger"/> tying them to the selection event.
///
/// The overlays go just under the panel's BevelFrame, which stays the last child: the frame is
/// the edge of the panel, and the static should read as inside it.
///
/// USAGE: Tools / UI / Inventory / Add Selection Transition. See <see cref="InventoryRedesign"/>.
/// </summary>
public static class InventorySelectionTransitionSetup
{
    private const string PanelPath = "LAYOUT/InventoryObjects/Item Selection";
    private const string StaticName = "SignalStatic";
    private const string ScanBarName = "SignalScanBar";
    private const string FrameName = "BevelFrame";

    private static readonly string[] TypedLabels = { "Item Name", "Item Description" };

    private const float ScanBarHeight = 3f;
    private const float ScanBarAlpha = 0.35f;

    [MenuItem("Tools/UI/Inventory/Add Selection Transition", priority = 16)]
    public static void Apply()
    {
        SO_UIThemeConfig theme = InventoryRedesign.LoadRequired<SO_UIThemeConfig>(InventoryRedesign.ThemePath);
        if (theme == null) return;

        StringBuilder report = new StringBuilder();

        InventoryRedesign.EditPrefab(InventoryRedesign.CanvasPrefab, root =>
        {
            Transform panel = InventoryRedesign.Find(root, PanelPath, report);
            if (panel == null) return;

            if (panel.GetComponent<CanvasGroup>() == null) panel.gameObject.AddComponent<CanvasGroup>();

            RawImage staticNoise = EnsureOverlay<RawImage>(panel, StaticName);
            InventoryRedesign.Stretch(staticNoise.rectTransform);
            staticNoise.color = new Color(1f, 1f, 1f, 0f);
            staticNoise.raycastTarget = false;

            Image scanBar = EnsureOverlay<Image>(panel, ScanBarName);
            ConfigureScanBar(scanBar, theme);

            // The frame is the edge of the panel; the overlays read as inside it.
            Transform frame = panel.Find(FrameName);
            if (frame != null) frame.SetAsLastSibling();

            UISignalTransition signal = panel.GetComponent<UISignalTransition>();
            if (signal == null) signal = panel.gameObject.AddComponent<UISignalTransition>();
            SerializedObject signalData = new SerializedObject(signal);
            signalData.FindProperty("staticNoise").objectReferenceValue = staticNoise;
            signalData.FindProperty("scanBar").objectReferenceValue = scanBar;
            signalData.ApplyModifiedPropertiesWithoutUndo();

            List<TMPTypewriterReveal> typewriters = new List<TMPTypewriterReveal>();
            foreach (string label in TypedLabels)
            {
                Transform node = panel.Find(label);
                if (node == null)
                {
                    report.AppendLine($"  MISSING '{PanelPath}/{label}'");
                    continue;
                }

                TMPTypewriterReveal typewriter = node.GetComponent<TMPTypewriterReveal>();
                if (typewriter == null) typewriter = node.gameObject.AddComponent<TMPTypewriterReveal>();
                typewriters.Add(typewriter);
            }

            ItemSelectionTransitionTrigger trigger = panel.GetComponent<ItemSelectionTransitionTrigger>();
            if (trigger == null) trigger = panel.gameObject.AddComponent<ItemSelectionTransitionTrigger>();
            SerializedObject triggerData = new SerializedObject(trigger);
            triggerData.FindProperty("signal").objectReferenceValue = signal;
            SerializedProperty list = triggerData.FindProperty("typewriters");
            list.arraySize = typewriters.Count;
            for (int i = 0; i < typewriters.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = typewriters[i];
            triggerData.ApplyModifiedPropertiesWithoutUndo();

            report.AppendLine($"  wired: signal + {typewriters.Count} typewriter(s)");
        });

        Debug.Log($"[InventorySelectionTransitionSetup] Done.\n{report}");
    }

    private static T EnsureOverlay<T>(Transform panel, string name) where T : Graphic
    {
        Transform existing = panel.Find(name);
        RectTransform rt = existing != null ? (RectTransform)existing : InventoryRedesign.CreateUIChild(name, panel);
        rt.SetAsLastSibling();

        // Its own group, deaf to the panel's: the panel's alpha flickers during the transition, and
        // the overlays are what covers that flicker — they must not dim with it.
        CanvasGroup group = rt.GetComponent<CanvasGroup>();
        if (group == null) group = rt.gameObject.AddComponent<CanvasGroup>();
        group.ignoreParentGroups = true;
        group.blocksRaycasts = false;
        group.interactable = false;

        T graphic = rt.GetComponent<T>();
        if (graphic == null) graphic = rt.gameObject.AddComponent<T>();
        return graphic;
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

        UIThemeApplier applier = bar.GetComponent<UIThemeApplier>();
        if (applier == null) applier = bar.gameObject.AddComponent<UIThemeApplier>();

        SerializedObject serialized = new SerializedObject(applier);
        serialized.FindProperty("theme").objectReferenceValue = theme;
        serialized.FindProperty("role").enumValueIndex = (int)UIThemeRole.TextPrimary;
        // The alpha is the sweep's peak brightness, not part of the token.
        serialized.FindProperty("preserveAlpha").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Color color = theme.TextPrimary;
        color.a = ScanBarAlpha;
        bar.color = color;
    }
}
#endif
