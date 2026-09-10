using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Win95-style frame around its own rect: a black outline ring, and inside it a bevel ring lit on
/// one diagonal and shaded on the other. Raised reads as a window or a button, Sunken as a well —
/// a list, an icon slot, a text area.
///
/// It draws only the ring and leaves the centre empty, so it lives on its own child, stretched over
/// the element it frames and kept as the last sibling. It cannot share the element's GameObject
/// anyway (one Graphic per GameObject), and this way the element's own Image stays free for its fill.
///
/// Widths are in canvas units. At the 1920x1080 reference, 2 + 2 keeps every ring at least one real
/// pixel wide down to 960x540; a 1-unit ring would start dropping out below 1080p.
///
/// Colours come from the bevel tokens of <see cref="SO_UIThemeConfig"/>, so frames retune with the
/// rest of the palette. Graphic.color still multiplies them, which is what lets a CanvasGroup or a
/// tint fade a frame together with its element.
/// </summary>
[ExecuteAlways]
// Graphic stopped requiring this itself in uGUI 2 (Unity 6); each concrete Graphic declares it now.
[RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("WIRED/UI/UI Bevel Frame")]
public class UIBevelFrame : MaskableGraphic
{
    public enum BevelStyle { Raised, Sunken, Flat }

    [SerializeField] private SO_UIThemeConfig theme;
    [SerializeField] private BevelStyle style = BevelStyle.Raised;
    [Tooltip("Black ring, outermost. Canvas units.")]
    [Min(0f)] [SerializeField] private float outlineWidth = 2f;
    [Tooltip("Lit/shaded ring inside the outline. Ignored when Style is Flat. Canvas units.")]
    [Min(0f)] [SerializeField] private float bevelWidth = 2f;

    public BevelStyle Style
    {
        get => style;
        set
        {
            if (style == value) return;
            style = value;
            SetVerticesDirty();
        }
    }

    // -- Unity -------------------

    protected override void OnEnable()
    {
        base.OnEnable();
#if UNITY_EDITOR
        SO_UIThemeConfig.OnThemeChanged += SetVerticesDirty;
#endif
    }

    protected override void OnDisable()
    {
#if UNITY_EDITOR
        SO_UIThemeConfig.OnThemeChanged -= SetVerticesDirty;
#endif
        base.OnDisable();
    }

#if UNITY_EDITOR
    protected override void Reset()
    {
        base.Reset();
        // A frame must never swallow the click meant for the element it frames.
        raycastTarget = false;
    }
#endif

    // -- Mesh -------------------

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (theme == null) return;

        Color light = theme.BevelLight;
        Color shadow = theme.BevelShadow;
        if (style == BevelStyle.Sunken) (light, shadow) = (shadow, light);

        Rect outer = GetPixelAdjustedRect();
        float outline = ClampWidth(outlineWidth, outer);
        AddRing(vh, outer, outline, theme.Outline, theme.Outline, theme.Outline, theme.Outline);

        if (style == BevelStyle.Flat) return;

        Rect inner = Shrink(outer, outline);
        AddRing(vh, inner, ClampWidth(bevelWidth, inner), light, shadow, shadow, light);
    }

    /// <summary>
    /// Four trapezoids meeting on the diagonals, so where a lit edge meets a shaded one the corner
    /// splits at 45° — the Win95 corner — instead of one edge overlapping the other.
    /// </summary>
    private void AddRing(VertexHelper vh, Rect outer, float width, Color top, Color right, Color bottom, Color left)
    {
        if (width <= 0f) return;

        Rect inner = Shrink(outer, width);

        Vector2 oTL = new Vector2(outer.xMin, outer.yMax), oTR = new Vector2(outer.xMax, outer.yMax);
        Vector2 oBR = new Vector2(outer.xMax, outer.yMin), oBL = new Vector2(outer.xMin, outer.yMin);
        Vector2 iTL = new Vector2(inner.xMin, inner.yMax), iTR = new Vector2(inner.xMax, inner.yMax);
        Vector2 iBR = new Vector2(inner.xMax, inner.yMin), iBL = new Vector2(inner.xMin, inner.yMin);

        AddQuad(vh, oTL, oTR, iTR, iTL, top);
        AddQuad(vh, oTR, oBR, iBR, iTR, right);
        AddQuad(vh, oBR, oBL, iBL, iBR, bottom);
        AddQuad(vh, oBL, oTL, iTL, iBL, left);
    }

    private void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
    {
        Color32 vertexColor = tint * color;
        int start = vh.currentVertCount;

        vh.AddVert(a, vertexColor, Vector2.zero);
        vh.AddVert(b, vertexColor, Vector2.zero);
        vh.AddVert(c, vertexColor, Vector2.zero);
        vh.AddVert(d, vertexColor, Vector2.zero);

        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start + 2, start + 3, start);
    }

    /// <summary>A ring can never be wider than half the rect, or the edges would cross over.</summary>
    private static float ClampWidth(float width, Rect rect) =>
        Mathf.Max(0f, Mathf.Min(width, rect.width * 0.5f, rect.height * 0.5f));

    private static Rect Shrink(Rect rect, float by) =>
        new Rect(rect.xMin + by, rect.yMin + by,
                 Mathf.Max(0f, rect.width - 2f * by), Mathf.Max(0f, rect.height - 2f * by));
}
