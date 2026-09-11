#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gives the inventory's window backgrounds the animated surface material (AnimatedSurface.shader):
/// the theme colour stays, with a faint drifting grid, slow noise and a sweep over it.
///
/// Windows only, not the wells that hold text (the item list, the doc text): a pattern moving
/// under reading text costs more legibility than it adds life. The detail area has no background of
/// its own, so the main panel shows through it and it gets the motion anyway.
///
/// USAGE: Tools / UI / Inventory / Animate Backgrounds. See <see cref="InventoryRedesign"/>.
/// </summary>
public static class InventorySurfaceSetup
{
    public const string MaterialPath = "Assets/_Project/Art/Materials/UI/InventorySurface.mat";

    private static readonly string[] Nodes =
    {
        "LAYOUT/DecorationObjects/Inventario Fondo",
        "LAYOUT/DecorationObjects/Avance Modulos fondo",
        "LAYOUT/Doc Box",
        "LAYOUT/DiscardDialogView",
    };

    [MenuItem("Tools/UI/Inventory/Animate Backgrounds", priority = 15)]
    public static void Apply()
    {
        Material material = InventoryRedesign.LoadRequired<Material>(MaterialPath);
        if (material == null) return;

        StringBuilder report = new StringBuilder();
        int applied = 0;

        InventoryRedesign.EditPrefab(InventoryRedesign.CanvasPrefab, root =>
        {
            foreach (string path in Nodes)
            {
                Transform node = InventoryRedesign.Find(root, path, report);
                if (node == null) continue;

                Image image = node.GetComponent<Image>();
                if (image == null)
                {
                    report.AppendLine($"  SKIPPED '{path}' — no Image.");
                    continue;
                }

                image.material = material;
                applied++;
            }
        });

        Debug.Log($"[InventorySurfaceSetup] {applied} background(s) animated.\n{report}");
    }
}
#endif
