#if UNITY_EDITOR
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds the inventory's title-bar close button: "[X] CLOSE [TAB]", sitting on top of the main
/// panel's top-right corner like a tab. It says both ways out — the click and the key — because the
/// art notes asked for exactly that: a legible close, with a cross or with Tab, at the top.
///
/// It goes in the strip between the panel and the module bar rather than inside the panel: inside,
/// it would sit on top of the item name header of the detail area.
///
/// Only creates the node. Colour, frame and outline come from the other redesign tools like every
/// other node, so it cannot drift from them. Skipped if the button already exists.
///
/// USAGE: Tools / UI / Inventory / Add Close Button. See <see cref="InventoryRedesign"/>.
/// </summary>
public static class InventoryCloseButtonSetup
{
    public const string ButtonName = "CloseInventoryButton";

    private const string ParentPath = "LAYOUT/InventoryObjects";
    private const string PanelPath  = "LAYOUT/DecorationObjects/Inventario Fondo";
    private const string LabelText  = "[X] CLOSE [TAB]";

    private static readonly Vector2 Size = new Vector2(260f, 44f);
    private const float GapAbovePanel = 6f;
    private const float FontSize = 22f;

    [MenuItem("Tools/UI/Inventory/Add Close Button", priority = 10)]
    public static void Apply()
    {
        TMP_FontAsset font = InventoryRedesign.LoadRequired<TMP_FontAsset>(InventoryRedesign.FontPath);
        if (font == null) return;

        StringBuilder report = new StringBuilder();
        string result = null;

        InventoryRedesign.EditPrefab(InventoryRedesign.CanvasPrefab, root =>
        {
            Transform parent = InventoryRedesign.Find(root, ParentPath, report);
            RectTransform panel = InventoryRedesign.Find(root, PanelPath, report) as RectTransform;
            if (parent == null || panel == null) return;

            if (parent.Find(ButtonName) != null)
            {
                result = "already there, left as is";
                return;
            }

            Build(parent, panel, font);
            result = "added";
        });

        Debug.Log($"[InventoryCloseButtonSetup] {ButtonName}: {result ?? "failed"}.\n{report}");
    }

    private static void Build(Transform parent, RectTransform panel, TMP_FontAsset font)
    {
        RectTransform rt = InventoryRedesign.CreateUIChild(ButtonName, parent);

        // Anchored to the panel's own top-right corner, so it follows the panel at any aspect ratio.
        // Both live under full-screen stretch containers of LAYOUT, so the anchors mean the same thing.
        rt.anchorMin = panel.anchorMax;
        rt.anchorMax = panel.anchorMax;
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(0f, GapAbovePanel);
        rt.sizeDelta = Size;

        Image background = rt.gameObject.AddComponent<Image>();
        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        rt.gameObject.AddComponent<InventoryCloseButton>();

        RectTransform labelRt = InventoryRedesign.CreateUIChild("Label", rt);
        InventoryRedesign.Stretch(labelRt);

        TextMeshProUGUI label = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = FontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        label.text = LabelText;
    }
}
#endif
