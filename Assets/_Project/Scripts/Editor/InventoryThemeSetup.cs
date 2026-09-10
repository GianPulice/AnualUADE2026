#if UNITY_EDITOR
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attaches and configures <see cref="UIThemeApplier"/> across the inventory prefabs in one pass,
/// so no node keeps a literal colour. One node left on its old colour is invisible until someone
/// changes a token and that node does not move with the rest — which is why this is a table and not
/// forty trips through the Inspector. The table IS the design decision; running it is just typing.
///
/// Nodes are addressed by path from the prefab root, not by name: "Text (TMP)" and "DiscardText"
/// each appear more than once, with different roles.
///
/// Some of these colours are overwritten at runtime and are themed anyway, so the prefab looks right
/// in the editor: ItemDetailView repaints the icon and tag chips per category, ItemSlotView the row
/// icon background. The category tag TEXT is left out entirely — only the runtime colour means
/// anything there.
///
/// USAGE: Tools / UI / Inventory / Apply Theme. See <see cref="InventoryRedesign"/> for how the
/// prefabs are edited.
/// </summary>
public static class InventoryThemeSetup
{
    private const string Detail = "LAYOUT/InventoryObjects/Item Selection";
    private const string Modules = "LAYOUT/ModuleList";

    private static readonly (string prefab, string node, UIThemeRole role)[] Table =
    {
        // -- Inventory Canvas: surfaces --
        (InventoryRedesign.CanvasPrefab, "LAYOUT/BG/Background Panel",                  UIThemeRole.Dim),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DecorationObjects/Inventario Fondo",     UIThemeRole.SurfacePanel),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DecorationObjects/Avance Modulos fondo", UIThemeRole.SurfacePanel),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DecorationObjects/Divisiones Inventario/Inventario Division",     UIThemeRole.Divider),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DecorationObjects/Divisiones Inventario/Inventario Division (1)", UIThemeRole.Divider),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DecorationObjects/Divisiones Inventario/Inventario Division (2)", UIThemeRole.Divider),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DecorationObjects/Divisiones Inventario/Inventario Division (3)", UIThemeRole.Divider),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DecorationObjects/Divisiones Inventario/Inventario Division (4)", UIThemeRole.Divider),

        // -- Inventory Canvas: list --
        (InventoryRedesign.CanvasPrefab, "LAYOUT/InventoryObjects/ItemsText",       UIThemeRole.TextPrimary),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/InventoryObjects/InventoryScroll", UIThemeRole.SurfaceScreen),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/InventoryObjects/InventoryScroll/Scrollbar Vertical",                    UIThemeRole.SurfaceRaised),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/InventoryObjects/InventoryScroll/Scrollbar Vertical/Sliding Area/Handle", UIThemeRole.BorderStrong),

        // -- Inventory Canvas: detail --
        (InventoryRedesign.CanvasPrefab, Detail + "/Item Type Color Box",               UIThemeRole.SurfaceRaised),
        (InventoryRedesign.CanvasPrefab, Detail + "/iTEM IMAGE BG",                     UIThemeRole.SurfaceRaised),
        (InventoryRedesign.CanvasPrefab, Detail + "/Item Name",                         UIThemeRole.TextPrimary),
        (InventoryRedesign.CanvasPrefab, Detail + "/Item Description",                  UIThemeRole.TextSecondary),
        (InventoryRedesign.CanvasPrefab, Detail + "/Discard Item Button",               UIThemeRole.AccentBgDeep),
        (InventoryRedesign.CanvasPrefab, Detail + "/Discard Item Button/DiscardText",   UIThemeRole.Accent),
        (InventoryRedesign.CanvasPrefab, Detail + "/OpenDocButton",                     UIThemeRole.SurfaceRaised),
        (InventoryRedesign.CanvasPrefab, Detail + "/OpenDocButton/Text (TMP)",          UIThemeRole.TextPrimary),
        (InventoryRedesign.CanvasPrefab, Detail + "/Empty State Panel",                 UIThemeRole.SurfacePanel),
        (InventoryRedesign.CanvasPrefab, Detail + "/Empty State Panel/EmptyStateText",  UIThemeRole.TextMuted),

        // -- Inventory Canvas: close button (added by InventoryCloseButtonSetup) --
        (InventoryRedesign.CanvasPrefab, "LAYOUT/InventoryObjects/" + InventoryCloseButtonSetup.ButtonName,            UIThemeRole.SurfaceRaised),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/InventoryObjects/" + InventoryCloseButtonSetup.ButtonName + "/Label", UIThemeRole.TextPrimary),

        // -- Inventory Canvas: module bar (static labels only; the timer is coloured by its view) --
        (InventoryRedesign.CanvasPrefab, Modules + "/Module Id",                               UIThemeRole.TextPrimary),
        (InventoryRedesign.CanvasPrefab, Modules + "/ModuleTimerSection/Time Left Text",       UIThemeRole.TextMuted),
        (InventoryRedesign.CanvasPrefab, Modules + "/ModuleTimerSection/Active Module Name",   UIThemeRole.TextSecondary),
        (InventoryRedesign.CanvasPrefab, Modules + "/Failures Section/Failures LeftText",      UIThemeRole.TextMuted),
        (InventoryRedesign.CanvasPrefab, Modules + "/Failures Section/FailureCount",           UIThemeRole.TextPrimary),
        (InventoryRedesign.CanvasPrefab, Modules + "/Image",                                   UIThemeRole.Divider),

        // -- Inventory Canvas: doc pop-up --
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box",                                  UIThemeRole.SurfaceRaised),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box/Header",                           UIThemeRole.SurfaceFooter),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box/Header/DocTitle",                  UIThemeRole.TextPrimary),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box/Header/CloseDocButton",            UIThemeRole.AccentBgDeep),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box/Header/CloseDocButton/CloseLabel", UIThemeRole.Accent),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box/Scroll View",                      UIThemeRole.SurfaceScreen),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box/Scroll View/Viewport/Content/DocText",                        UIThemeRole.TextPrimary),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box/Scroll View/Scrollbar Horizontal",                           UIThemeRole.SurfaceRaised),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box/Scroll View/Scrollbar Horizontal/Sliding Area/Handle",       UIThemeRole.BorderStrong),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box/Scroll View/Scrollbar Vertical",                             UIThemeRole.SurfaceRaised),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/Doc Box/Scroll View/Scrollbar Vertical/Sliding Area/Handle",         UIThemeRole.BorderStrong),

        // -- Inventory Canvas: discard dialog --
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DiscardDialogView",                         UIThemeRole.SurfacePanel),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DiscardDialogView/DiscardText",             UIThemeRole.TextPrimary),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DiscardDialogView/wARNINGText",             UIThemeRole.Accent),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DiscardDialogView/ButtonConfirm",           UIThemeRole.AccentBgDeep),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DiscardDialogView/ButtonConfirm/Text (TMP)", UIThemeRole.Accent),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DiscardDialogView/ButtonCancel",            UIThemeRole.SurfaceRaised),
        (InventoryRedesign.CanvasPrefab, "LAYOUT/DiscardDialogView/ButtonCancel/Text (TMP)", UIThemeRole.TextPrimary),

        // -- Runtime-spawned rows --
        (InventoryRedesign.SlotPrefab,       "Item Name",              UIThemeRole.TextPrimary),
        (InventoryRedesign.GroupLabelPrefab, "",                       UIThemeRole.TextMuted),
        (InventoryRedesign.ParameterPrefab,  "",                       UIThemeRole.TextSecondary),
        (InventoryRedesign.ParameterPrefab,  "Item Parameter Value 1", UIThemeRole.TextPrimary),
        (InventoryRedesign.ModuleRowPrefab,  "Background",             UIThemeRole.SurfaceRaised),
        (InventoryRedesign.ModuleRowPrefab,  "Active Module Name (1)", UIThemeRole.TextSecondary),
        (InventoryRedesign.ModuleRowPrefab,  "Module Status",          UIThemeRole.TextPrimary),
    };

    [MenuItem("Tools/UI/Inventory/Apply Theme", priority = 11)]
    public static void Apply()
    {
        SO_UIThemeConfig theme = InventoryRedesign.LoadRequired<SO_UIThemeConfig>(InventoryRedesign.ThemePath);
        if (theme == null) return;

        StringBuilder report = new StringBuilder();
        int applied = 0;

        foreach (IGrouping<string, (string prefab, string node, UIThemeRole role)> group in Table.GroupBy(e => e.prefab))
        {
            report.AppendLine(group.Key);

            InventoryRedesign.EditPrefab(group.Key, root =>
            {
                foreach ((string _, string path, UIThemeRole role) in group)
                {
                    Transform node = InventoryRedesign.Find(root, path, report);
                    if (node == null) continue;

                    Graphic graphic = node.GetComponent<Graphic>();
                    if (graphic == null)
                    {
                        report.AppendLine($"  SKIPPED '{path}' — no Graphic to paint.");
                        continue;
                    }

                    Paint(graphic, theme, role);
                    applied++;
                }
            });
        }

        Debug.Log($"[InventoryThemeSetup] {applied} node(s) themed.\n\n{report}");
    }

    private static void Paint(Graphic graphic, SO_UIThemeConfig theme, UIThemeRole role)
    {
        UIThemeApplier applier = graphic.GetComponent<UIThemeApplier>();
        if (applier == null) applier = graphic.gameObject.AddComponent<UIThemeApplier>();

        // Through SerializedObject and not public setters: the fields are private, and this is the
        // supported way to write them without widening the component's API for a one-off tool.
        SerializedObject serialized = new SerializedObject(applier);
        serialized.FindProperty("theme").objectReferenceValue = theme;
        serialized.FindProperty("role").enumValueIndex = (int)role;
        serialized.FindProperty("preserveAlpha").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // Written here as well, not left to the applier's OnEnable: ButtonHoverColorSwap captures a
        // label's colour in Awake, before the label's applier runs, so the SERIALIZED colour has to
        // already be the token or the hover would swap back to the old one.
        graphic.color = theme.Get(role);
    }
}
#endif
