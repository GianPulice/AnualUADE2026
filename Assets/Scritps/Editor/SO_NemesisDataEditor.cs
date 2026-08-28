using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Inspector for <see cref="SO_NemesisData"/>. Same job as <see cref="SO_MovementEditor"/> does
/// for the player: the numbers alone do not tell you what they mean in the world. "View range 5,
/// hearing 10" says nothing about whether hearing swallows vision, or how small 5 actually gets
/// once <c>crouchVisionMultiplier</c> eats into it (2.09 m in this project's shipped asset — most
/// people guessing that number land nowhere close).
///
/// Three sections, same shape as the player editor:
/// <list type="bullet">
/// <item><b>Rangos</b> — every detection range drawn top-down, to scale, around a "you are here"
/// marker for the Nemesis. Hearing and the two detection circles are full circles; vision is a
/// wedge at the real cone angle, because a sphere throws away exactly the number (the angle) that
/// matters most.</item>
/// <item><b>Probar un caso</b> — distance + angle sliders with live pass/fail against every sense,
/// and the test point drawn on the diagram so the verdicts and the picture read as one thing.</item>
/// <item><b>Chequeos</b> — the two relationships the fields' own tooltips already assert
/// (proximity detection should stay well under view range; hearing through a floor should be more
/// generous than through a wall) turned into something that actually fails loudly when an edit
/// breaks them, plus a couple of derived numbers worth seeing without doing the multiplication by
/// hand.</item>
/// </list>
///
/// Deliberately does NOT reach into a loaded scene the way <see cref="SO_CameraConfigEditor"/>
/// does: every range here lives entirely on this asset, so the diagram works with no Nemesis
/// loaded anywhere, and there is no live/fallback distinction to explain.
///
/// This is the asset-level twin of <see cref="NemesisGizmos"/>, which draws the same ranges in the
/// Scene view from an actual Nemesis instance. Use this one to tune the numbers in isolation; use
/// that one to see them against the real level geometry.
///
/// Labels in Spanish to match the other custom inspectors in this project.
/// </summary>
[CustomEditor(typeof(SO_NemesisData))]
public class SO_NemesisDataEditor : Editor
{
    private const string TestDistanceKey = "WIRED.SO_NemesisDataEditor.TestDistance";
    private const string TestBearingKey  = "WIRED.SO_NemesisDataEditor.TestBearing";
    private const float DefaultTestDistance = 4f;
    private const float DefaultTestBearing  = 0f;

    // Compass bearings (0 = up/forward, clockwise) each range's label is placed at, so five
    // circles that can share almost the same radius (proximity detection and catch reach are both
    // 1.5 m on the shipped asset) still land as five readable labels instead of one smear of text.
    private const float HearingLabelBearing  = 90f;
    private const float ProximityLabelBearing = 270f;
    private const float CatchLabelBearing    = 180f;

    private static readonly Color TestPointColor = new Color(0.92f, 0.72f, 0.28f);

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SO_NemesisData data = (SO_NemesisData)target;

        PlayerDiagramGUI.SectionHeader("Rangos");
        DrawRangeDiagram(data);

        PlayerDiagramGUI.SectionHeader("Probar un caso");
        DrawCaseTester(data);

        PlayerDiagramGUI.SectionHeader("Chequeos");
        DrawChecks(data);
    }

    // Rangos ==================================================================================

    private void DrawRangeDiagram(SO_NemesisData data)
    {
        Rect canvas = PlayerDiagramGUI.Canvas(300f);
        Vector2 origin = new Vector2(canvas.x + canvas.width * 0.5f, canvas.y + canvas.height * 0.5f);
        float pxPerMetre = PxPerMetre(canvas, MaxDetectionRadius(data));

        DrawDetectionShapes(data, origin, pxPerMetre);
        PlayerDiagramGUI.Pawn(origin, PlayerDiagramGUI.Ink, "Nemesis");
    }

    /// <summary>
    /// Only the shapes that actually detect something decide the scale. ProximityRadius (the HUD
    /// vignette) and SearchSweepRadius are deliberately excluded — including a cosmetic 12 m ring
    /// here would shrink every detection shape down to a third of the canvas to make room for a
    /// range that catches nobody. Both are covered in the Scene view by NemesisGizmos, which has
    /// room to spare.
    /// </summary>
    private static float MaxDetectionRadius(SO_NemesisData data) =>
        Mathf.Max(1f, data.ViewRange, data.ListenRange, data.ProximityDetectionRange, data.CatchMaxReach);

    private static float PxPerMetre(Rect canvas, float maxRadius) =>
        Mathf.Min(canvas.width, canvas.height) * 0.5f * 0.86f / maxRadius;

    /// <summary>
    /// Every detection shape, at one origin and scale. Shared by the "Rangos" diagram and the
    /// "Probar un caso" tester so the second one is a full redraw of the first — not a smaller,
    /// separate strip — and the test point lands visibly inside or outside the exact circles that
    /// decide each verdict below it, instead of next to an abstract distance readout.
    /// </summary>
    private static void DrawDetectionShapes(SO_NemesisData data, Vector2 origin, float pxPerMetre)
    {
        DrawHearing(data, origin, pxPerMetre);
        DrawProximityDetection(data, origin, pxPerMetre);
        DrawVision(data, origin, pxPerMetre);
        DrawCatch(data, origin, pxPerMetre);
    }

    private static void DrawHearing(SO_NemesisData data, Vector2 origin, float pxPerMetre)
    {
        if (data.ListenRange <= 0.01f) return;

        Color hearing = new Color(0.541f, 0.706f, 0.831f);
        float radiusPx = data.ListenRange * pxPerMetre;

        PlayerDiagramGUI.Circle(origin, radiusPx, hearing);
        LabelAt(origin, radiusPx, HearingLabelBearing, $"oído {data.ListenRange:0.#} m", hearing);

        if (!data.WallOcclusionEnabled) return;

        // Faded rather than dashed: same call NemesisGizmos makes in the Scene view, and for the
        // same reason — these are the ranges that actually apply most of the time (the Nemesis is
        // almost never in an open room with the player), so they are drawn inside the full radius
        // rather than as an equally strong second circle competing with it.
        Color faded = new Color(hearing.r, hearing.g, hearing.b, 0.5f);
        PlayerDiagramGUI.Circle(origin, data.ListenRange * data.WallOcclusionMultiplier * pxPerMetre, faded, 1f);
        PlayerDiagramGUI.Circle(origin, data.ListenRange * data.FloorOcclusionMultiplier * pxPerMetre, faded, 1f);
    }

    private static void DrawProximityDetection(SO_NemesisData data, Vector2 origin, float pxPerMetre)
    {
        if (data.ProximityDetectionRange <= 0.01f) return;

        Color color = new Color(0.95f, 0.55f, 0.25f);
        float radiusPx = data.ProximityDetectionRange * pxPerMetre;

        PlayerDiagramGUI.Circle(origin, radiusPx, color);
        LabelAt(origin, radiusPx, ProximityLabelBearing,
                $"detección dura {data.ProximityDetectionRange:0.#} m", color);
    }

    private static void DrawVision(SO_NemesisData data, Vector2 origin, float pxPerMetre)
    {
        Color vision = new Color(1f, 0.784f, 0.314f);
        Color crouch = new Color(0.55f, 0.75f, 0.45f);

        if (data.ViewRange > 0.01f)
        {
            float radiusPx = data.ViewRange * pxPerMetre;
            PlayerDiagramGUI.Arc(origin, radiusPx, 0f, data.ViewAngle, vision);
            LabelAt(origin, radiusPx, 0f, $"visión {data.ViewRange:0.#} m", vision);
        }

        float crouched = data.ViewRange * data.CrouchVisionMultiplier;
        if (crouched <= 0.01f) return;

        float crouchedPx = crouched * pxPerMetre;
        PlayerDiagramGUI.Arc(origin, crouchedPx, 0f, data.ViewAngle, crouch);
        // Offset a little off dead-centre so it does not sit exactly under the healthy-range
        // label when the two radii land close together.
        LabelAt(origin, crouchedPx, -28f, $"agachado {crouched:0.##} m", crouch);
    }

    private static void DrawCatch(SO_NemesisData data, Vector2 origin, float pxPerMetre)
    {
        if (data.CatchMaxReach <= 0.01f) return;

        Color color = new Color(0.8f, 0.10f, 0.10f);
        float radiusPx = data.CatchMaxReach * pxPerMetre;

        PlayerDiagramGUI.Circle(origin, radiusPx, color);
        LabelAt(origin, radiusPx, CatchLabelBearing, $"atrapa {data.CatchMaxReach:0.##} m", color);
    }

    /// <summary>Places a tick + value at a bearing on a circle's rim — see the per-shape bearing
    /// constants above for why this is not always "straight up".</summary>
    private static void LabelAt(Vector2 origin, float radiusPx, float bearingDeg, string text, Color c)
    {
        float rad = bearingDeg * Mathf.Deg2Rad;
        Vector2 point = origin + new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad)) * radiusPx;

        PlayerDiagramGUI.Text(new Rect(point.x - 60f, point.y - 6f, 120f, 13f), text, c,
                              TextAnchor.MiddleCenter);
    }

    // Probar un caso ==========================================================================

    private void DrawCaseTester(SO_NemesisData data)
    {
        float distance = EditorPrefs.GetFloat(TestDistanceKey, DefaultTestDistance);
        float bearing = EditorPrefs.GetFloat(TestBearingKey, DefaultTestBearing);

        float maxRadius = Mathf.Max(1f, data.ViewRange, data.ListenRange,
                                    data.ProximityDetectionRange, data.CatchMaxReach);

        EditorGUI.BeginChangeCheck();
        float newDistance = EditorGUILayout.Slider(
            new GUIContent("Distancia al jugador (m)",
                           "Se guarda en EditorPrefs, no en el asset: es una regla de medir."),
            distance, 0f, maxRadius * 1.3f);
        float newBearing = EditorGUILayout.Slider(
            new GUIContent("Ángulo respecto al frente (°)",
                           "0 = de frente, 180 = a la espalda. El cono de visión es simétrico, " +
                           "así que un solo lado alcanza para probar los dos."),
            bearing, 0f, 180f);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetFloat(TestDistanceKey, newDistance);
            EditorPrefs.SetFloat(TestBearingKey, newBearing);
        }
        distance = newDistance;
        bearing = newBearing;

        DrawZoomedTest(data, distance, bearing);

        bool withinCone = bearing <= data.ViewAngle * 0.5f;
        bool seenStanding = withinCone && distance <= data.ViewRange;
        bool seenCrouched = withinCone && distance <= data.ViewRange * data.CrouchVisionMultiplier;
        bool heard = distance <= data.ListenRange;
        bool hardDetected = distance <= data.ProximityDetectionRange;
        bool catchable = distance <= data.CatchMaxReach;

        PlayerDiagramGUI.Verdict(seenStanding,
            seenStanding ? "Te ve parado" : "No te ve parado (fuera de rango o del cono)");
        PlayerDiagramGUI.Verdict(seenCrouched,
            seenCrouched ? "Te ve agachado" : "No te ve agachado");
        PlayerDiagramGUI.Verdict(heard, heard ? "Te oye" : "No te oye");
        PlayerDiagramGUI.Verdict(hardDetected,
            hardDetected ? "Detección dura: te nota igual, sin importar nada más"
                         : "Fuera de la detección dura");
        PlayerDiagramGUI.Verdict(catchable,
            catchable ? "Dentro del alcance de atrapada (sólo importa si ya te está persiguiendo)"
                      : "Fuera de alcance de atrapada");

        EditorGUILayout.HelpBox(
            "Prueba en 2D, sin paredes ni pisos de por medio: no reproduce oclusión " +
            "(WallOcclusionMultiplier / FloorOcclusionMultiplier / ProximityDetectionRespectsWalls) " +
            "ni CatchMaxVerticalOffset, que es un eje aparte. Para eso, con el Nemesis en escena, " +
            "mirá los gizmos (NemesisGizmos) contra la geometría real.",
            MessageType.Info);
    }

    /// <summary>
    /// The tested position, on the same origin/scale as the shapes it is being tested against.
    /// Drawn to the right (positive bearing = clockwise) since the cone itself is symmetric and
    /// there is nothing a left/right choice would add — the slider's 0–180° already covers every
    /// distinct case.
    /// </summary>
    /// <summary>
    /// Nemesis and player, framed to fit the two of them rather than to fit the biggest detection
    /// range. That is the difference between this and the "Rangos" diagram above: there, the scale
    /// is fixed so a 10 m hearing circle always fits, which is exactly what makes a 1.5 m proximity
    /// circle and a 1.5 m catch circle collapse into two indistinguishable dots when the case
    /// worth testing is usually a close one. Here the camera zooms to the pair of them — close
    /// together, close-up; far apart, pulled back — so the two circles the test is actually about
    /// stay legible at whatever distance is being tried.
    ///
    /// The trade is that at a tight zoom, a wide detection shape (hearing, at full range) now
    /// routinely extends past the canvas — which is fine, even useful (it reads as "well outside
    /// this shape"), but has to be clipped or it bleeds into the inspector rows drawn after it.
    /// </summary>
    private void DrawZoomedTest(SO_NemesisData data, float distance, float bearing)
    {
        const float MinZoomDistance = 0.6f;
        const float MinPxPerMetre = 6f;
        const float MaxPxPerMetre = 160f;

        Rect canvas = PlayerDiagramGUI.Canvas(220f);

        float pxPerMetre = Mathf.Clamp(
            Mathf.Min(canvas.width, canvas.height) * 0.32f / Mathf.Max(distance, MinZoomDistance),
            MinPxPerMetre, MaxPxPerMetre);

        // Local to the clip rect below (its own top-left becomes (0,0)), not canvas-absolute —
        // that is what BeginClip expects from anything drawn inside it.
        Vector2 centre = new Vector2(canvas.width * 0.5f, canvas.height * 0.5f);
        float rad = bearing * Mathf.Deg2Rad;
        Vector2 halfOffset = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad)) * (distance * pxPerMetre * 0.5f);

        Vector2 nemesisPos = centre - halfOffset;
        Vector2 playerPos = centre + halfOffset;

        GUI.BeginClip(canvas);

        DrawDetectionShapes(data, nemesisPos, pxPerMetre);
        PlayerDiagramGUI.Pawn(nemesisPos, PlayerDiagramGUI.Ink, "Nemesis");

        PlayerDiagramGUI.Line(nemesisPos, playerPos, TestPointColor, 1.5f);
        PlayerDiagramGUI.Circle(playerPos, 6f, TestPointColor, 2.5f, 16);
        PlayerDiagramGUI.Text(new Rect(playerPos.x - 45f, playerPos.y + 8f, 90f, 13f), "Jugador",
                              TestPointColor, TextAnchor.MiddleCenter);

        Vector2 midpoint = (nemesisPos + playerPos) * 0.5f;
        PlayerDiagramGUI.Text(new Rect(midpoint.x - 45f, midpoint.y - 20f, 90f, 13f),
                              $"{distance:0.##} m", PlayerDiagramGUI.Muted, TextAnchor.MiddleCenter);

        GUI.EndClip();
    }

    // Chequeos ================================================================================

    private static void DrawChecks(SO_NemesisData data)
    {
        // Backed directly by ProximityDetectionRange's own tooltip: "Keep it well under viewRange
        // — this is 'it is literally on top of me', not a second vision range."
        bool proximityIsTight = data.ProximityDetectionRange < data.ViewRange * 0.75f;
        PlayerDiagramGUI.Verdict(proximityIsTight,
            proximityIsTight
                ? $"Detección dura ({data.ProximityDetectionRange:0.##} m) bien por debajo de la visión ({data.ViewRange:0.##} m)"
                : $"Detección dura ({data.ProximityDetectionRange:0.##} m) se acerca demasiado a la visión ({data.ViewRange:0.##} m) — empieza a leerse como un segundo rango de visión, no como 'está encima mío'");

        if (data.WallOcclusionEnabled)
        {
            // Backed by FloorOcclusionMultiplier's own tooltip: "Deliberately more generous than
            // the wall multiplier."
            bool floorMoreGenerous = data.FloorOcclusionMultiplier >= data.WallOcclusionMultiplier;
            PlayerDiagramGUI.Verdict(floorMoreGenerous,
                floorMoreGenerous
                    ? "El piso deja pasar el sonido al menos tanto como una pared, como corresponde"
                    : "El piso atenúa MÁS que una pared — el Nemesis nunca va a poder ubicarte un piso arriba/abajo por sonido, que es su único canal ahí");
        }

        float crouched = data.ViewRange * data.CrouchVisionMultiplier;
        EditorGUILayout.LabelField(
            $"Agachado te ve recién a {crouched:0.##} m (visión sana ×{data.CrouchVisionMultiplier:0.##}).",
            EditorStyles.wordWrappedMiniLabel);

        if (data.WallOcclusionEnabled)
        {
            EditorGUILayout.LabelField(
                $"Te oye a través de una pared hasta {data.ListenRange * data.WallOcclusionMultiplier:0.##} m, " +
                $"y a través de un piso hasta {data.ListenRange * data.FloorOcclusionMultiplier:0.##} m.",
                EditorStyles.wordWrappedMiniLabel);
        }
    }
}
#endif
