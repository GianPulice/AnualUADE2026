#if UNITY_EDITOR
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Style = UIBevelFrame.BevelStyle;

/// <summary>
/// Small, idempotent building blocks for the one-shot UI builders (<see cref="ModuleTimerHUDBuilder"/>;
/// the skill check canvas was built with it too, and its builder already deleted): find-or-create a
/// node, anchor it to a point, paint it with a theme role, give it the project's fonts and a Win95
/// frame, and wire a serialized field.
///
/// Everything here finds its own earlier work before creating anything, so running a builder twice
/// changes nothing. The look is the same the style profiles produce (see <see cref="UIStyleTools"/>):
/// theme roles through <see cref="UIThemeApplier"/>, ShareTechMono / Oswald with the outline presets,
/// and a <see cref="UIBevelFrame"/> child named like the one the frames step makes.
///
/// Goes away with the builders once their prefabs are committed.
/// </summary>
public static class UIBuildKit
{
    public const string BodyPresetPath  = "Assets/_Project/Art/Fonts/Share_Tech_Mono/ShareTechMono-Regular SDF - Outline.mat";
    public const string TitlePresetPath = "Assets/_Project/Art/Fonts/Oswald/static/Oswald-Regular SDF - Outline.mat";

    // Title bar caps, same numbers as the interaction prompt's window.
    private const float CapWidth       = 22f;
    private const float CapHeight      = 18f;
    private const float CapY           = -2f;
    private const float GlyphThickness = 2f;
    private const float MinusLength    = 10f;
    private const float CrossLength    = 12f;

    // -- Nodes -------------------

    public static RectTransform Ensure(string name, Transform parent)
    {
        Transform existing = parent.Find(name);
        return existing != null ? (RectTransform)existing : UIStyleTools.CreateUIChild(name, parent);
    }

    /// <summary>Fixed size, anchored to one point of the parent — the only anchoring that holds at
    /// every resolution for a fixed-size widget (UI-System §7.5).</summary>
    public static void Point(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    /// <summary>Stretched over the parent with fixed insets in canvas units.</summary>
    public static void Inset(RectTransform rt, float left, float bottom, float right, float top)
    {
        UIStyleTools.Stretch(rt);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>A bar pinned to the top edge, full width.</summary>
    public static void Top(RectTransform rt, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    // -- Graphics -------------------

    /// <summary>A flat Image painted by a theme role (and kept in the theme by its applier).</summary>
    public static Image Fill(RectTransform rt, SO_UIThemeConfig theme, UIThemeRole role)
    {
        Image image = Plain(rt, theme.Get(role));
        UIStyleTheme.Paint(image, theme, role, false);
        return image;
    }

    /// <summary>A flat Image with no theme applier: its colour is driven at runtime by a view, and an
    /// applier would repaint it on every enable.</summary>
    public static Image Plain(RectTransform rt, Color color)
    {
        Image image = UIStyleTools.GetOrAdd<Image>(rt.gameObject);
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = color;
        // HUD and overlays: nothing in them should ever eat a click meant for the UI underneath.
        image.raycastTarget = false;
        return image;
    }

    /// <summary>
    /// A TMP label in the project's faces. <paramref name="role"/> null = no applier, for labels a
    /// view recolours at runtime.
    /// </summary>
    public static TextMeshProUGUI Label(RectTransform rt, string text, float size, TextAlignmentOptions alignment,
                                        SO_UIThemeConfig theme, UIThemeRole? role, bool title = false)
    {
        TextMeshProUGUI label = UIStyleTools.GetOrAdd<TextMeshProUGUI>(rt.gameObject);

        TMP_FontAsset font = UIStyleTools.LoadRequired<TMP_FontAsset>(title ? UIStyleText.TitleFontPath : UIStyleText.BodyFontPath);
        Material preset = AssetDatabase.LoadAssetAtPath<Material>(title ? TitlePresetPath : BodyPresetPath);
        // Font before material: assigning a font resets the material to the font's own.
        if (font != null) label.font = font;
        if (preset != null) label.fontSharedMaterial = preset;

        label.text = text;
        label.fontSize = size;
        label.alignment = alignment;
        label.enableAutoSizing = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;

        if (role.HasValue) UIStyleTheme.Paint(label, theme, role.Value, false);
        else label.color = theme.TextPrimary;
        return label;
    }

    /// <summary>The Win95 frame: a stretched <see cref="UIBevelFrame"/> child drawn over the node's
    /// other children, exactly as the style profiles' frames step builds it.</summary>
    public static UIBevelFrame Frame(Transform node, SO_UIThemeConfig theme, Style style)
    {
        RectTransform rt = Ensure(UIStyleTools.FrameName, node);
        UIStyleTools.Stretch(rt);
        rt.SetAsLastSibling();

        UIStyleTools.GetOrAdd<CanvasRenderer>(rt.gameObject);
        UIBevelFrame frame = UIStyleTools.GetOrAdd<UIBevelFrame>(rt.gameObject);
        frame.raycastTarget = false;

        SerializedObject serialized = new SerializedObject(frame);
        serialized.FindProperty("theme").objectReferenceValue = theme;
        serialized.FindProperty("style").enumValueIndex = (int)style;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        UIStyleTools.GetOrAdd<LayoutElement>(rt.gameObject).ignoreLayout = true;
        return frame;
    }

    /// <summary>A procedural ring. Colour is left to the caller (a role, or a view at runtime).</summary>
    public static UIRingArc Ring(RectTransform rt, float thickness, int blockCount, float blockGap,
                                 float startAngle = 0f, float sweep = 360f)
    {
        UIStyleTools.GetOrAdd<CanvasRenderer>(rt.gameObject);
        UIRingArc ring = UIStyleTools.GetOrAdd<UIRingArc>(rt.gameObject);
        ring.raycastTarget = false;

        SerializedObject serialized = new SerializedObject(ring);
        serialized.FindProperty("startAngle").floatValue = startAngle;
        serialized.FindProperty("sweep").floatValue = sweep;
        serialized.FindProperty("thickness").floatValue = thickness;
        serialized.FindProperty("segmentsPerCircle").intValue = 128;
        serialized.FindProperty("blockCount").intValue = blockCount;
        serialized.FindProperty("blockGapDegrees").floatValue = blockGap;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return ring;
    }

    /// <summary>
    /// Title bar with the window's name on the left and the two decorative caps (minimise, close) on
    /// the right. The caps are not Buttons — it is a HUD window, nothing in it is clickable.
    /// </summary>
    public static RectTransform TitleBar(RectTransform window, SO_UIThemeConfig theme, string title,
                                         float height, float fontSize)
    {
        RectTransform bar = Ensure("TitleBar", window);
        Top(bar, height);
        Fill(bar, theme, UIThemeRole.SurfaceFooter);

        RectTransform text = Ensure("TitleText", bar);
        Inset(text, 8f, 0f, 60f, 0f);
        Label(text, title, fontSize, TextAlignmentOptions.MidlineLeft, theme, UIThemeRole.TextSecondary);

        RectTransform min   = TitleCap("TitleButtonMin", bar, -30f, theme);
        RectTransform close = TitleCap("TitleButtonClose", bar, -6f, theme);

        // Bars rather than typed glyphs: at this size the mono font's "-" and "X" come out a hairline
        // thin (same reasoning as the interaction prompt).
        GlyphBar("Glyph", min, MinusLength, 0f, theme);
        GlyphBar("GlyphA", close, CrossLength, 45f, theme);
        GlyphBar("GlyphB", close, CrossLength, -45f, theme);
        return bar;
    }

    private static RectTransform TitleCap(string name, Transform bar, float x, SO_UIThemeConfig theme)
    {
        RectTransform rt = Ensure(name, bar);
        Point(rt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(x, CapY), new Vector2(CapWidth, CapHeight));
        Fill(rt, theme, UIThemeRole.SurfaceRaised);
        Frame(rt, theme, Style.Raised);
        return rt;
    }

    private static void GlyphBar(string name, RectTransform cap, float length, float angle, SO_UIThemeConfig theme)
    {
        RectTransform rt = Ensure(name, cap);
        Point(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(length, GlyphThickness));
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);
        Fill(rt, theme, UIThemeRole.TextPrimary);

        // The cap's frame has to stay the last sibling.
        Transform frame = cap.Find(UIStyleTools.FrameName);
        if (frame != null) frame.SetAsLastSibling();
    }

    // -- Wiring -------------------

    public static void Wire(SerializedObject serialized, string field, Object value, StringBuilder report)
    {
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            report.AppendLine($"  NO FIELD '{field}' on {serialized.targetObject.GetType().Name} — left unwired.");
            return;
        }
        property.objectReferenceValue = value;
    }

    public static void WireArray(SerializedObject serialized, string field, Object[] values, StringBuilder report)
    {
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null || !property.isArray)
        {
            report.AppendLine($"  NO ARRAY '{field}' on {serialized.targetObject.GetType().Name} — left unwired.");
            return;
        }

        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    public static T LoadAsset<T>(string path, StringBuilder report) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) report.AppendLine($"  MISSING {typeof(T).Name} at '{path}'.");
        return asset;
    }
}
#endif
