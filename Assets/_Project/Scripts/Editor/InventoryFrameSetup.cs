#if UNITY_EDITOR
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Style = UIBevelFrame.BevelStyle;

/// <summary>
/// Puts a <see cref="UIBevelFrame"/> on every panel, well, button and icon slot of the inventory.
///
/// The rule the table follows is the Win95 one: windows and buttons are Raised, anything that holds
/// content — the item list, the detail area, the icon and tag chips, the doc text — is Sunken.
/// Buttons also get <see cref="UIBevelPressFeedback"/> so they sink while held.
///
/// Left out on purpose: the circular timer and failure pips (a square frame on a circle reads as a
/// mistake), and the module progress bar, whose fill is a later sibling and would paint over it.
///
/// Each frame is a child named "BevelFrame", stretched, last sibling, ignored by layout groups and
/// by raycasts. Re-running finds the existing child and only reconfigures it.
///
/// USAGE: Tools / UI / Inventory / Add Bevel Frames. See <see cref="InventoryRedesign"/>.
/// </summary>
public static class InventoryFrameSetup
{
    private const string FrameName = "BevelFrame";
    private const string Detail = "LAYOUT/InventoryObjects/Item Selection";

    private static readonly (string prefab, string node, Style style, bool pressable)[] Table =
    {
        // -- Windows --
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DecorationObjects/Inventario Fondo",     Style.Raised, false),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DecorationObjects/Avance Modulos fondo", Style.Raised, false),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box",                                       Style.Raised, false),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DiscardDialogView",                             Style.Raised, false),

        // -- Wells --
        (InventoryRedesign.CanvasPrefab, "LAYOUT/InventoryObjects/InventoryScroll", Style.Sunken, false),
        (InventoryRedesign.CanvasPrefab, Detail,                                    Style.Sunken, false),
        (InventoryRedesign.CanvasPrefab, Detail + "/iTEM IMAGE BG",                 Style.Sunken, false),
        (InventoryRedesign.CanvasPrefab, Detail + "/Item Type Color Box",           Style.Sunken, false),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box/Scroll View",                     Style.Sunken, false),
        (InventoryRedesign.SlotPrefab,   "Background Image",                        Style.Sunken, false),

        // -- Buttons --
        (InventoryRedesign.CanvasPrefab, Detail + "/Discard Item Button",                                       Style.Raised, true),
        (InventoryRedesign.CanvasPrefab, Detail + "/OpenDocButton",                                             Style.Raised, true),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/InventoryObjects/" + InventoryCloseButtonSetup.ButtonName,     Style.Raised, true),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box/Header/CloseDocButton",                                       Style.Raised, true),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DiscardDialogView/ButtonConfirm",                                     Style.Raised, true),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DiscardDialogView/ButtonCancel",                                      Style.Raised, true),

        // -- Scrollbar handles --
        (InventoryRedesign.CanvasPrefab, "LAYOUT/InventoryObjects/InventoryScroll/Scrollbar Vertical/Sliding Area/Handle", Style.Raised, false),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box/Scroll View/Scrollbar Vertical/Sliding Area/Handle",                     Style.Raised, false),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box/Scroll View/Scrollbar Horizontal/Sliding Area/Handle",                   Style.Raised, false),
    };

    [MenuItem("Tools/UI/Inventory/Add Bevel Frames", priority = 12)]
    public static void Apply()
    {
        SO_UIThemeConfig theme = InventoryRedesign.LoadRequired<SO_UIThemeConfig>(InventoryRedesign.ThemePath);
        if (theme == null) return;

        StringBuilder report = new StringBuilder();
        int framed = 0;

        foreach (IGrouping<string, (string prefab, string node, Style style, bool pressable)> group in Table.GroupBy(e => e.prefab))
        {
            report.AppendLine(group.Key);

            InventoryRedesign.EditPrefab(group.Key, root =>
            {
                foreach ((string _, string path, Style style, bool pressable) in group)
                {
                    Transform node = InventoryRedesign.Find(root, path, report);
                    if (node == null) continue;

                    UIBevelFrame frame = EnsureFrame(node, theme, style);
                    if (pressable) EnsurePress(node, frame, path, report);
                    framed++;
                }
            });
        }

        Debug.Log($"[InventoryFrameSetup] {framed} node(s) framed.\n\n{report}");
    }

    private static UIBevelFrame EnsureFrame(Transform node, SO_UIThemeConfig theme, Style style)
    {
        Transform existing = node.Find(FrameName);
        RectTransform rt = existing != null
            ? (RectTransform)existing
            : InventoryRedesign.CreateUIChild(FrameName, node);

        InventoryRedesign.Stretch(rt);
        // Last, so it draws over the node's other children — a sweep bar, an icon — not under them.
        rt.SetAsLastSibling();

        // Explicit, not left to [RequireComponent]: that only acts when the Graphic is added, and the
        // first run of this tool saved frames before UIBevelFrame declared it.
        if (rt.GetComponent<CanvasRenderer>() == null) rt.gameObject.AddComponent<CanvasRenderer>();

        UIBevelFrame frame = rt.GetComponent<UIBevelFrame>();
        if (frame == null) frame = rt.gameObject.AddComponent<UIBevelFrame>();
        frame.raycastTarget = false;

        SerializedObject serialized = new SerializedObject(frame);
        serialized.FindProperty("theme").objectReferenceValue = theme;
        serialized.FindProperty("style").enumValueIndex = (int)style;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // Some framed nodes lay out their children; the frame must not take a slot in that layout.
        LayoutElement layout = rt.GetComponent<LayoutElement>();
        if (layout == null) layout = rt.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        return frame;
    }

    private static void EnsurePress(Transform node, UIBevelFrame frame, string path, StringBuilder report)
    {
        if (node.GetComponent<Button>() == null)
        {
            report.AppendLine($"  NO PRESS on '{path}' — it has no Button.");
            return;
        }

        UIBevelPressFeedback press = node.GetComponent<UIBevelPressFeedback>();
        if (press == null) press = node.gameObject.AddComponent<UIBevelPressFeedback>();

        SerializedObject serialized = new SerializedObject(press);
        serialized.FindProperty("frame").objectReferenceValue = frame;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
#endif
