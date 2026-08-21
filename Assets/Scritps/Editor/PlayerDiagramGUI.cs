using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Small IMGUI drawing kit shared by the two player authoring inspectors,
/// <see cref="SO_MovementEditor"/> and <see cref="SO_CameraConfigEditor"/>.
///
/// Everything is plain rects. Diagonal lines rotate the GUI matrix around their start point and
/// stretch a thin rect, which keeps Handles out of an inspector where it would need its own
/// repaint handling to behave.
///
/// The palette follows the project's other custom inspectors: green is the good/applied state,
/// red is something wrong. Blue is reserved here for "standing", so the eye can pair a colour
/// with a stance across both diagrams.
/// </summary>
internal static class PlayerDiagramGUI
{
    private static bool Pro => EditorGUIUtility.isProSkin;

    internal static Color Ink      => Pro ? new Color(0.80f, 0.82f, 0.85f) : new Color(0.18f, 0.20f, 0.23f);
    internal static Color Muted    => Pro ? new Color(0.52f, 0.54f, 0.58f) : new Color(0.46f, 0.48f, 0.51f);
    internal static Color Backdrop => Pro ? new Color(0.16f, 0.17f, 0.18f) : new Color(0.85f, 0.86f, 0.87f);
    internal static Color Floor    => Pro ? new Color(0.40f, 0.42f, 0.45f) : new Color(0.55f, 0.57f, 0.60f);

    internal static readonly Color Standing = new Color(0.45f, 0.60f, 0.85f);
    internal static readonly Color Crouched = new Color(0.30f, 0.75f, 0.45f);
    internal static readonly Color Bad      = new Color(0.85f, 0.35f, 0.30f);
    internal static readonly Color Accent   = new Color(0.92f, 0.72f, 0.28f);

    private static GUIStyle textStyle;
    private static GUIStyle verdictStyle;

    /// <summary>
    /// Reserves a drawing area, paints its background and returns it already inset by
    /// <paramref name="pad"/> so callers never have to remember the padding.
    /// </summary>
    internal static Rect Canvas(float height, float pad = 7f)
    {
        Rect outer = EditorGUILayout.GetControlRect(false, height);
        EditorGUI.DrawRect(outer, Backdrop);
        return new Rect(outer.x + pad, outer.y + pad, outer.width - pad * 2f, outer.height - pad * 2f);
    }

    internal static void Box(Rect r, Color c) => EditorGUI.DrawRect(r, c);

    /// <summary>Outline-only box, for the stance the diagram is not talking about.</summary>
    internal static void Outline(Rect r, Color c, float thickness = 1f)
    {
        HLine(r.xMin, r.xMax, r.yMin, c, thickness);
        HLine(r.xMin, r.xMax, r.yMax, c, thickness);
        VLine(r.xMin, r.yMin, r.yMax, c, thickness);
        VLine(r.xMax, r.yMin, r.yMax, c, thickness);
    }

    internal static void HLine(float x0, float x1, float y, Color c, float thickness = 1f)
    {
        EditorGUI.DrawRect(new Rect(Mathf.Min(x0, x1), y - thickness * 0.5f,
                                    Mathf.Abs(x1 - x0), thickness), c);
    }

    internal static void VLine(float x, float y0, float y1, Color c, float thickness = 1f)
    {
        EditorGUI.DrawRect(new Rect(x - thickness * 0.5f, Mathf.Min(y0, y1),
                                    thickness, Mathf.Abs(y1 - y0)), c);
    }

    internal static void DashedHLine(float x0, float x1, float y, Color c,
                                     float dash = 7f, float gap = 5f, float thickness = 1f)
    {
        for (float x = x0; x < x1; x += dash + gap)
            HLine(x, Mathf.Min(x + dash, x1), y, c, thickness);
    }

    /// <summary>
    /// Diagonal line. Rotating the matrix instead of reaching for Handles: Handles wants a
    /// Repaint-only guard and its own colour state, and this is two lines of arithmetic.
    /// </summary>
    internal static void Line(Vector2 a, Vector2 b, Color c, float thickness = 1.5f)
    {
        float length = Vector2.Distance(a, b);
        if (length < 0.01f) return;

        float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
        Matrix4x4 saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, a);
        EditorGUI.DrawRect(new Rect(a.x, a.y - thickness * 0.5f, length, thickness), c);
        GUI.matrix = saved;
    }

    /// <summary>Vertical measurement: a capped bar with the distance written next to it.</summary>
    internal static void VMeasure(float x, float yA, float yB, Color c, string label, float labelWidth = 150f)
    {
        float top = Mathf.Min(yA, yB);
        float bottom = Mathf.Max(yA, yB);

        VLine(x, top, bottom, c);
        HLine(x - 3.5f, x + 3.5f, top, c);
        HLine(x - 3.5f, x + 3.5f, bottom, c);
        Text(new Rect(x + 6f, (top + bottom) * 0.5f - 8f, labelWidth, 16f), label, c);
    }

    internal static void Text(Rect r, string s, Color c,
                              TextAnchor anchor = TextAnchor.MiddleLeft,
                              FontStyle fontStyle = FontStyle.Normal)
    {
        if (textStyle == null) textStyle = new GUIStyle(EditorStyles.miniLabel);
        textStyle.alignment = anchor;
        textStyle.fontStyle = fontStyle;
        textStyle.normal.textColor = c;
        GUI.Label(r, s, textStyle);
    }

    /// <summary>One horizontal bar in a comparison group: name on the left, value on the right.</summary>
    internal static void Bar(Rect row, string label, float value, float max, Color c,
                             string valueText, float labelWidth = 74f, float valueWidth = 58f)
    {
        Text(new Rect(row.x, row.y, labelWidth, row.height), label, Muted);

        float trackX = row.x + labelWidth;
        float trackW = Mathf.Max(4f, row.width - labelWidth - valueWidth);
        float fill = max > 0.0001f ? Mathf.Clamp01(value / max) : 0f;

        Box(new Rect(trackX, row.y + row.height * 0.5f - 4f, trackW, 8f),
            new Color(c.r, c.g, c.b, 0.16f));
        Box(new Rect(trackX, row.y + row.height * 0.5f - 4f, trackW * fill, 8f), c);

        Text(new Rect(trackX + trackW + 6f, row.y, valueWidth, row.height), valueText, Ink,
             TextAnchor.MiddleRight);
    }

    /// <summary>Pass/fail line under a diagram. Escapes, not literal glyphs, on purpose.</summary>
    internal static void Verdict(bool ok, string message)
    {
        if (verdictStyle == null) verdictStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
        verdictStyle.normal.textColor = ok ? Crouched : Bad;
        EditorGUILayout.LabelField((ok ? "\u2713  " : "\u2717  ") + message, verdictStyle);
    }

    /// <summary>
    /// The player in the loaded scenes, or null. Include inactive for the same reason
    /// SO_VisionFogConfigEditor does: a disabled object is exactly when the wiring is wrong.
    /// </summary>
    internal static PlayerStateManager FindLoadedPlayer()
    {
        PlayerStateManager[] found = Object.FindObjectsByType<PlayerStateManager>(FindObjectsInactive.Include);
        return found.Length > 0 ? found[0] : null;
    }

    internal static void SectionHeader(string title)
    {
        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }
}
#endif
