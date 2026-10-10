using UnityEngine;
#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;

/// <summary>
/// Inspector for <see cref="SO_NemesisData"/>. Same job as <see cref="SO_MovementEditor"/> does
/// for the player: the numbers alone do not tell you what they mean in the world. "View range 5,
/// hearing 10" says nothing about whether hearing swallows vision, or how small 5 actually gets
/// once <c>crouchVisionMultiplier</c> eats into it (2.09 m in this project's shipped asset — most
/// people guessing that number land nowhere close).
///
/// Four sections, the first three the same shape as the player editor:
/// <list type="bullet">
/// <item><b>Rangos</b> — every detection range drawn top-down, to scale, around a "you are here"
/// marker for the Nemesis. Hearing and the two detection circles are full circles; vision is a
/// wedge at the real cone angle, because a sphere throws away exactly the number (the angle) that
/// matters most.</item>
/// <item><b>Probar un caso</b> — distance + angle sliders with live pass/fail against every sense,
/// and the test point drawn on the diagram so the verdicts and the picture read as one thing.</item>
/// <item><b>Perderlo de vista</b> — the second and a bit after the eyes lose the player, to scale
/// in time rather than in metres: how long the chase holds on what it still knows, with and without
/// hearing them, and how long a route verdict has to stand before the ladder believes it.</item>
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
    private static readonly Color FocusColor = new Color(0.95f, 0.55f, 0.25f);
    private static readonly Color RearColor = new Color(0.62f, 0.58f, 0.78f);   // Same as NemesisGizmos.

    /// <summary>
    /// What the search box holds. Per inspector instance and not persisted: a filter that survived
    /// a domain reload would have the asset come up half-empty with no obvious reason, which is a
    /// worse first impression than retyping three letters.
    /// </summary>
    private string filter = string.Empty;

    public override void OnInspectorGUI()
    {
        DrawFilterableFields();

        SO_NemesisData data = (SO_NemesisData)target;

        PlayerDiagramGUI.SectionHeader("Rangos");
        DrawRangeDiagram(data);

        PlayerDiagramGUI.SectionHeader("Probar un caso");
        DrawCaseTester(data);

        PlayerDiagramGUI.SectionHeader("Perderlo de vista");
        DrawLostSightTimeline(data);

        PlayerDiagramGUI.SectionHeader("Chequeos");
        DrawChecks(data);
    }

    // Buscador ================================================================================
    //
    // This asset carries around ninety fields under twenty headers, which is past the point where
    // scrolling is a search strategy. Every one of them is reachable by name, by the header it
    // lives under, or by any word in its tooltip — the tooltips are the richest text on the asset
    // and the thing you actually remember ("el que decide si cruza pisos"), so leaving them out of
    // the match would make the box useless for exactly the field you cannot name.

    /// <summary>
    /// The fields, filtered. With an empty box this is <c>DrawDefaultInspector</c> field for
    /// field — same order, same headers, same drawers — so nothing is lost by the box existing.
    /// </summary>
    private void DrawFilterableFields()
    {
        DrawSearchBox();

        string[] terms = filter.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        bool filtering = terms.Length > 0;

        serializedObject.Update();

        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true;

        // The header a field falls under is not on the field: it is on whichever field STARTED the
        // group, so it has to be carried forward as the iteration walks past the ones that follow.
        string group = "General";
        string lastDrawnGroup = null;

        int shown = 0;
        int total = 0;

        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;   // Top level only; each field's own drawer owns its children.

            if (property.propertyPath == "m_Script")
            {
                if (!filtering)
                {
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(property);
                }
                continue;
            }

            total++;

            FieldInfo field = FindField(property.name);
            string header = AttributeOf<HeaderAttribute>(field)?.header;
            if (!string.IsNullOrEmpty(header)) group = header;

            if (filtering)
            {
                if (!Matches(terms, property, field, group)) continue;

                if (lastDrawnGroup != group)
                {
                    lastDrawnGroup = group;

                    // A field that carries the [Header] itself already has Unity drawing it as a
                    // decorator; adding ours on top would print the group name twice.
                    if (string.IsNullOrEmpty(header))
                    {
                        EditorGUILayout.Space(6f);
                        EditorGUILayout.LabelField(group, EditorStyles.boldLabel);
                    }
                }
            }

            shown++;
            EditorGUILayout.PropertyField(property, true);
        }

        serializedObject.ApplyModifiedProperties();

        if (!filtering) return;

        EditorGUILayout.Space(2f);

        if (shown == 0)
        {
            EditorGUILayout.HelpBox($"Ningún campo coincide con «{filter}». Se busca en el nombre, " +
                                    "en el título de la sección y en el texto de ayuda de cada campo.",
                                    MessageType.Info);
        }
        else
        {
            EditorGUILayout.LabelField($"{shown} de {total} campos", EditorStyles.miniLabel);
        }
    }

    private void DrawSearchBox()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Buscar campo", GUILayout.Width(90f));

            // The toolbar style over a plain text field only because it reads as a search box at a
            // glance; nothing below depends on it.
            filter = EditorGUILayout.TextField(filter, EditorStyles.toolbarSearchField);

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(filter)))
            {
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22f)))
                {
                    filter = string.Empty;
                    GUI.FocusControl(null);
                }
            }
        }

        EditorGUILayout.Space(2f);
    }

    /// <summary>
    /// Every term has to match SOMETHING — an AND across terms, an OR across the places looked at.
    /// That is what makes two words useful: "montacargas tiempo" narrows, where either alone does
    /// not.
    /// </summary>
    private static bool Matches(string[] terms, SerializedProperty property, FieldInfo field,
                                string group)
    {
        string haystack = $"{property.displayName} {property.name} {group} " +
                          $"{AttributeOf<TooltipAttribute>(field)?.tooltip}";

        foreach (string term in terms)
        {
            if (haystack.IndexOf(term, System.StringComparison.OrdinalIgnoreCase) < 0) return false;
        }

        return true;
    }

    /// <summary>
    /// The backing field for a serialized property, private ones included — which is all of them
    /// here. Walks the base types because <c>GetField</c> does not see a private member declared
    /// on a parent.
    /// </summary>
    private FieldInfo FindField(string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (System.Type type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field != null) return field;
        }

        return null;
    }

    private static T AttributeOf<T>(FieldInfo field) where T : System.Attribute =>
        field != null ? field.GetCustomAttribute<T>() : null;

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
        Mathf.Max(1f, data.ViewRange, data.ViewHoldRange, data.ViewHuntRange, data.ListenRange,
                  data.ProximityDetectionRange, data.CatchMaxReach);

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
        DrawRear(data, origin, pxPerMetre);
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
        Color underTable = new Color(0.35f, 0.82f, 0.80f);   // Same teal as NemesisGizmos.

        if (data.ViewRange > 0.01f)
        {
            float radiusPx = data.ViewRange * pxPerMetre;
            PlayerDiagramGUI.Arc(origin, radiusPx, 0f, data.ViewAngle, vision);

            // The focus wedge, nested. What the reader is meant to take from the picture is the
            // GAP between the two arcs: that is the peripheral band, the only place where being
            // seen takes time instead of happening the instant you step into it. Drawn in the
            // hard-detection orange rather than a colour of its own, because "inside this you are
            // spotted immediately" is the same claim the proximity circle makes.
            if (data.HasPeripheralVision)
            {
                PlayerDiagramGUI.Arc(origin, radiusPx, 0f, data.FocusAngle, FocusColor);
                LabelAt(origin, radiusPx * 0.55f, 0f, $"foco {data.FocusAngle:0.#}\u00b0", FocusColor);
            }
            LabelAt(origin, radiusPx, 0f, $"visión {data.ViewRange:0.#} m", vision);

            DrawAdaptiveRanges(data, origin, pxPerMetre, vision);
        }

        float crouched = data.ViewRange * data.CrouchVisionMultiplier;
        if (crouched > 0.01f)
        {
            float crouchedPx = crouched * pxPerMetre;
            PlayerDiagramGUI.Arc(origin, crouchedPx, 0f, data.ViewAngle, crouch);
            // Offset a little off dead-centre so it does not sit exactly under the healthy-range
            // label when the two radii land close together.
            LabelAt(origin, crouchedPx, -28f, $"agachado {crouched:0.##} m", crouch);
        }

        // Under a table, the same shortened wedge again (plan §3.4, level B): a table shortens the
        // view exactly the way a crouch does, and the picture should say so. What it feeds is
        // different — the suspicion meter, never an instant sighting — and that is on the field's
        // tooltip, not something an arc can show. Labelled on the opposite side from the crouched
        // one, because the two radii routinely land within a metre of each other.
        float underTableRange = data.ViewRange * data.UnderTableVisionMultiplier;
        if (underTableRange <= 0.01f) return;

        float underTablePx = underTableRange * pxPerMetre;
        PlayerDiagramGUI.Arc(origin, underTablePx, 0f, data.ViewAngle, underTable);
        LabelAt(origin, underTablePx, 28f, $"bajo mesa {underTableRange:0.##} m", underTable);
    }

    /// <summary>
    /// The two rings past the base wedge (AdaptiveViewRange): how far it HOLDS a player it is already
    /// seeing, and how far a HUNT lets it see again one it lost. Thin and faint, because neither is
    /// where it notices anybody — that is still the solid wedge inside them, and the picture has to
    /// keep saying so. One ring when the two coincide (as shipped, both ×2), labelled off to the
    /// sides so they stay clear of the base range's own label.
    /// </summary>
    private static void DrawAdaptiveRanges(SO_NemesisData data, Vector2 origin, float pxPerMetre,
                                           Color vision)
    {
        const float Visible = 0.05f;
        const float LabelBearing = 38f;

        float hold = data.ViewHoldRange;
        float hunt = data.ViewHuntRange;
        Color faint = new Color(vision.r, vision.g, vision.b, 0.5f);

        if (Mathf.Abs(hold - hunt) <= Visible)
        {
            if (hold <= data.ViewRange + Visible) return;

            PlayerDiagramGUI.Arc(origin, hold * pxPerMetre, 0f, data.ViewAngle, faint, 1f);
            LabelAt(origin, hold * pxPerMetre, LabelBearing, $"sostiene / caza {hold:0.#} m", faint);
            return;
        }

        if (hold > data.ViewRange + Visible)
        {
            PlayerDiagramGUI.Arc(origin, hold * pxPerMetre, 0f, data.ViewAngle, faint, 1f);
            LabelAt(origin, hold * pxPerMetre, LabelBearing, $"sostiene {hold:0.#} m", faint);
        }

        if (hunt > data.ViewRange + Visible)
        {
            PlayerDiagramGUI.Arc(origin, hunt * pxPerMetre, 0f, data.ViewAngle, faint, 1f);
            LabelAt(origin, hunt * pxPerMetre, -LabelBearing, $"caza {hunt:0.#} m", faint);
        }
    }

    /// <summary>The third zone (VisionZones.EZone.Rear): everything outside the cone, out to
    /// RearSenseRange — the wedge behind it. Faint, because it is the weakest of the three.</summary>
    private static void DrawRear(SO_NemesisData data, Vector2 origin, float pxPerMetre)
    {
        if (data.RearSenseRange <= 0.01f || data.RearSenseStrength <= 0f) return;

        float radiusPx = data.RearSenseRange * pxPerMetre;
        PlayerDiagramGUI.Arc(origin, radiusPx, 180f, 360f - data.ViewAngle, RearColor);
        LabelAt(origin, radiusPx, 180f, $"atrás {data.RearSenseRange:0.#} m", RearColor);
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

        // The same radius the "Rangos" diagram scales to, so the slider reaches every ring drawn
        // there — the hold and hunt rings included, which sit past the base view range.
        float maxRadius = MaxDetectionRadius(data);

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
        bool withinFocus = bearing <= data.FocusAngle * 0.5f;
        bool seenStanding = withinCone && distance <= data.ViewRange;
        bool seenCrouched = withinCone && distance <= data.ViewRange * data.CrouchVisionMultiplier;
        bool heard = distance <= data.ListenRange;
        bool hardDetected = distance <= data.ProximityDetectionRange;
        bool catchable = distance <= data.CatchMaxReach;

        // "Nota" and not "ve" since 04/10: these two are the BASE range, what a Nemesis that has not
        // seen you needs to notice you. Whether one that already has keeps seeing you here is the
        // pair of verdicts right below.
        PlayerDiagramGUI.Verdict(seenStanding,
            seenStanding ? "Te nota parado" : "No te nota parado (fuera del rango base o del cono)");
        PlayerDiagramGUI.Verdict(seenCrouched,
            seenCrouched ? "Te nota agachado" : "No te nota agachado");

        DrawAdaptiveVerdicts(data, distance, withinCone);

        // The verdict that makes the two-band cone tunable at all: "te ve" stopped being one
        // question the moment detection got a ramp, and the answer that matters is HOW LONG.
        // Without this the designer can see that a position is inside the cone and has no way to
        // tell whether it means instant death or a second and a half of grace.
        if (seenStanding && data.HasPeripheralVision)
        {
            if (withinFocus)
            {
                PlayerDiagramGUI.Verdict(true, "Te ve <b>al instante</b> (dentro del cono de foco)");
            }
            else
            {
                PlayerDiagramGUI.Verdict(false,
                    $"Visi\u00f3n perif\u00e9rica: te nota reci\u00e9n a los " +
                    $"<b>{NoticeSeconds(data, distance):0.00} s</b> de exposici\u00f3n continua");
            }
        }
        // The third zone: behind it. Never a sighting — the verdict that matters is how long it takes
        // to turn round and look, from an empty meter, with nothing else going on.
        VisionZones.EZone zone = VisionZones.Classify(bearing, distance, data.ViewAngle, data.FocusAngle,
                                                      data.ViewRange, data.RearSenseRange);
        if (zone == VisionZones.EZone.Rear && !hardDetected && data.RearSenseStrength > 0f)
        {
            float crouchedRear = data.RearSenseRange * data.CrouchVisionMultiplier;
            string crouched = distance <= crouchedRear ? "" : " (agachado no te siente)";
            PlayerDiagramGUI.Verdict(false,
                $"Atrás: <b>siente que hay alguien</b> y se da vuelta a mirar a los " +
                $"<b>{TurnRoundSeconds(data, distance):0.0} s</b>{crouched}. Nunca es un avistamiento solo");
        }
        else if (bearing > data.ViewAngle * 0.5f && !hardDetected)
        {
            PlayerDiagramGUI.Verdict(true, "Atrás y fuera de la zona de atrás: no te siente");
        }

        PlayerDiagramGUI.Verdict(heard, heard ? "Te oye" : "No te oye");
        PlayerDiagramGUI.Verdict(hardDetected,
            hardDetected ? "Detección dura: te nota igual, sin importar cono ni escondite " +
                           "(medida en plano, desde el cuerpo, contra un jugador en su mismo piso)"
                         : "Fuera de la detección dura");
        PlayerDiagramGUI.Verdict(catchable,
            catchable ? "Dentro del alcance de atrapada (sólo importa si ya te está persiguiendo)"
                      : "Fuera de alcance de atrapada");

        EditorGUILayout.HelpBox(
            "Prueba en 2D, sin paredes ni pisos de por medio: no reproduce oclusión " +
            "(WallOcclusionMultiplier / FloorOcclusionMultiplier / ProximityDetectionRespectsWalls) " +
            "ni CatchMaxVerticalOffset, que es un eje aparte y gobierna las dos: la atrapada y la " +
            "detección dura se miden en plano desde el cuerpo, sólo contra un jugador en su mismo " +
            "piso. Para eso, con el Nemesis en escena, mirá los gizmos (NemesisGizmos) contra la " +
            "geometría real.",
            MessageType.Info);
    }

    /// <summary>
    /// The two verdicts the adaptive range adds (AdaptiveViewRange). The two above answer "does it
    /// NOTICE me here", which is still the base range and the only thing stealth is balanced on.
    /// These answer the other two questions a chase asks: at this distance does it KEEP seeing a
    /// player it already sees, and how long into a hunt before it can see them AGAIN.
    ///
    /// Through the rule's own function for the hunt (AdaptiveViewRange.SecondsToReach) and the SO's
    /// own derived ranges, for the reason NoticeSeconds gives: a tester that works the number out its
    /// own way is worse than no tester, because it is believed. Silent with both scales at 1 — then
    /// the range is the one number it always was, and the two verdicts above are the whole story.
    /// </summary>
    private static void DrawAdaptiveVerdicts(SO_NemesisData data, float distance, bool withinCone)
    {
        float hold = data.ViewHoldRange;
        float hunt = data.ViewHuntRange;

        const float Visible = 0.001f;
        if (hold <= data.ViewRange + Visible && hunt <= data.ViewRange + Visible) return;

        if (!withinCone)
        {
            PlayerDiagramGUI.Verdict(false,
                "Fuera del cono: ni sosteniéndote ni cazándote te ve desde este ángulo " +
                "(en la persecución la mirada gira hacia vos: ver Lost Sight Look Ahead)");
            return;
        }

        bool held = distance <= hold;
        PlayerDiagramGUI.Verdict(held,
            held ? $"Si ya te venía viendo, te SIGUE viendo acá (sostiene hasta {hold:0.#} m; " +
                   $"agachado, hasta {hold * data.CrouchVisionMultiplier:0.#} m)"
                 : $"Ni viéndote te sostiene tan lejos: te pierde al pasar los {hold:0.#} m");

        float seconds = AdaptiveViewRange.SecondsToReach(distance, data.ViewRange, data.ViewHuntScale,
                                                         data.ViewHuntGrowTime);

        if (float.IsPositiveInfinity(seconds))
        {
            PlayerDiagramGUI.Verdict(false,
                $"Cazándote tampoco vuelve a verte acá: el rango de caza llega hasta {hunt:0.#} m");
        }
        else if (seconds <= 0f)
        {
            PlayerDiagramGUI.Verdict(true,
                "Si te perdió y te caza, te vuelve a ver acá apenas te tenga en el cono y sin nada en el medio");
        }
        else
        {
            PlayerDiagramGUI.Verdict(true,
                $"Si te perdió y te caza, recién puede volver a verte acá a los {seconds:0.0} s de " +
                $"haberte perdido (crece hasta {hunt:0.#} m en {data.ViewHuntGrowTime:0.#} s)");
        }
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

    /// <summary>
    /// Seconds of continuous peripheral exposure before the Nemesis notices, at this distance.
    ///
    /// Through VisionZones, the same rate FieldOfView.TickAwareness integrates: a tester that computes
    /// the number its own way is worse than no tester, because it is believed. Ignores corroboration —
    /// with a fresh belief there, a glimpse is a sighting at once (GlimpseCorroborationWindow).
    /// </summary>
    private static float NoticeSeconds(SO_NemesisData data, float distance)
    {
        float rate = VisionZones.BuildRate(VisionZones.Closeness(distance, data.ViewRange),
                                           data.AwarenessBuildTime);

        return rate <= 0.0001f ? float.PositiveInfinity : 1f / rate;
    }

    /// <summary>Seconds of a presence felt behind it, from an empty meter and with nothing else, before
    /// the meter crosses the suspicion threshold and it turns round. Same rate as FieldOfView.</summary>
    private static float TurnRoundSeconds(SO_NemesisData data, float distance)
    {
        float rate = VisionZones.BuildRate(VisionZones.Closeness(distance, data.RearSenseRange),
                                           data.AwarenessBuildTime) * data.RearSenseStrength;

        return rate <= 0.0001f ? float.PositiveInfinity : data.AwarenessTriggerThreshold / rate;
    }

    // Perderlo de vista =======================================================================
    //
    // Every other picture on this asset is in metres. These three numbers are seconds, and what
    // they decide lasts about one: nobody can see it in game, only that the Nemesis "se quedó
    // parado" or "titila". So it is drawn as a timeline from the last sighting.

    private static readonly Color HeardColor = PlayerDiagramGUI.Standing;

    /// <summary>
    /// The chase without a sighting, as time: how long "todavía sabe dónde está" holds with nothing
    /// but the memory of the sighting, how long with the player's noise keeping the belief tight,
    /// and where Chase Hold Max Time cuts both. Under it, the route verdict: how long "it does not
    /// get there" has to stand before the ladder acts on it, against the path queries it is made of.
    /// </summary>
    private static void DrawLostSightTimeline(SO_NemesisData data)
    {
        float cap = data.ChaseHoldMaxTime;
        float blind = BlindHoldSeconds(data);
        float heard = HeardHoldSeconds(data);
        float settle = data.RouteVerdictSettleTime;
        float interval = Mathf.Max(0.05f, data.RouteVerdictInterval);

        float span = Mathf.Max(1.5f, cap * 1.35f, settle * 1.35f, interval * 3f);

        Rect canvas = PlayerDiagramGUI.Canvas(158f);
        float x0 = canvas.x + 4f;
        float x1 = canvas.xMax - 4f;
        float px = (x1 - x0) / span;

        // -- how long the chase holds --
        float y = canvas.y;
        PlayerDiagramGUI.Text(new Rect(x0, y, canvas.width, 13f),
            blind > 0f ? $"Sin oírte: sigue en Chasing {blind:0.##} s" : "Sin oírte: no aguanta nada",
            FocusColor);
        PlayerDiagramGUI.Box(new Rect(x0, y + 15f, Mathf.Max(1f, blind * px), 7f), FocusColor);

        y += 28f;
        PlayerDiagramGUI.Text(new Rect(x0, y, canvas.width, 13f),
            heard > blind + 0.005f
                ? $"Oyéndote cerca: hasta {heard:0.##} s"
                : "Oyéndote: no agrega nada con estos valores",
            HeardColor);
        PlayerDiagramGUI.Box(new Rect(x0, y + 15f, Mathf.Max(1f, heard * px), 7f), HeardColor);

        // The bound, across both rows, and what comes after it.
        if (cap > 0f)
        {
            float capX = x0 + cap * px;
            PlayerDiagramGUI.VLine(capX, canvas.y + 13f, y + 24f, PlayerDiagramGUI.Bad, 1.5f);
            PlayerDiagramGUI.Text(new Rect(capX + 5f, y + 10f, 150f, 13f), "tope → Searching",
                                  PlayerDiagramGUI.Bad);
        }

        y += 30f;
        DrawSecondsAxis(x0, x1, y, span, px, "s desde que dejó de verte");

        // -- how long a route verdict has to stand --
        y += 32f;
        bool settles = settle > 0f;
        PlayerDiagramGUI.Text(new Rect(x0, y, canvas.width, 13f),
            settles
                ? $"Veredicto de ruta: tiene que sostenerse {settle:0.##} s ({VerdictsToSettle(data)} consultas seguidas)"
                : "Veredicto de ruta: sin espera, vale la última consulta",
            PlayerDiagramGUI.Ink);

        Color settleColor = PlayerDiagramGUI.Accent;
        PlayerDiagramGUI.Box(new Rect(x0, y + 15f, Mathf.Max(1f, settle * px), 7f),
                             new Color(settleColor.r, settleColor.g, settleColor.b, 0.55f));

        // One tick per path query: the verdict cannot change more often than this.
        for (float t = 0f; t <= span + 0.0001f; t += interval)
            PlayerDiagramGUI.VLine(x0 + t * px, y + 12f, y + 25f, PlayerDiagramGUI.Ink, 1.5f);

        PlayerDiagramGUI.Text(new Rect(x0, y + 26f, canvas.width, 13f),
            $"cada marca es una consulta de camino ({interval:0.##} s)", PlayerDiagramGUI.Muted);
    }

    /// <summary>A time axis under a row of bars: a tick every half second, labelled.</summary>
    private static void DrawSecondsAxis(float x0, float x1, float y, float span, float px, string caption)
    {
        PlayerDiagramGUI.HLine(x0, x1, y, PlayerDiagramGUI.Floor, 1.5f);

        for (float t = 0f; t <= span + 0.0001f; t += 0.5f)
        {
            float x = x0 + t * px;
            PlayerDiagramGUI.VLine(x, y - 3f, y + 3f, PlayerDiagramGUI.Floor);
            PlayerDiagramGUI.Text(new Rect(x - 16f, y + 3f, 32f, 12f), t.ToString("0.#"),
                                  PlayerDiagramGUI.Muted, TextAnchor.MiddleCenter);
        }

        PlayerDiagramGUI.Text(new Rect(x1 - 170f, y - 15f, 170f, 12f), caption, PlayerDiagramGUI.Muted,
                              TextAnchor.MiddleRight);
    }

    /// <summary>
    /// Seconds the chase holds with no sighting and no sound: what the belief takes to grow from a
    /// sighting's radius to Chase Hold Radius, cut at Chase Hold Max Time. The same arithmetic the
    /// rung comes down to (NemesisBelief.Radius under the threshold, sight age under the bound).
    /// </summary>
    private static float BlindHoldSeconds(SO_NemesisData data)
    {
        if (data.ChaseHoldMaxTime <= 0f) return 0f;

        float room = data.ChaseHoldRadius - data.BeliefSightRadius;
        if (room <= 0f) return 0f;

        float grows = data.BeliefGrowthSpeed > 0.0001f ? room / data.BeliefGrowthSpeed : float.PositiveInfinity;
        return Mathf.Min(grows, data.ChaseHoldMaxTime);
    }

    /// <summary>The same with the player heard close by all the while: the bound itself, as long as
    /// a noise next to it is precise enough to keep the belief under the radius at all.</summary>
    private static float HeardHoldSeconds(SO_NemesisData data)
    {
        bool aCloseNoiseHolds = NemesisBelief.NoiseRadiusFor(data, 0f, false, false, false) < data.ChaseHoldRadius;
        return aCloseNoiseHolds ? Mathf.Max(BlindHoldSeconds(data), data.ChaseHoldMaxTime) : BlindHoldSeconds(data);
    }

    /// <summary>
    /// The farthest a noise of the player's can be heard from and still leave the belief under
    /// Chase Hold Radius, in metres: past it the ears are too vague to keep a chase going. Asked of
    /// NemesisBelief's own formula rather than solved here, so the two cannot drift.
    /// </summary>
    private static float HoldingNoiseDistance(SO_NemesisData data, bool wall, bool floor, bool hidingSpot)
    {
        float radius = data.ChaseHoldRadius;
        if (NemesisBelief.NoiseRadiusFor(data, 0f, wall, floor, hidingSpot) >= radius) return 0f;

        float near = 0f;
        float far = Mathf.Max(1f, data.ListenRange);
        if (NemesisBelief.NoiseRadiusFor(data, far, wall, floor, hidingSpot) < radius) return far;

        for (int i = 0; i < 20; i++)
        {
            float middle = (near + far) * 0.5f;
            if (NemesisBelief.NoiseRadiusFor(data, middle, wall, floor, hidingSpot) < radius) near = middle;
            else far = middle;
        }

        return near;
    }

    /// <summary>How many path queries in a row have to agree for the route verdict to change: each
    /// one stands for Route Verdict Interval.</summary>
    private static int VerdictsToSettle(SO_NemesisData data)
    {
        float interval = Mathf.Max(0.05f, data.RouteVerdictInterval);
        return Mathf.Max(1, Mathf.CeilToInt(data.RouteVerdictSettleTime / interval));
    }

    /// <summary>
    /// The chase's two holds (plan §19.4): what it still knows of the player once the eyes lose them,
    /// and a route verdict that has to stand. Both fail silently when tuned off: the Nemesis goes
    /// back to trading Chasing and Searching several times a second, and nothing says why.
    /// </summary>
    private static void DrawLostSightChecks(SO_NemesisData data)
    {
        float blind = BlindHoldSeconds(data);

        bool holds = blind > 0f;
        bool holdIsABlink = blind <= 1f;
        PlayerDiagramGUI.Verdict(holds && holdIsABlink,
            !holds
                ? "Chase Hold Radius no supera a Belief Sight Radius (o Chase Hold Max Time está en 0) — " +
                  "un solo barrido de vista que falle le pasa la persecución a la búsqueda, y el " +
                  "siguiente se la devuelve: el titileo entre Chasing y Searching"
                : holdIsABlink
                    ? $"Perderte de vista un instante no corta la persecución: sin oírte aguanta {blind:0.##} s, " +
                      $"lo que tarda su creencia en pasar de {data.BeliefSightRadius:0.##} a {data.ChaseHoldRadius:0.##} m"
                    : $"Sin verte ni oírte aguanta {blind:0.##} s — es más que un parpadeo: ese tiempo se " +
                      "queda parado en la esquina por la que te fuiste antes de empezar a buscar");

        if (holds)
        {
            float open = HoldingNoiseDistance(data, false, false, false);
            float wall = HoldingNoiseDistance(data, true, false, false);
            float hiding = HoldingNoiseDistance(data, false, false, true);

            EditorGUILayout.LabelField(
                open <= 0f
                    ? "Ningún ruido tuyo es tan preciso como para sostener la persecución: el oído no cuenta."
                    : $"El oído la sostiene, hasta {data.ChaseHoldMaxTime:0.##} s sin verte, si te oye a menos de " +
                      $"{open:0.#} m al aire libre, {wall:0.#} m a través de una pared o {hiding:0.#} m desde un " +
                      "escondite. Más lejos tu ruido es demasiado vago, y pasa a buscarte.",
                EditorStyles.wordWrappedMiniLabel);
        }

        float settle = data.RouteVerdictSettleTime;
        float interval = data.RouteVerdictInterval;
        bool filters = settle > interval;
        PlayerDiagramGUI.Verdict(filters,
            settle <= 0f
                ? "Route Verdict Settle Time en 0 — vale la última consulta de camino: una sola que falle " +
                  "corta la persecución con vos a la vista"
                : !filters
                    ? $"Route Verdict Settle Time ({settle:0.##} s) no supera a Route Verdict Interval " +
                      $"({interval:0.##} s) — cada consulta dura un intervalo entero, así que no filtra ninguna"
                    : $"Hacen falta {VerdictsToSettle(data)} consultas de camino seguidas para pasar de 'llega' a " +
                      $"'no llega' o al revés; una sola no alcanza. Tarda {settle:0.##} s en darse cuenta de que " +
                      "te subiste a un lugar al que no puede llegar");
    }

    // Chequeos ================================================================================

    private static void DrawChecks(SO_NemesisData data)
    {
        // Backed by FocusAngle's own tooltip. This one fails SILENTLY in game, which is why it
        // earns a check: with the focus cone as wide as the vision cone there is no peripheral
        // band, so the suspicion ramp, the "vio algo de reojo" rung and the whole gradual-detection
        // feature simply never fire - and nothing anywhere says so. The Nemesis just goes back to
        // spotting you instantly and it looks like the feature was never built.
        bool focusIsInner = data.HasPeripheralVision;
        PlayerDiagramGUI.Verdict(focusIsInner,
            focusIsInner
                ? $"Cono de foco ({data.FocusAngle:0.#}\u00b0) dentro del de visi\u00f3n ({data.ViewAngle:0.#}\u00b0): " +
                  $"quedan {(data.ViewAngle - data.FocusAngle) * 0.5f:0.#}\u00b0 de perif\u00e9rica a cada lado"
                : $"Cono de foco ({data.FocusAngle:0.#}\u00b0) igual o m\u00e1s ancho que el de visi\u00f3n " +
                  $"({data.ViewAngle:0.#}\u00b0) \u2014 no hay visi\u00f3n perif\u00e9rica: todo vuelve a ser " +
                  $"detecci\u00f3n instant\u00e1nea y la regla 'vio algo de reojo' no se dispara nunca");

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

        DrawAdaptiveVisionChecks(data);

        if (data.WallOcclusionEnabled)
        {
            EditorGUILayout.LabelField(
                $"Te oye a través de una pared hasta {data.ListenRange * data.WallOcclusionMultiplier:0.##} m, " +
                $"y a través de un piso hasta {data.ListenRange * data.FloorOcclusionMultiplier:0.##} m.",
                EditorStyles.wordWrappedMiniLabel);
        }

        DrawChaseProgressChecks(data);
        DrawLostSightChecks(data);
        DrawSearchChecks(data);
    }

    /// <summary>
    /// The range that adapts and the gaze of the chase (AdaptiveViewRange, ChaseGaze; 04/10). The
    /// derived distances first — the scales are multipliers and nobody pictures "×2" — and then the
    /// two settings that switch a half of it off without anything in game saying so: a hunt that
    /// regrows instantly (breaking line of sight buys the player nothing), and a look-ahead of 0
    /// (the cone is welded to the body again and it arrives at every corner facing the wall).
    /// </summary>
    private static void DrawAdaptiveVisionChecks(SO_NemesisData data)
    {
        bool holds = data.ViewHoldScale > 1f;
        bool hunts = data.ViewHuntScale > 1f;

        if (!holds && !hunts)
        {
            EditorGUILayout.LabelField(
                "View Hold Scale y View Hunt Scale en 1: el rango de visión es uno solo, como antes " +
                "del 04/10. Te nota y te pierde a la misma distancia.",
                EditorStyles.wordWrappedMiniLabel);
        }
        else
        {
            float crouch = data.CrouchVisionMultiplier;
            EditorGUILayout.LabelField(
                $"Te NOTA a {data.ViewRange:0.#} m (patrullando, o yendo a un ruido). Viéndote te " +
                $"sostiene hasta {data.ViewHoldRange:0.#} m (agachado {data.ViewHoldRange * crouch:0.#} m). " +
                $"Si te pierde y te caza, vuelve a verte hasta {data.ViewHuntRange:0.#} m (agachado " +
                $"{data.ViewHuntRange * crouch:0.#} m), y llega a ese rango a los " +
                $"{data.ViewHuntGrowTime:0.#} s de perderte. Se multiplica encima de la escalada y del Director.",
                EditorStyles.wordWrappedMiniLabel);
        }

        if (hunts)
        {
            bool breakingSightPays = data.ViewHuntGrowTime > 0f;
            PlayerDiagramGUI.Verdict(breakingSightPays,
                breakingSightPays
                    ? $"Romper la línea de vista le devuelve el rango a {data.ViewRange:0.#} m y tarda " +
                      $"{data.ViewHuntGrowTime:0.#} s en recuperar el de caza: esa es tu ventana"
                    : "View Hunt Grow Time en 0 — apenas te pierde ya te busca con el rango máximo: " +
                      "romper la línea de vista no te da ninguna ventana");
        }

        bool looksWhereYouWent = data.LostSightLookAhead > 0f;
        PlayerDiagramGUI.Verdict(looksWhereYouWent,
            looksWhereYouWent
                ? $"En la persecución la mirada te sigue; si te pierde, mira {data.LostSightLookAhead:0.#} m " +
                  $"más allá de donde te perdió, en tu rumbo, girando a {data.GazeTurnSpeed:0} °/s"
                : "Lost Sight Look Ahead en 0 — la mirada queda pegada al cuerpo en la persecución: " +
                  "llega a la esquina mirando la pared, con el pasillo por el que te fuiste fuera del cono");
    }

    /// <summary>
    /// Plan-Busqueda-Nemesis Fases 1 and 2: the ear's error and the possibility map. The checks are the
    /// relationships the tooltips assert and nothing in game would ever flag: a map that clears farther
    /// than the Nemesis sees, a "precise" noise vaguer than a footstep heard next to it.
    /// </summary>
    private static void DrawSearchChecks(SO_NemesisData data)
    {
        // The rear zone is measured from the body like the hard detection, so a range at or under it
        // is a zone that can never fire: the proximity rule has already caught anyone that close.
        if (data.RearSenseRange > 0f && data.RearSenseStrength > 0f)
        {
            bool rearReaches = data.RearSenseRange > data.ProximityDetectionRange;
            PlayerDiagramGUI.Verdict(rearReaches,
                rearReaches
                    ? $"Atrás siente de {data.ProximityDetectionRange:0.##} a {data.RearSenseRange:0.##} m " +
                      $"(agachado, hasta {data.RearSenseRange * data.CrouchVisionMultiplier:0.##} m), " +
                      $"a ×{data.RearSenseStrength:0.##} de lo que pesa un vistazo"
                    : $"Rear Sense Range ({data.RearSenseRange:0.##} m) no pasa la detección dura " +
                      $"({data.ProximityDetectionRange:0.##} m): la zona de atrás no agrega nada");
        }

        bool rearCapped = data.NoiseOnlySuspicionCap < 1f;
        PlayerDiagramGUI.Verdict(rearCapped,
            rearCapped
                ? "Lo que siente atrás y los ruidos suaves nunca llegan solos a avistamiento"
                : "Noise Only Suspicion Cap en 1: un ruido suave o algo atrás pueden volverse avistamiento sin verte");

        EditorGUILayout.LabelField(
            data.GlimpseCorroborationWindow > 0f
                ? $"Si te sintió hace menos de {data.GlimpseCorroborationWindow:0.#} s, un vistazo de reojo dentro " +
                  "de la creencia es verte: pasa directo a perseguir."
                : "Glimpse Corroboration Window en 0: la periferia siempre arranca de cero, aunque te esté buscando.",
            EditorStyles.wordWrappedMiniLabel);

        // The ear right beside the Nemesis: the radius of a footstep at 1 m, open air.
        float besideRadius = data.BeliefNoiseBaseRadius + data.BeliefNoiseRadiusPerMetre;
        float besideError = besideRadius * data.HearingLocalizationError;
        float tenMetres = (data.BeliefNoiseBaseRadius + data.BeliefNoiseRadiusPerMetre * 10f) *
                          data.BeliefNoiseWallFactor;

        bool precisePossible = data.SearchPreciseNoiseRadius >= besideRadius;
        PlayerDiagramGUI.Verdict(precisePossible,
            precisePossible
                ? $"Un paso a 1 m (radio {besideRadius:0.##} m) cuenta como ruido preciso: la búsqueda puede " +
                  "pararse en el punto mismo"
                : $"Search Precise Noise Radius ({data.SearchPreciseNoiseRadius:0.##} m) por debajo del radio de un " +
                  $"paso a 1 m ({besideRadius:0.##} m) — ningún ruido es preciso: nunca se para en el punto de un " +
                  "ruido, ni siquiera al lado suyo (busca la zona, como con cualquier ruido vago)");

        EditorGUILayout.LabelField(
            $"El oído se equivoca hasta ±{besideError:0.##} m a 1 m, y hasta " +
            $"±{tenMetres * data.HearingLocalizationError:0.##} m a 10 m a través de una pared " +
            $"(×{data.HearingLocalizationError:0.##} del radio de la evidencia).",
            EditorStyles.wordWrappedMiniLabel);

        // The clearing follows the range the eyes are really using (07/10, WIR-062): the base one,
        // stretched while it holds the player in sight or hunts one it lost.
        float scale = data.SearchMapClearRangeScale;
        bool clearInsideView = scale <= 1f;
        PlayerDiagramGUI.Verdict(clearInsideView,
            clearInsideView
                ? $"El mapa limpia hasta {data.SearchMapClearRange:0.##} m, dentro de lo que ve ({data.ViewRange:0.##} m); " +
                  $"cazándote, hasta {data.ViewHuntRange * scale:0.##} m (ve hasta {data.ViewHuntRange:0.##} m)"
                : $"El mapa limpia hasta {data.SearchMapClearRange:0.##} m, más lejos de lo que ve " +
                  $"({data.ViewRange:0.##} m) — descarta lugares donde no te podría ver: hace trampa por eliminación");

        bool spreadsAtPlayerSpeed = data.SearchMapSpreadSpeed >= data.BeliefGrowthSpeed * 0.75f;
        PlayerDiagramGUI.Verdict(spreadsAtPlayerSpeed,
            spreadsAtPlayerSpeed
                ? $"El valor corre a {data.SearchMapSpreadSpeed:0.#} m/s (vos corriendo: {data.BeliefGrowthSpeed:0.#} m/s)"
                : $"El valor corre a {data.SearchMapSpreadSpeed:0.#} m/s, bastante menos que vos corriendo " +
                  $"({data.BeliefGrowthSpeed:0.#} m/s) — el mapa se queda atrás y busca donde ya no podés estar");

        DrawSearchPickChecks(data);
    }

    /// <summary>
    /// Plan-Busqueda-Nemesis Fase 2b: the search goes where the possibility map holds value. Two
    /// relationships that fail silently — a zone smaller than one node gathers nothing, and one wider
    /// than what a look clears can never be looked at whole, so the search keeps choosing it — and the
    /// two thresholds turned into what they mean in seconds and percentages, which is the only way
    /// "0.015" can be tuned.
    /// </summary>
    private static void DrawSearchPickChecks(SO_NemesisData data)
    {
        // Fails silently at 1: the search still sets off at once, just as often sideways as after
        // the player, and it reads as "no predice que seguís para adelante".
        float boost = data.SearchMapChaseHeadingBoost;
        bool leans = boost > 1f;
        PlayerDiagramGUI.Verdict(leans,
            leans
                ? $"Al perderte en una persecución, el primer lugar que busca tira hacia donde ibas: los que " +
                  $"quedan en tu rumbo pesan ×{boost:0.#} en el sorteo, los del costado ×1 y los de atrás " +
                  $"×{1f / boost:0.##}"
                : "Search Map Chase Heading Boost en 1 — el primer lugar se sortea sin mirar hacia dónde " +
                  "ibas: al doblar una esquina sale a buscar para el costado tan seguido como detrás tuyo");

        float zone = data.SearchMapZoneRadius;

        bool zoneGathers = zone >= data.SearchMapNodeSpacing;
        PlayerDiagramGUI.Verdict(zoneGathers,
            zoneGathers
                ? $"Un lugar de la búsqueda (radio {zone:0.##} m) junta un nodo y sus vecinos " +
                  $"(nodos cada {data.SearchMapNodeSpacing:0.##} m)"
                : $"Search Map Zone Radius ({zone:0.##} m) por debajo de la separación de nodos " +
                  $"({data.SearchMapNodeSpacing:0.##} m) — cada lugar es un nodo suelto: la búsqueda " +
                  "camina al borde de una mancha de valor igual que al medio");

        // It stops a stride short of the middle of the zone and looks: the far side of the zone has to
        // be inside what that look clears, or part of it keeps its value however long it stares.
        const float ArrivalSlack = 1f;
        bool zoneSeenWhole = zone + ArrivalSlack <= data.SearchMapClearRange;
        PlayerDiagramGUI.Verdict(zoneSeenWhole,
            zoneSeenWhole
                ? $"Al llegar a un lugar lo ve entero: limpia hasta {data.SearchMapClearRange:0.##} m"
                : $"Search Map Zone Radius ({zone:0.##} m) no entra en lo que limpia mirando " +
                  $"({data.SearchMapClearRange:0.##} m) — llega, mira, y parte del lugar sigue con valor: " +
                  "lo vuelve a elegir");

        float threshold = data.SearchMapWorthThreshold;
        EditorGUILayout.LabelField(
            threshold > 0f
                ? $"\"Revisé todo\" con el umbral en {threshold:0.###}: un lugar con 10 % del valor vale la " +
                  $"caminata hasta {Mathf.Max(0f, 0.1f / threshold - 1f):0.#} s de distancia; uno con 30 %, hasta " +
                  $"{Mathf.Max(0f, 0.3f / threshold - 1f):0.#} s; pegado a él, hace falta al menos {threshold:P1}. " +
                  "Si ninguno llega, mira alrededor donde está y la búsqueda termina pasado Search Min Time " +
                  $"({data.SearchMinTime:0.#} s). Recién oído o visto (y al salir de una persecución o del " +
                  $"montacargas) la caminata no descarta: alcanza con que el lugar tenga {threshold:P1} del valor."
                : "Search Map Worth Threshold en 0: nunca da por revisado todo. La búsqueda termina solo por " +
                  "silencio (o porque más de la mitad del valor se fue al Hub).",
            EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.LabelField(
            data.SearchMapRepickShare > 0f
                ? $"Deja de caminar a un lugar cuando le queda menos del {data.SearchMapRepickShare:P0} del valor " +
                  "con el que lo eligió (lo vio vacío de lejos, o evidencia nueva movió el valor), y elige otro " +
                  $"entre los {data.SearchMapCandidates} de más valor."
                : "Search Map Repick Share en 0: camina siempre hasta el lugar que eligió, aunque de lejos ya " +
                  "lo haya visto vacío.",
            EditorStyles.wordWrappedMiniLabel);

        DrawTrailChecks(data);
    }

    /// <summary>
    /// The trail (07/10, WIR-062: "descartar la salida que él mismo estaba tapando"). Two ways it
    /// fails silently: switched off, the search goes back the way it came as soon as the value leaks
    /// past it; narrower than the node spacing, the value slips past it between nodes and the gizmo
    /// still draws a trail.
    /// </summary>
    private static void DrawTrailChecks(SO_NemesisData data)
    {
        float memory = data.SearchMapTrailMemory;
        float radius = data.SearchMapTrailRadius;
        bool on = memory > 0f && radius > 0f;

        PlayerDiagramGUI.Verdict(on,
            on
                ? $"Rastro: por donde caminó cazándote en los últimos {memory:0.#} s el valor no vuelve — la " +
                  "búsqueda no sale para atrás por donde vino"
                : "Rastro apagado (Search Map Trail Memory o Radius en 0) — el valor puede volver por detrás suyo " +
                  "y la búsqueda sale para atrás, por donde vino él (WIR-062)");

        if (!on) return;

        bool spansNodes = radius >= data.SearchMapNodeSpacing;
        PlayerDiagramGUI.Verdict(spansNodes,
            spansNodes
                ? $"El rastro tapa una franja de {radius * 2f:0.#} m de ancho: en un pasillo así de angosto nadie " +
                  "pasa por al lado suyo"
                : $"Search Map Trail Radius ({radius:0.##} m) por debajo de la separación de nodos " +
                  $"({data.SearchMapNodeSpacing:0.##} m) — el valor se cuela entre nodos por el costado del rastro");

        EditorGUILayout.LabelField(
            "Lo que te oye o te ve cerca del rastro borra ese tramo: si te escuchó detrás, busca detrás. " +
            "En Patrolling no deja rastro.",
            EditorStyles.wordWrappedMiniLabel);
    }

    /// <summary>
    /// The loop-round-a-table counterplay (NemesisChaseProgress + NemesisPursuit). Every check here
    /// is one that fails SILENTLY in game: the stall still gets detected and logged, the HUD still
    /// says "estancado", and the Nemesis goes on tail-chasing exactly as before — so from the
    /// outside it looks like the counterplay does not work, when it was simply tuned off.
    /// </summary>
    private static void DrawChaseProgressChecks(SO_NemesisData data)
    {
        // Backed by ChaseTrailPenalty's own tooltip: 1 is "off", and nothing else says so.
        bool penaltyActs = data.ChaseTrailPenalty < 1f;
        PlayerDiagramGUI.Verdict(penaltyActs,
            penaltyActs
                ? $"Estancado, los waypoints sobre el rastro pesan ×{data.ChaseTrailPenalty:0.##}: " +
                  "la ruta tiende a salir por el otro lado"
                : "Chase Trail Penalty en 1 — la penalización del rastro está apagada: " +
                  "detecta el loop pero lo sigue persiguiendo por atrás");

        // Backed by ChaseStagnantDetourTolerance's own tooltip: the code takes the larger of the
        // two, so a stagnant value at or below the normal one means the budget never widens.
        bool toleranceWidens = data.ChaseStagnantDetourTolerance > data.ChaseDetourTolerance;
        PlayerDiagramGUI.Verdict(toleranceWidens,
            toleranceWidens
                ? $"Estancado acepta desvíos de hasta ×{data.ChaseStagnantDetourTolerance:0.##} " +
                  $"(normal ×{data.ChaseDetourTolerance:0.##})"
                : $"Chase Stagnant Detour Tolerance ({data.ChaseStagnantDetourTolerance:0.##}) no supera " +
                  $"a la normal ({data.ChaseDetourTolerance:0.##}) — estancado no amplía nada, y el " +
                  "otro lado del obstáculo casi nunca entra en el presupuesto");

        // Backed by ChaseTrailPenaltyRadius's own tooltip. BeliefTraceRadius is documented as
        // roughly the spacing between neighbouring waypoints, so a trail radius well past it
        // reaches the waypoints on the FAR side of anything table-sized too — and a penalty on
        // both sides is no preference at all.
        bool radiusTight = data.ChaseTrailPenaltyRadius <= data.BeliefTraceRadius * 2f;
        PlayerDiagramGUI.Verdict(radiusTight,
            radiusTight
                ? $"Radio del rastro ({data.ChaseTrailPenaltyRadius:0.##} m) del orden de la " +
                  $"separación entre waypoints ({data.BeliefTraceRadius:0.##} m)"
                : $"Radio del rastro ({data.ChaseTrailPenaltyRadius:0.##} m) mucho mayor que la " +
                  $"separación entre waypoints ({data.BeliefTraceRadius:0.##} m) — marca los " +
                  "dos lados de un obstáculo chico y ya no queda 'otro lado' que elegir");

        // The number the two window knobs boil down to. Worth seeing as a rate because that is
        // what it gets compared against in your head — a chase speed and a sprint speed — and
        // neither of those lives on this asset, so they are left to the reader rather than
        // hard-coded here to drift.
        float closingRate = data.ChaseMinProgress / Mathf.Max(0.01f, data.ChaseProgressWindow);
        EditorGUILayout.LabelField(
            $"Para no estancarse tiene que acortar {data.ChaseMinProgress:0.##} m (por NavMesh) cada " +
            $"{data.ChaseProgressWindow:0.#} s: {closingRate:0.##} m/s de promedio. Se mide solo en " +
            $"Chasing y mientras lo vio hace menos de {data.VisionLossGracePeriod:0.#} s " +
            "(Vision Loss Grace Period). Sin vista la ventana se pausa (no avanza ni se juzga).",
            EditorStyles.wordWrappedMiniLabel);

        // Backed by ChasePathJumpDistance's own tooltip: a step the size of the progress the
        // window asks for would turn an ordinary chase into "the path changed", and the detector
        // would re-baseline instead of ever judging. The sample interval is the one the path
        // query is throttled by (RouteVerdictInterval): the step is metres PER SAMPLE.
        bool jumpClearsProgress = data.ChasePathJumpDistance > data.ChaseMinProgress;
        PlayerDiagramGUI.Verdict(jumpClearsProgress,
            jumpClearsProgress
                ? $"Un salto de más de {data.ChasePathJumpDistance:0.##} m de NavMesh entre dos " +
                  $"mediciones (cada {data.RouteVerdictInterval:0.##} s) se toma por cambio de camino " +
                  "y no se juzga"
                : $"Chase Path Jump Distance ({data.ChasePathJumpDistance:0.##} m) no supera a " +
                  $"Chase Min Progress ({data.ChaseMinProgress:0.##} m) — cualquier medición normal " +
                  "se tomaría por salto y la ventana nunca llegaría a juzgar");

        // The counting rule, said once where the knobs are: it is the part of this block that no
        // number on screen shows. 0 on the regroup time is a legal value with a real effect
        // (every exit from Chasing starts a new count), so it is said, not hidden.
        string regroup = data.ChaseStallRegroupTime > 0f
            ? $"una persecución cuenta como otra recién tras {data.ChaseStallRegroupTime:0.#} s fuera de Chasing (o al agarrarte)"
            : "Chase Stall Regroup Time en 0: cada vez que sale de Chasing empieza una persecución nueva";
        EditorGUILayout.LabelField(
            "A los hábitos llega UN ChaseStalled por persecución, el de la primera ventana sin " +
            $"progreso, por larga que sea; {regroup}. El estancamiento en sí (lo que lee el " +
            "pursuit) dura todas las ventanas.",
            EditorStyles.wordWrappedMiniLabel);
    }
}
#endif
