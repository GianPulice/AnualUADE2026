using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A ring, or a piece of one, drawn as mesh: no sprite, so it stays crisp at any size and the arc can
/// be moved or resized every frame without touching an Image's fill settings.
///
/// Angles follow a clock face, not trigonometry: 0 is twelve o'clock and the arc grows clockwise.
/// That is the convention the skill check needle and the module timer both read in, so neither has
/// to convert.
///
/// Two looks:
///  • Smooth (blockCount 0): one continuous band, tessellated at segmentsPerCircle.
///  • Blocks (blockCount > 0): the full circle is split into that many equal cells separated by
///    blockGapDegrees, and only the cells the arc fully covers are drawn — the ring version of
///    <see cref="UIBlockFill"/>, so a draining timer loses a whole block at a time, Win95-style.
///
/// The colour is Graphic.color — drive it with a <see cref="UIThemeApplier"/>. Sizes are in canvas
/// units; the outer radius is half the rect's shorter side.
/// </summary>
[ExecuteAlways]
// Graphic stopped requiring this itself in uGUI 2 (Unity 6); each concrete Graphic declares it now.
[RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("WIRED/UI/UI Ring Arc")]
public class UIRingArc : MaskableGraphic
{
    [Tooltip("Where the arc starts, in degrees. 0 = twelve o'clock, growing clockwise.")]
    [SerializeField] private float startAngle = 0f;

    [Tooltip("How much of the circle the arc covers, in degrees. 360 = full ring.")]
    [Range(0f, 360f)] [SerializeField] private float sweep = 360f;

    [Tooltip("Width of the band, from the outer edge inwards. Canvas units.")]
    [Min(0.5f)] [SerializeField] private float thickness = 6f;

    [Tooltip("Smoothness of a full circle. The arc uses its share of these.")]
    [Range(8, 256)] [SerializeField] private int segmentsPerCircle = 96;

    [Tooltip("0 = one continuous band. Above 0 = the full circle is split into this many cells and " +
             "only the ones the arc fully covers are drawn.")]
    [Min(0)] [SerializeField] private int blockCount = 0;

    [Tooltip("Empty space between two cells when blockCount > 0. Degrees.")]
    [Min(0f)] [SerializeField] private float blockGapDegrees = 2f;

    public float StartAngle => startAngle;
    public float Sweep => sweep;

    /// <summary>Moves / resizes the arc. Rebuilds the mesh only when something actually changed, so
    /// it is cheap to call every frame from a timer.</summary>
    public void SetArc(float start, float newSweep)
    {
        newSweep = Mathf.Clamp(newSweep, 0f, 360f);
        if (Mathf.Approximately(start, startAngle) && Mathf.Approximately(newSweep, sweep)) return;

        startAngle = start;
        sweep = newSweep;
        SetVerticesDirty();
    }

    public void SetSweep(float newSweep) => SetArc(startAngle, newSweep);

    public void SetThickness(float value)
    {
        value = Mathf.Max(0.5f, value);
        if (Mathf.Approximately(value, thickness)) return;
        thickness = value;
        SetVerticesDirty();
    }

#if UNITY_EDITOR
    protected override void Reset()
    {
        base.Reset();
        // A ring must never swallow the click meant for whatever it sits in.
        raycastTarget = false;
    }
#endif

    // -- Mesh -------------------

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (sweep <= 0f) return;

        Rect rect = GetPixelAdjustedRect();
        float outer = Mathf.Min(rect.width, rect.height) * 0.5f;
        if (outer <= 0f) return;
        float inner = Mathf.Max(0f, outer - thickness);
        Vector2 centre = rect.center;
        Color32 tint = color;

        if (blockCount > 0)
        {
            float cell = 360f / blockCount;
            float gap = Mathf.Min(blockGapDegrees, cell * 0.9f);
            // The epsilon keeps a cell the arc covers exactly from being lost to float error.
            int count = Mathf.FloorToInt(sweep / cell + 1e-4f);
            for (int i = 0; i < count; i++)
            {
                float a0 = startAngle + i * cell + gap * 0.5f;
                AddBand(vh, centre, inner, outer, a0, a0 + cell - gap, tint);
            }
            return;
        }

        AddBand(vh, centre, inner, outer, startAngle, startAngle + sweep, tint);
    }

    /// <summary>One continuous band between two clock angles, as a strip of quads.</summary>
    private void AddBand(VertexHelper vh, Vector2 centre, float inner, float outer,
                         float fromDeg, float toDeg, Color32 tint)
    {
        float span = toDeg - fromDeg;
        if (span <= 0f) return;

        int steps = Mathf.Max(1, Mathf.CeilToInt(segmentsPerCircle * span / 360f));
        int start = vh.currentVertCount;

        for (int i = 0; i <= steps; i++)
        {
            Vector2 dir = ClockDirection(fromDeg + span * i / steps);
            vh.AddVert(centre + dir * outer, tint, Vector2.zero);
            vh.AddVert(centre + dir * inner, tint, Vector2.zero);
        }

        for (int i = 0; i < steps; i++)
        {
            int o0 = start + i * 2;
            int i0 = o0 + 1;
            int o1 = o0 + 2;
            int i1 = o0 + 3;
            vh.AddTriangle(o0, o1, i1);
            vh.AddTriangle(i1, i0, o0);
        }
    }

    /// <summary>Unit vector for a clock angle: 0 = up, 90 = right.</summary>
    public static Vector2 ClockDirection(float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
    }
}
