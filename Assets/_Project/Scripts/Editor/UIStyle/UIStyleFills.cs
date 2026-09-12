#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fills step: drops the sprite of each image the profile lists, so the theme colour fills its rect
/// flat. The old chrome — rounded knobs, gradients, border sprites — is what the Win95 frame
/// replaces; painted with a dark token it would only turn into a dark smudge of its old shape.
///
/// Only chrome goes in the list. An icon keeps its sprite, or it would become a square.
/// </summary>
public static class UIStyleFills
{
    public static void Apply(UIStyleContext ctx)
    {
        if (ctx.Profile.flatFills.Count == 0) return;

        int flattened = 0;
        foreach (string path in ctx.Profile.flatFills)
        {
            Image image = ctx.Find(path).GetComponent<Image>();
            if (image == null)
            {
                ctx.Report.AppendLine($"  SKIPPED flat fill '{path}' — no Image.");
                continue;
            }

            if (image.sprite == null && image.type == Image.Type.Simple) continue;

            image.sprite = null;
            image.type = Image.Type.Simple;
            flattened++;
        }

        ctx.Report.AppendLine($"  flat fills: {flattened} flattened");
    }
}
#endif
