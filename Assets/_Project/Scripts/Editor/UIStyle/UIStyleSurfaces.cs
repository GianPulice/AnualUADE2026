#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Surfaces step: the animated surface material (AnimatedSurface.shader) on window backgrounds — the
/// theme colour stays, with a faint drifting grid, slow noise and a sweep over it.
///
/// Windows only, not the wells that hold text: a pattern moving under reading text costs more
/// legibility than it adds life. Which nodes are windows is the profile's call.
/// </summary>
public static class UIStyleSurfaces
{
    public static void Apply(UIStyleContext ctx)
    {
        if (ctx.Profile.animatedSurfaces.Count == 0) return;

        Material material = ctx.Profile.surfaceMaterial;
        if (material == null)
        {
            ctx.Report.AppendLine("  SKIPPED surfaces — the profile has no surface material.");
            return;
        }

        int applied = 0;
        foreach (string path in ctx.Profile.animatedSurfaces)
        {
            Image image = ctx.Find(path).GetComponent<Image>();
            if (image == null)
            {
                ctx.Report.AppendLine($"  SKIPPED surface '{path}' — no Image.");
                continue;
            }

            image.material = material;
            applied++;
        }

        ctx.Report.AppendLine($"  surfaces: {applied} animated");
    }
}
#endif
