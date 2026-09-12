#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Theme step: a <see cref="UIThemeApplier"/> on every node the profile lists, so no node keeps a
/// literal colour. One node left on its old colour is invisible until someone changes a token and
/// that node does not move with the rest.
///
/// Some of these colours are overwritten at runtime and are themed anyway, so the prefab looks right
/// in the editor (the inventory's category chips, for one).
/// </summary>
public static class UIStyleTheme
{
    public static void Apply(UIStyleContext ctx)
    {
        int applied = 0;

        foreach (SO_UIStyleProfile.ThemeEntry entry in ctx.Profile.theme)
        {
            Transform node = ctx.Find(entry.path);
            Graphic graphic = node.GetComponent<Graphic>();
            if (graphic == null)
            {
                ctx.Report.AppendLine($"  SKIPPED theme '{entry.path}' — no Graphic to paint.");
                continue;
            }

            Paint(graphic, ctx.Theme, entry.role, entry.preserveAlpha);
            applied++;
        }

        ctx.Report.AppendLine($"  theme: {applied} node(s)");
    }

    public static void Paint(Graphic graphic, SO_UIThemeConfig theme, UIThemeRole role, bool preserveAlpha)
    {
        UIThemeApplier applier = UIStyleTools.GetOrAdd<UIThemeApplier>(graphic.gameObject);

        // Through SerializedObject and not public setters: the fields are private, and this is the
        // supported way to write them without widening the component's API for an editor tool.
        SerializedObject serialized = new SerializedObject(applier);
        serialized.FindProperty("theme").objectReferenceValue = theme;
        serialized.FindProperty("role").enumValueIndex = (int)role;
        serialized.FindProperty("preserveAlpha").boolValue = preserveAlpha;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // Written here as well, not left to the applier's OnEnable: ButtonHoverColorSwap captures a
        // label's colour in Awake, before the label's applier runs, so the SERIALIZED colour has to
        // already be the token or the hover would swap back to the old one.
        Color color = theme.Get(role);
        if (preserveAlpha) color.a = graphic.color.a;
        graphic.color = color;
    }
}
#endif
