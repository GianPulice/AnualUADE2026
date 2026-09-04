#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attaches and configures <see cref="UIThemeApplier"/> across the inventory panel in one pass.
///
/// It exists because that repaint is about forty nodes deep in a prefab, and doing it by hand is
/// both the longest part of the redesign and the easiest to get subtly wrong — one node left on its
/// old literal colour is invisible until someone changes a token and that node does not move with
/// the rest. The mapping below IS the design decision; running the tool is just typing.
///
/// <b>It only ever adds and configures. It never deletes.</b> Nodes that should go — the SweepBars —
/// are reported instead, because removing objects from a prefab by script is exactly the operation
/// worth doing by hand where you can see what you are losing.
///
/// Everything goes through Undo, so a bad run is one Ctrl+Z away.
///
/// USAGE: select the Inventory Canvas root (in Prefab Mode, or its instance in the scene) and run
/// Tools / UI / Apply Theme To Inventory.
/// </summary>
public static class InventoryThemeSetup
{
    private const string ThemePath = "Assets/_Project/ScriptableObjects/UI/UITheme.asset";

    /// <summary>
    /// Node name → role. Names come from the prefab as authored; anything not listed is left alone,
    /// which is what keeps this safe to re-run after the panel grows.
    ///
    /// The category colour is deliberately absent: it used to be carried by "iTEM IMAGE BG" and
    /// "item type" as coloured fills, and it is now carried by the [CMP] / [KEY] tag in the row
    /// text. Those two nodes become plain raised surfaces.
    /// </summary>
    private static readonly Dictionary<string, UIThemeRole> RoleByName = new Dictionary<string, UIThemeRole>
    {
        { "Background Panel",       UIThemeRole.Dim },
        { "Inventario Fondo",       UIThemeRole.SurfacePanel },
        { "Avance Modulos fondo",   UIThemeRole.SurfacePanel },

        { "Inventario Division",     UIThemeRole.Divider },
        { "Inventario Division (1)", UIThemeRole.Divider },
        { "Inventario Division (2)", UIThemeRole.Divider },
        { "Inventario Division (3)", UIThemeRole.Divider },
        { "Inventario Division (4)", UIThemeRole.Divider },

        { "iTEM IMAGE BG",          UIThemeRole.SurfaceRaised },
        { "item type",              UIThemeRole.SurfaceRaised },

        { "Discard Item Button",    UIThemeRole.AccentBgDeep },
        { "DiscardText",            UIThemeRole.Accent },

        { "Empty State Panel",      UIThemeRole.SurfacePanel },
        { "Doc Box",                UIThemeRole.SurfaceRaised },
    };

    [MenuItem("Tools/UI/Apply Theme To Inventory")]
    private static void Apply()
    {
        GameObject root = Selection.activeGameObject;
        if (root == null)
        {
            Debug.LogWarning("[InventoryThemeSetup] Select the Inventory Canvas root first — in " +
                             "Prefab Mode, or its instance in the scene.");
            return;
        }

        SO_UIThemeConfig theme = AssetDatabase.LoadAssetAtPath<SO_UIThemeConfig>(ThemePath);
        if (theme == null)
        {
            Debug.LogError($"[InventoryThemeSetup] No SO_UIThemeConfig at {ThemePath}. Fix the " +
                           "path in this script, or create the asset.");
            return;
        }

        StringBuilder report = new StringBuilder();
        int applied = 0;
        int skipped = 0;

        foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
        {
            if (!RoleByName.TryGetValue(node.name, out UIThemeRole role)) continue;

            // A role is meaningless without something to paint. Reported rather than skipped
            // silently: a node in the table with no Graphic means the table has drifted from the
            // prefab, which is worth knowing.
            if (node.GetComponent<Graphic>() == null)
            {
                report.AppendLine($"  SKIPPED '{node.name}' — no Graphic to paint.");
                skipped++;
                continue;
            }

            ApplyTo(node.gameObject, theme, role);
            report.AppendLine($"  {node.name} → {role}");
            applied++;
        }

        int sweeps = ReportSweepBars(root, report);

        Debug.Log($"[InventoryThemeSetup] {applied} node(s) themed, {skipped} skipped, " +
                  $"{sweeps} SweepBar(s) found.\n\n{report}\n" +
                  (sweeps > 0
                      ? "Delete the SweepBars by hand: in an application UI the hover is a change of "
                        + "border, not a sweep across the row. Their ButtonHoverSweepEffect goes with them."
                      : ""));
    }

    private static void ApplyTo(GameObject go, SO_UIThemeConfig theme, UIThemeRole role)
    {
        UIThemeApplier applier = go.GetComponent<UIThemeApplier>();
        if (applier == null) applier = Undo.AddComponent<UIThemeApplier>(go);

        Undo.RecordObject(applier, "Apply UI Theme");

        // Through SerializedObject and not public setters: both fields are private, and this is the
        // supported way to write them without widening the component's API for a one-off tool.
        SerializedObject serialized = new SerializedObject(applier);
        serialized.FindProperty("theme").objectReferenceValue = theme;
        serialized.FindProperty("role").enumValueIndex = (int)role;
        serialized.ApplyModifiedProperties();

        EditorUtility.SetDirty(applier);
    }

    /// <summary>Finds the sweep bars so they can be removed by hand, and says why they should be.</summary>
    private static int ReportSweepBars(GameObject root, StringBuilder report)
    {
        int found = 0;

        foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
        {
            if (node.name != "SweepBar") continue;

            report.AppendLine($"  TO DELETE: {Path(node)}");
            found++;
        }

        return found;
    }

    private static string Path(Transform transform)
    {
        string path = transform.name;

        for (Transform parent = transform.parent; parent != null; parent = parent.parent)
            path = $"{parent.name}/{path}";

        return path;
    }
}
#endif
