using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Win95 progress-bar fill: a row of separate solid blocks instead of one continuous bar. Only whole
/// blocks are drawn — a block appears once there is room for all of it — so the bar advances in
/// chunks, the way the Windows 95 copy dialog did.
///
/// It draws across its own rect, left to right, so it drops straight in as a Slider's Fill: the
/// slider widens the rect with its value, and a new block pops in each time one more fits. Size the
/// fill area to a whole number of blocks (n * blockWidth + (n - 1) * gap) and a full bar ends flush.
///
/// The colour is Graphic.color — drive it with a <see cref="UIThemeApplier"/> (Accent), like the
/// settings sliders' fill. Sizes are in canvas units.
/// </summary>
[ExecuteAlways]
// Graphic stopped requiring this itself in uGUI 2 (Unity 6); each concrete Graphic declares it now.
[RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("WIRED/UI/UI Block Fill")]
public class UIBlockFill : MaskableGraphic
{
    [Tooltip("Width of one block. Canvas units.")]
    [Min(1f)] [SerializeField] private float blockWidth = 18f;

    [Tooltip("Empty space between two blocks. Canvas units.")]
    [Min(0f)] [SerializeField] private float gap = 4f;

#if UNITY_EDITOR
    protected override void Reset()
    {
        base.Reset();
        // A fill must never swallow the click meant for whatever it sits in.
        raycastTarget = false;
    }
#endif

    // -- Mesh -------------------

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = GetPixelAdjustedRect();
        float step = blockWidth + gap;
        // The last block needs no gap after it. The epsilon keeps a block that fits exactly from
        // being lost to float error.
        int count = Mathf.FloorToInt((rect.width + gap) / step + 1e-4f);

        Color32 tint = color;
        for (int i = 0; i < count; i++)
        {
            float x = rect.xMin + i * step;
            AddQuad(vh, x, rect.yMin, x + blockWidth, rect.yMax, tint);
        }
    }

    private static void AddQuad(VertexHelper vh, float xMin, float yMin, float xMax, float yMax, Color32 tint)
    {
        int start = vh.currentVertCount;

        vh.AddVert(new Vector3(xMin, yMin), tint, Vector2.zero);
        vh.AddVert(new Vector3(xMin, yMax), tint, Vector2.zero);
        vh.AddVert(new Vector3(xMax, yMax), tint, Vector2.zero);
        vh.AddVert(new Vector3(xMax, yMin), tint, Vector2.zero);

        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start + 2, start + 3, start);
    }
}
