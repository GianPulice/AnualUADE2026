#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Style = UIBevelFrame.BevelStyle;

/// <summary>
/// Frames step: a <see cref="UIBevelFrame"/> on every node the profile lists. The Win95 rule the
/// profiles follow: windows and buttons are Raised, anything that holds content — a list, a text
/// area, a slider track, an icon slot — is Sunken. Pressable buttons also get
/// <see cref="UIBevelPressFeedback"/> so they sink while held.
///
/// Each frame is a child named "BevelFrame", stretched, last sibling, ignored by layout groups and by
/// raycasts. Re-running finds the existing child and only reconfigures it.
///
/// Sunken wells also get their layout padding raised to the profile's minWellPadding: the frame
/// covers the well's edge, and a label laid out flush against it loses its first character.
/// </summary>
public static class UIStyleFrames
{
    public static void Apply(UIStyleContext ctx)
    {
        int framed = 0;

        foreach (SO_UIStyleProfile.FrameEntry entry in ctx.Profile.frames)
        {
            Transform node = ctx.Find(entry.path);

            UIBevelFrame frame = EnsureFrame(node, ctx.Theme, entry.style);
            if (entry.pressable) EnsurePress(node, frame, entry.path, ctx.Report);
            if (entry.style == Style.Sunken) PadWell(node, ctx.Profile.minWellPadding, entry.path, ctx.Report);
            framed++;
        }

        ctx.Report.AppendLine($"  frames: {framed}");
    }

    private static UIBevelFrame EnsureFrame(Transform node, SO_UIThemeConfig theme, Style style)
    {
        Transform existing = node.Find(UIStyleTools.FrameName);
        RectTransform rt = existing != null
            ? (RectTransform)existing
            : UIStyleTools.CreateUIChild(UIStyleTools.FrameName, node);

        UIStyleTools.Stretch(rt);
        // Last, so it draws over the node's other children — a sweep bar, an icon — not under them.
        rt.SetAsLastSibling();

        // Explicit, not left to [RequireComponent]: that only acts when the Graphic is added, and the
        // first inventory pass saved frames before UIBevelFrame declared it.
        UIStyleTools.GetOrAdd<CanvasRenderer>(rt.gameObject);

        UIBevelFrame frame = UIStyleTools.GetOrAdd<UIBevelFrame>(rt.gameObject);
        frame.raycastTarget = false;

        SerializedObject serialized = new SerializedObject(frame);
        serialized.FindProperty("theme").objectReferenceValue = theme;
        serialized.FindProperty("style").enumValueIndex = (int)style;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // Some framed nodes lay out their children; the frame must not take a slot in that layout.
        UIStyleTools.GetOrAdd<LayoutElement>(rt.gameObject).ignoreLayout = true;

        return frame;
    }

    private static void EnsurePress(Transform node, UIBevelFrame frame, string path, StringBuilder report)
    {
        if (node.GetComponent<Button>() == null)
        {
            report.AppendLine($"  NO PRESS on '{path}' — it has no Button.");
            return;
        }

        UIBevelPressFeedback press = UIStyleTools.GetOrAdd<UIBevelPressFeedback>(node.gameObject);

        SerializedObject serialized = new SerializedObject(press);
        serialized.FindProperty("frame").objectReferenceValue = frame;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void PadWell(Transform node, int min, string path, StringBuilder report)
    {
        if (min <= 0) return;

        LayoutGroup group = node.GetComponent<LayoutGroup>();
        ScrollRect scroll = node.GetComponent<ScrollRect>();
        if (group == null && scroll != null && scroll.content != null) group = scroll.content.GetComponent<LayoutGroup>();
        if (group == null) return;

        RectOffset p = group.padding;
        if (p.left >= min && p.right >= min && p.top >= min && p.bottom >= min) return;

        string before = $"{p.left}/{p.right}/{p.top}/{p.bottom}";
        group.padding = new RectOffset(Mathf.Max(p.left, min), Mathf.Max(p.right, min),
                                       Mathf.Max(p.top, min), Mathf.Max(p.bottom, min));
        report.AppendLine($"  PADDED '{path}' layout (l/r/t/b) {before} → {group.padding.left}/{group.padding.right}/{group.padding.top}/{group.padding.bottom}");
    }
}
#endif
