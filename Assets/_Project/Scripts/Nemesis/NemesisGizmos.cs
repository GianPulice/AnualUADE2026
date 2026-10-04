using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws every tuning range on <see cref="SO_NemesisData"/> in the Scene view.
///
/// It exists because the inspector says "view range 5" and nothing in the world says how far 5 is.
/// Tuning the Nemesis by typing numbers and then playing to find out is how it ended up seeing
/// two metres while crouched — a value nobody chose on purpose, they just could not see it.
///
/// Three deliberate choices:
///
///   - <b>OnDrawGizmos, not OnDrawGizmosSelected.</b> Selected-only gizmos are invisible in Prefab
///     Mode unless you click the root, which is exactly where this is most useful. Everything is
///     behind per-block toggles instead, so the cost of always drawing is a checkbox — plus a
///     master <c>drawGizmos</c> switch, because "always on" is right while tuning detection and
///     wrong while dressing the level, and turning a dozen checkboxes off one at a time is not a
///     workflow anyone repeats twice.
///   - <b>Every value is read from the ScriptableObject</b>, through NemesisStateManager, never
///     from a local copy. A gizmo with its own serialised radius drifts from the value the game
///     actually uses, and then it is worse than no gizmo at all.
///   - <b>Nothing is cached in Awake.</b> The Scene view draws outside Play mode, where Awake has
///     not run, so every lookup goes through the serialised references — which are populated in
///     the prefab.
///
/// Colour follows the project's visual language: red is danger and is used ONLY for the capture
/// reach, amber is the alert/vision band, cool blue is passive sensing. See docs/Materials-System.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(NemesisStateManager))]
public class NemesisGizmos : MonoBehaviour
{
    [Header("Master switch")]
    [Tooltip("Turns every block below off in one click, without you having to remember which ones " +
             "were on. It also switches off the PATROL ROUTE gizmos on the NemesisRoute objects — " +
             "those are the bulk of what is on screen, so clearing everything else and leaving " +
             "them would not have cleared much.\n\n" +
             "This component draws from OnDrawGizmos rather than OnDrawGizmosSelected on purpose " +
             "(see the class summary), so with a Nemesis in the scene its rings are always on " +
             "screen — which is what you want while tuning detection and squarely in the way while " +
             "dressing the level or framing a shot. Prefer this over disabling the component: the " +
             "component also has to be enabled for the checkboxes to mean anything the next time " +
             "you come back, and a disabled component reads as 'this is broken' to whoever finds " +
             "it next.")]
    [SerializeField] private bool drawGizmos = true;

    /// <summary>
    /// Whether Nemesis gizmos are being drawn at all, readable by components that draw their own
    /// and sit on OTHER GameObjects — <see cref="NemesisRoute"/>, which is the whole reason this
    /// is not just a private field.
    ///
    /// WHY A STATIC AND NOT A REFERENCE. The routes are their own objects, scattered through the
    /// level, and they draw from their own OnDrawGizmos. For the switch on the Nemesis to reach
    /// them, either they look the Nemesis up — FindObjectOfType per route per repaint, which is
    /// the expensive answer to a checkbox — or the Nemesis publishes the answer once. This is the
    /// same trade NemesisNav.AreaMask makes, and it carries the same caveat: with two
    /// NemesisGizmos in a scene the last one to draw wins. The design has one Nemesis.
    ///
    /// DEFAULTS TRUE AND IS RESTORED ON DISABLE, which is the part that keeps it from becoming a
    /// trap. A static that only ever gets written by a component can outlive it: switch the gizmos
    /// off, delete the Nemesis, and every route in the level is invisible with no checkbox
    /// anywhere to bring it back. Releasing the override in OnDisable means the routes draw
    /// whenever nothing is actively suppressing them.
    ///
    /// No RuntimeInitializeOnLoadMethod reset, unlike the other statics in this project. Those
    /// accumulate real state that a stale copy would corrupt; this one is re-asserted from the
    /// serialised field on the very next repaint, so it cannot survive being wrong.
    /// </summary>
    public static bool DrawingEnabled { get; private set; } = true;

    [Header("Vision")]
    [Tooltip("Vision cone at full range, with the real ViewAngle. An arc and two edges rather " +
             "than a sphere: the angle is half the information and a sphere throws it away.")]
    [SerializeField] private bool drawVisionCone = true;

    [Tooltip("The same cone shortened by CrouchVisionMultiplier — how close you can get while " +
             "crouched. Usually far smaller than anyone expects.")]
    [SerializeField] private bool drawCrouchedVisionCone = true;

    [Tooltip("The same cone shortened by UnderTableVisionMultiplier — how far it can make out a " +
             "player hiding under a table. Suspicion meter only, never an instant sighting, and a " +
             "full meter marks the spot as known instead of starting a chase (see " +
             "FieldOfView.SenseThroughSpot). A locker's own fraction lives on SO_HidingData and is " +
             "drawn round the spot itself, under 'Hiding spots' below, not here.")]
    [SerializeField] private bool drawUnderTableVisionCone = true;

    [Tooltip("Inner cone (FocusAngle): where detection is INSTANT. Everything between it and the " +
             "outer cone is peripheral vision, where the Nemesis only builds suspicion instead of " +
             "spotting you outright. Drawn nested inside the vision cone, so the gap between the " +
             "two arcs IS the peripheral band.")]
    [SerializeField] private bool drawFocusCone = true;

    [Tooltip("Hard detection radius: inside it you are seen with no cone and no hiding. Measured " +
             "flat from the body and only against a player on its own floor (within " +
             "CatchMaxVerticalOffset), so it is drawn at the feet, where it applies — not at the " +
             "eye.")]
    [SerializeField] private bool drawProximityDetection = true;

    [Tooltip("The third zone: behind it, outside the vision cone, out to RearSenseRange (and, faint, " +
             "the crouched reach). 'Siento que hay alguien atrás': it only fills the suspicion meter, " +
             "slowly, and never makes a sighting on its own. Drawn at the feet, facing away from where " +
             "it is looking.")]
    [SerializeField] private bool drawRearSense = true;

    [Tooltip("How far the eyes REALLY reach, next to the base cone (AdaptiveViewRange). Faint: the two " +
             "ceilings — how far it holds a player it is already seeing (View Hold Scale) and how far " +
             "a hunt lets it see again one it lost (View Hunt Scale). In Play mode, brighter: the " +
             "range it is using right now, with why (sostiene / caza / vuelve a la base) — nothing " +
             "extra is drawn while it is on the base range. Also, while it looks the way a lost " +
             "player went: a line from the spot where it lost them to the point its eyes are aimed " +
             "at (Lost Sight Look Ahead). The cones themselves already turn with the gaze.")]
    [SerializeField] private bool drawAdaptiveVision = true;

    [Header("Hearing")]
    [Tooltip("Hearing radius at full strength. The wall and floor multipliers are drawn as inner " +
             "rings, since those are the ranges that actually apply most of the time.")]
    [SerializeField] private bool drawHearing = true;

    [Tooltip("For a couple of seconds after each noise of the player's: a line from where they really " +
             "were to where the ear placed them, and round that the longest error it could have put " +
             "on it (Plan-Busqueda-Nemesis Fase 1, HearingLocalizationError). Editor and Play mode " +
             "only: the real position never leaves the sensor in a build. Needs Hearing on.")]
    [SerializeField] private bool drawHearingError = true;

    [Header("Capture")]
    [Tooltip("Where the grab can happen: CatchMaxReach horizontally by CatchMaxVerticalOffset " +
             "vertically. The only thing drawn in red.")]
    [SerializeField] private bool drawCatchReach = true;

    [Header("Search & feedback")]
    [Tooltip("Radius of the last-resort scatter the Searching state runs around where it started " +
             "when the possibility map holds no value at all — no belief to seed it " +
             "(SearchSweepRadius).")]
    [SerializeField] private bool drawSearchSweep = true;

    [Tooltip("Where the search is going and why (Plan-Busqueda-Nemesis Fase 2b): a line to the place " +
             "it chose off the possibility map, the zone around it (Search Map Zone Radius) with the " +
             "share of the value it was picked for and what it holds now, and — fainter — the other " +
             "places the last roll weighed, each with its share and the seconds to walk there. A " +
             "place marked 'no vale' was under the worth threshold and took no part in the roll. " +
             "Cheap, so it stays on with the heat map off. Play mode only, while it searches.\n\n" +
             "It took the place of the old sweep disc, which is why the field keeps its saved value.")]
    [UnityEngine.Serialization.FormerlySerializedAs("drawRoomSweep")]
    [SerializeField] private bool drawSearchPick = true;

    [Tooltip("The possibility map (Plan-Busqueda-Nemesis Fase 2): how possible it thinks it is that " +
             "you are on each patch of NavMesh, as heat — faint purple is 'could be', warm orange is " +
             "'most likely' — with a line to the likeliest place and its share. Hiding spots holding " +
             "value get a box. Also the cone it clears ('acá no está': SearchMapClearRange × its view " +
             "angle), and, while it searches, the place it chose (the same drawing as Draw Search " +
             "Pick). Play mode only. Off by default: it is a few thousand cubes per repaint on a " +
             "big level.\n\n" +
             "What to look for in a corridor with one exit: the heat must never appear BEHIND the " +
             "Nemesis, and the line to the chosen place must run ahead of it.")]
    [SerializeField] private bool drawPossibilityMap = false;

    [Tooltip("ProximityRadius — the HUD vignette only. Detects nothing.")]
    [SerializeField] private bool drawProximityVignette = false;

    [Header("Hiding spots")]
    [Tooltip("The spot it KNOWS the player is in (orange) or only SUSPECTS (blue): a line to the " +
             "approach point it walks to, and round the interior the range it can still make the " +
             "player out at — through the slats (a half disc, the door side only) or under the " +
             "table (all round). No ring means sealed. Play mode only: there is nothing to know " +
             "outside it.")]
    [SerializeField] private bool drawHidingKnowledge = true;

    [Header("Chase")]
    [Tooltip("While chasing: a ring of Chase Trail Penalty Radius around every waypoint on the " +
             "sensed trail (where the player was sensed passing). Those are the detour waypoints " +
             "the pursuit marks down once the chase stalls, so what is left outside the rings is " +
             "'the other way round'. Faint while measuring, solid once stalled. Play mode only.")]
    [SerializeField] private bool drawChaseTrail = true;

    [Header("Style")]
    [Tooltip("Segments per arc. Higher is smoother and costs nothing outside Play mode.")]
    [SerializeField, Range(8, 64)] private int arcSegments = 28;

    [Tooltip("Mark each range with a tick and its distance in metres. Turn off when several " +
             "Nemeses overlap and the text stacks up.")]
    [SerializeField] private bool drawLabels = true;

    // Palette. Red is reserved for danger by the visual language spec, so only the capture reach
    // gets it — a vision cone drawn red would read as "this is the kill zone", which it is not.
    private static readonly Color VisionColor    = new Color(1f, 0.784f, 0.314f);
    private static readonly Color CrouchColor    = new Color(0.55f, 0.75f, 0.45f);
    // Teal, and nothing else in the palette: it nests between the crouched and the full cone, so
    // it has to read against both green and amber at a glance. SO_NemesisDataEditor uses the same.
    private static readonly Color UnderTableColor = new Color(0.35f, 0.82f, 0.80f);
    private static readonly Color HearingColor   = new Color(0.541f, 0.706f, 0.831f);
    private static readonly Color HardDetectColor = new Color(0.95f, 0.55f, 0.25f);
    private static readonly Color CatchColor     = new Color(0.8f, 0.10f, 0.10f);
    private static readonly Color SearchColor    = new Color(0.65f, 0.55f, 0.85f);
    private static readonly Color VignetteColor  = new Color(0.45f, 0.45f, 0.50f);
    // Passive and faint: behind it is the weakest of the three zones, and must not read as a cone.
    private static readonly Color RearColor      = new Color(0.62f, 0.58f, 0.78f);
    // The vision amber, lighter: the range it is really seeing with sits outside the base cone and
    // has to read as the same sense reaching further, not as a new one.
    private static readonly Color AdaptiveVisionColor = new Color(1f, 0.92f, 0.62f);
    // The search's purple, paler: the cone the possibility map clears sits on top of the vision cone
    // and has to read as a different thing.
    private static readonly Color MapClearColor  = new Color(0.80f, 0.75f, 0.95f, 0.6f);

    private NemesisStateManager StateManager => GetComponent<NemesisStateManager>();

    /// <summary>Publishes the switch the moment the checkbox is clicked, rather than leaving the
    /// routes waiting for this component's next repaint to notice.</summary>
    private void OnValidate() => DrawingEnabled = drawGizmos;

    /// <summary>Releases the override so nothing stays suppressed by a component that is no longer
    /// drawing. See <see cref="DrawingEnabled"/>.</summary>
    private void OnDisable() => DrawingEnabled = true;

    private void OnDrawGizmos()
    {
        // Published BEFORE the early-out, and that order is the whole mechanism. Returning first
        // would mean the one state worth broadcasting — "gizmos are off" — is the one state that
        // never gets broadcast, and the routes would keep drawing forever.
        DrawingEnabled = drawGizmos;

        // Checked before anything else, including the component lookups below: the whole point of
        // the switch is that a scene with it off pays nothing for this component at all.
        if (!drawGizmos) return;

        NemesisStateManager manager = StateManager;
        if (manager == null) return;

        SO_NemesisData data = manager.NemesisData;
        if (data == null) return;   // Reported as an error by the state manager itself.

        FieldOfView view = manager.FieldOfView;

        // Eye height when the sensor is wired, this object's pivot otherwise. Drawing the cone
        // from the pivot when the sweep runs from the eye would be a lie in the one dimension
        // people are trying to check.
        Transform eye = view != null ? view.ViewTransform : transform;

        DrawName();
        DrawVision(data, eye);
        DrawHearing(data, manager);
        DrawCatch(data);
        DrawSearchAndVignette(data);
        if (drawPossibilityMap) DrawPossibilityMap(data, manager, eye);
        if (drawSearchPick || drawPossibilityMap) DrawSearchPick(data, manager);
        if (drawHidingKnowledge) DrawHidingKnowledge(manager);
        DrawPursuit();
        if (drawChaseTrail) DrawChaseTrail(data);
    }

    /// <summary>
    /// The sensed trail as the stalled-chase counterplay reads it, with the penalty radius around
    /// each stamped waypoint.
    ///
    /// Same argument as DrawPursuit above, and it matters more here. "Did it come round the other
    /// side" has two very different failure modes that look identical from the outside: the rings
    /// cover BOTH sides of the obstacle (the radius is too big for it — nothing is left to pick),
    /// or there is simply no waypoint outside the rings with a view of the player (the level
    /// needs waypoints there, and no number will fix it). Seeing the rings over the real geometry
    /// is what tells the two apart.
    ///
    /// Drawn from the same graph, the same age window and the same radius the pursuit uses, never
    /// a copy — see the class summary for what a gizmo that disagrees with the game is worth.
    /// Play mode only: there is no trail outside it.
    /// </summary>
    private void DrawChaseTrail(SO_NemesisData data)
    {
        if (!Application.isPlaying) return;

        NemesisChaseProgress progress = GetComponent<NemesisChaseProgress>();
        if (progress == null || (!progress.IsMeasuring && !progress.IsChaseStagnant)) return;

        NemesisStateManager manager = StateManager;
        NemesisController controller = manager != null ? manager.NemesisController : null;
        NemesisRouteGraph graph = controller != null ? controller.RouteGraph : null;

        bool stagnant = progress.IsChaseStagnant;

        // Faint while it is only measuring: the rings are a preview of what a stall would mark
        // down, which is exactly what you want to see while tuning the radius before one fires.
        Color color = stagnant
            ? HardDetectColor
            : new Color(HardDetectColor.r, HardDetectColor.g, HardDetectColor.b, 0.3f);

        if (graph != null && graph.IsBuilt)
        {
            for (int i = 0; i < graph.NodeCount; i++)
            {
                if (graph.SensedAge(i) > NemesisPursuit.TrailMemoryTime) continue;

                NemesisRouteGraph.Node node = graph.GetNode(i);
                if (!node.IsValid) continue;

                DrawDisc(node.Position, data.ChaseTrailPenaltyRadius, color);
                Gizmos.DrawWireSphere(node.Position, 0.25f);
            }
        }

        if (!stagnant) return;

        DrawLabel(transform.position + Vector3.up * 2.6f,
                  $"persecución estancada ({progress.ChaseStalledCount})", HardDetectColor);
    }

    /// <summary>
    /// Where the chase is aiming: the predicted point, and the waypoint it decided to route
    /// through when it took one.
    ///
    /// An invisible decision is an untunable one. A Nemesis that swung round a corner to open the angle
    /// on you and a Nemesis that wandered into you from the side are indistinguishable from the
    /// outside, so without this "did the flanking work" is not a question anyone can answer - and
    /// ChaseDetourTolerance is a number nobody can tune. The ABSENCE of the detour line is
    /// information too: it means going direct was good enough, which is the common and correct
    /// case.
    ///
    /// Play mode only, because none of it exists outside it.
    /// </summary>
    private void DrawPursuit()
    {
        if (!Application.isPlaying) return;

        NemesisStateManager manager = StateManager;
        NemesisChasingState chasing = manager != null ? manager.ChasingState : null;
        NemesisPursuit pursuit = chasing != null ? chasing.Pursuit : null;
        if (pursuit == null || !pursuit.HasPredictedPoint) return;

        Vector3 eye = transform.position + Vector3.up * 0.5f;

        // Amber, the alert band, and not red: red is the capture reach and nothing else.
        Gizmos.color = VisionColor;
        Gizmos.DrawLine(eye, pursuit.PredictedPoint);
        Gizmos.DrawWireSphere(pursuit.PredictedPoint, 0.4f);
        DrawLabel(pursuit.PredictedPoint + Vector3.up * 0.8f, "predicho", VisionColor);

        if (!pursuit.HasRoutePoint) return;

        Gizmos.color = HardDetectColor;
        Gizmos.DrawLine(eye, pursuit.RoutePoint);
        Gizmos.DrawWireCube(pursuit.RoutePoint, Vector3.one * 0.5f);
        DrawLabel(pursuit.RoutePoint + Vector3.up * 0.8f, "flanqueo", HardDetectColor);
    }

    /// <summary>
    /// The search's choice (Plan-Busqueda-Nemesis Fase 2b, §3.5): a line to the place it is heading
    /// to, the zone it was chosen for with its share of the value — when picked, and now — and the
    /// other places the last roll weighed.
    ///
    /// WITHOUT THIS THE SEARCH IS UNTUNABLE. "It went the wrong way" has four different causes that
    /// look the same from watching it walk: the map had the value in the wrong place (the heat map
    /// answers that), the right place was not worth the walk (the candidates marked "no vale" answer
    /// it: lower Search Map Worth Threshold), the roll simply came up the other way (the faint
    /// candidates show what it was choosing between, and by how much), or it was never the map's
    /// choice at all — a hiding spot, the scatter of a search with nothing to go on, standing with
    /// nowhere to go. The label says which. And "ahora" dropping under Search Map Repick Share of the
    /// share it was picked with is the moment it turns away before arriving.
    ///
    /// It took the place of the sweep disc (the anchor, its radius, the swept trail), which stopped
    /// existing when the search stopped sweeping one. Play mode only, and only while it searches:
    /// there is nothing to draw until then.
    /// </summary>
    private void DrawSearchPick(SO_NemesisData data, NemesisStateManager manager)
    {
        if (!Application.isPlaying) return;
        if (manager.CurrentStateKey != NemesisStateManager.ENemesisState.Searching) return;

        NemesisSearchingState searching = manager.SearchingState;
        if (searching == null) return;

        Vector3 target = searching.SearchTarget;

        Gizmos.color = SearchColor;
        Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, target + Vector3.up * 0.3f);
        Gizmos.DrawWireCube(target, Vector3.one * 0.5f);

        // On its way to a hiding spot: DrawHidingKnowledge already says which and why.
        if (searching.SpotTarget != null) return;

        Vector3 labelAt = target + Vector3.up * 1.2f;

        switch (searching.Target)
        {
            case NemesisSearchingState.ETarget.Scatter:
                DrawLabel(labelAt, "al azar: el mapa no tiene valor", SearchColor);
                return;

            case NemesisSearchingState.ETarget.Standing:
                DrawLabel(labelAt, "se queda: nada vale la caminata", SearchColor);
                break;

            default:
                // The zone the value is measured around. Usually where it is heading; with precise
                // evidence inside the zone it stands on the evidence point instead, and the two part.
                DrawDisc(searching.TargetZone, data.SearchMapZoneRadius, SearchColor);
                DrawLabel(labelAt, $"busca acá: {searching.TargetShare:P0} del valor " +
                                   $"(ahora {searching.TargetShareNow:P0})", SearchColor);
                break;
        }

        // What the last roll was choosing between.
        NemesisSearchPicker picker = searching.Picker;
        IReadOnlyList<NemesisSearchPicker.Candidate> candidates = picker.Candidates;
        Color faint = new Color(SearchColor.r, SearchColor.g, SearchColor.b, 0.45f);

        for (int i = 0; i < candidates.Count; i++)
        {
            if (i == picker.ChosenIndex) continue;

            NemesisSearchPicker.Candidate candidate = candidates[i];

            Gizmos.color = faint;
            Gizmos.DrawWireSphere(candidate.Position + Vector3.up * 0.2f, 0.3f);

            // NaN: it held too little for the walk to be worth asking about.
            string walk = float.IsNaN(candidate.Seconds) ? "poco valor"
                        : float.IsPositiveInfinity(candidate.Seconds) ? "sin camino a pie"
                        : $"{candidate.Seconds:0.0} s";
            string roll = candidate.InRoll ? "" : " · no vale";
            DrawLabel(candidate.Position + Vector3.up * 0.7f, $"{candidate.Share:P0} · {walk}{roll}", faint);
        }
    }

    /// <summary>Below this share of the likeliest node, a node is not drawn: the tail of the spread
    /// is everywhere, and drawing it hides the shape.</summary>
    private const float MapDrawFloor = 0.02f;

    /// <summary>
    /// The possibility map as heat over the NavMesh (Plan-Busqueda-Nemesis Fase 2), the cone it is
    /// clearing, and a line to the likeliest place with how much of the value sits within 4 m of it.
    ///
    /// THIS IS WHAT THE SEARCH GOES BY (Fase 2b), so it is the first thing to look at when a search
    /// goes somewhere odd: lose the Nemesis in a corridor with one exit and the heat has to run ahead
    /// of it towards the exit, never behind it; at a T it has to lean the way you were going without
    /// leaving the other arm empty. A map that does not look right here does not search right, and
    /// the spread speed, the heading bias and the clear range are three numbers nobody can tune
    /// without seeing them. The place the search actually CHOSE off it is DrawSearchPick's — the
    /// likeliest place drawn here is the map's own argmax, which the search rolls around and does
    /// not simply walk to.
    ///
    /// Drawn from the component's own map, never a copy. Play mode only.
    /// </summary>
    private void DrawPossibilityMap(SO_NemesisData data, NemesisStateManager manager, Transform eye)
    {
        if (!Application.isPlaying) return;

        NemesisPossibilityMap possibility = manager.PossibilityMap;
        if (possibility == null || !possibility.IsBuilt) return;

        // "Acá no está": the reach of the clearing, at the real view angle.
        DrawCone(eye, data.SearchMapClearRange, data.ViewAngle, MapClearColor, "acá no está");

        PossibilityMap map = possibility.Map;
        PossibilityGraph graph = map.Graph;

        // The Hub's doorway nodes, always: if these are not at its doors, value drains through walls.
        Gizmos.color = MapClearColor;
        for (int i = 0; i < graph.NodeCount; i++)
        {
            if (!graph.IsDrain(i)) continue;
            Vector3 p = graph.Position(i);
            Gizmos.DrawWireCube(p + Vector3.up * 0.5f, new Vector3(0.6f, 1f, 0.6f));
            DrawLabel(p + Vector3.up * 1.3f, "puerta del Hub", MapClearColor);
        }

        if (!possibility.TryGetBest(out int best, out Vector3 bestAt)) return;

        float max = map.Value(best);
        float size = graph.Spacing * 0.8f;
        Vector3 tile = new Vector3(size, 0.04f, size);

        for (int i = 0; i < graph.NodeCount; i++)
        {
            float v = map.Value(i);
            if (v <= max * MapDrawFloor) continue;

            float t = v / max;
            Color color = Color.Lerp(SearchColor, HardDetectColor, t);
            color.a = 0.2f + 0.6f * t;
            Gizmos.color = color;

            Vector3 p = graph.Position(i);
            if (graph.Kind(i) == PossibilityGraph.ENodeKind.HidingSpot)
            {
                Gizmos.DrawWireCube(p, Vector3.one * 0.7f);
                Gizmos.DrawCube(p, Vector3.one * 0.35f);
            }
            else Gizmos.DrawCube(p + Vector3.up * 0.05f, tile);
        }

        Gizmos.color = HardDetectColor;
        Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, bestAt + Vector3.up * 0.3f);
        Gizmos.DrawWireSphere(bestAt, 0.4f);
        DrawLabel(bestAt + Vector3.up * 1f, $"más probable ({possibility.ShareNear(bestAt, 4f):P0} en 4 m)",
                  HardDetectColor);
    }

    /// <summary>
    /// The hiding spot the Nemesis knows or suspects the player is in (plan §3.4), against the
    /// geometry it has to cross to get there.
    ///
    /// The HUD names the spot; the line is what shows the walk is measured to the APPROACH POINT
    /// and whether the NavMesh actually reaches it. A certainty that goes unacted on for a whole
    /// search budget is forgotten as a safety net (NemesisHidingAwareness), and a line to an
    /// approach point behind a wall is how to tell that from a Nemesis that never knew.
    ///
    /// The ring round the interior is level B: how far it can still make the player out at through
    /// the slats or under the table, from FieldOfView.HiddenViewRange and never a copy of the
    /// multipliers. A locker's slats are in the DOOR, so it gets half a disc facing the way the
    /// player inside looks out; a table is open all round. No ring means sealed — a container, or
    /// a locker whose exposure is 0 — and that absence answers "why is the meter not filling".
    ///
    /// Orange for a certainty (the hard-detection colour: "inside this you are spotted") and the
    /// passive blue for a suspicion, which is also the colour of the state that goes to look at
    /// one (Investigating) in NemesisDebugHUD. Play mode only: there is nothing to know outside it.
    /// </summary>
    private void DrawHidingKnowledge(NemesisStateManager manager)
    {
        if (!Application.isPlaying) return;

        HidingSpot known = manager.KnownHidingSpot;
        HidingSpot spot = known != null ? known : manager.SuspectedHidingSpot;
        if (spot == null) return;

        Color color = known != null ? HardDetectColor : HearingColor;
        Vector3 approach = spot.ApproachPoint.position;

        Gizmos.color = color;
        Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, approach);
        Gizmos.DrawWireSphere(approach, 0.3f);
        DrawLabel(approach + Vector3.up * 0.8f, $"{(known != null ? "sabe" : "sospecha")} {NameOf(spot)}",
                  color);

        FieldOfView view = manager.FieldOfView;
        float range = view != null ? view.HiddenViewRange(spot) : 0f;
        if (range <= 0f) return;

        Vector3 interior = spot.InteriorPose.position;
        string label = $"distingue {range:0.#} m";

        if (spot.Type == EHidingSpotType.Locker)
        {
            DrawCone(interior, spot.InteriorPose.forward, range, 180f, color, label);
            return;
        }

        DrawDisc(interior, range, color);
        DrawLabel(interior + Vector3.forward * range, label, color);
    }

    /// <summary>SpotId when the designer set one, the GameObject's name otherwise — the same name
    /// NemesisDebugHUD prints, so the two pictures agree.</summary>
    private static string NameOf(HidingSpot spot) =>
        string.IsNullOrEmpty(spot.SpotId) ? spot.name : spot.SpotId;

    /// <summary>
    /// Names the GameObject at its own base. On a level with a single Nemesis this looks
    /// redundant — but the moment there are two (a duplicate dropped in for testing, a second
    /// prefab variant), every one of the ranges below is otherwise unlabelled as to whose it is.
    /// </summary>
    private void DrawName()
    {
        if (!drawLabels) return;

#if UNITY_EDITOR
        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2.2f, name);
#endif
    }

    // ── Vision ──────────────────────────────────────────────────────────────

    private void DrawVision(SO_NemesisData data, Transform eye)
    {
        if (drawVisionCone)
            DrawCone(eye, data.ViewRange, data.ViewAngle, VisionColor, $"view {data.ViewRange:0.#} m");

        if (drawAdaptiveVision) DrawAdaptiveVision(data, eye);

        // Nested inside the outer cone, so what the eye reads off the picture is the GAP: that
        // wedge is the band where the Nemesis has to look at you for a moment before it reacts.
        // Drawn in the hard-detection orange rather than a fourth colour, because "inside this you
        // are spotted immediately" is the same statement the proximity ring makes.
        if (drawFocusCone && data.HasPeripheralVision)
        {
            DrawCone(eye, data.ViewRange, data.FocusAngle, HardDetectColor,
                     $"foco {data.FocusAngle:0.#}\u00b0");
        }

        if (drawCrouchedVisionCone)
        {
            // Crouching shortens the range rather than breaking line of sight — see FieldOfView.
            // So it is the same cone at a shorter radius, drawn nested, which is what makes the
            // size difference legible.
            float crouched = data.ViewRange * data.CrouchVisionMultiplier;
            DrawCone(eye, crouched, data.ViewAngle, CrouchColor, $"crouched {crouched:0.#} m");
        }

        if (drawUnderTableVisionCone)
        {
            // A table shortens the view the same way a crouch does (plan §3.4, level B), so it is
            // the same nested cone again. What it feeds is different — the suspicion meter, never
            // a sighting — and that is not something a shape can show; the tooltip says it.
            float underTable = data.ViewRange * data.UnderTableVisionMultiplier;
            DrawCone(eye, underTable, data.ViewAngle, UnderTableColor,
                     $"bajo mesa {underTable:0.#} m");
        }

        if (drawRearSense && data.RearSenseRange > 0f && data.RearSenseStrength > 0f)
        {
            // The third zone (VisionZones.EZone.Rear): everything OUTSIDE the vision cone, out to
            // RearSenseRange, measured flat from the body — so drawn at the feet, facing away from
            // where it is looking. The crouched reach inside it, faint: crouching shortens it the way
            // it shortens the view, and below the proximity ring it adds nothing.
            Vector3 back = -LookDirectionOf(eye);
            float span = 360f - data.ViewAngle;
            DrawCone(transform.position, back, data.RearSenseRange, span, RearColor,
                     $"siente atrás {data.RearSenseRange:0.#} m");

            float crouchedRear = data.RearSenseRange * data.CrouchVisionMultiplier;
            Color faint = new Color(RearColor.r, RearColor.g, RearColor.b, 0.35f);
            DrawCone(transform.position, back, crouchedRear, span, faint, string.Empty);
        }

        if (!drawProximityDetection || data.ProximityDetectionRange <= 0f) return;

        // At the feet, not the eye: the test is flat from the body and gated on the Nemesis's own
        // floor (FieldOfView.IsStandingOnMe). Drawn at the eye it floated 1.8 m above where it
        // applies — the same picture that hid the old sphere never reaching the floor at all.
        DrawDisc(transform.position, data.ProximityDetectionRange, HardDetectColor);
    }

    /// <summary>
    /// How far the eyes really reach, against the base cone (AdaptiveViewRange).
    ///
    /// "View range 7" stopped being the whole answer on 04/10: a player it is already seeing stays
    /// seen out to ViewRange x the hold scale, and one it lost can be seen again from further the
    /// longer the hunt goes on. Without this a sighting from twelve metres looks like the cone
    /// lying. The two ceilings are drawn faint — with their distance outside Play mode, where they
    /// are the only thing there is to tune against the level — and, in Play, the range the sweep is
    /// using right now is drawn over them with the reason, from the sensor's own numbers and never a
    /// copy of the rule. Nothing but the faint ceilings while it is on the base range: an unaware
    /// Nemesis sees to the base cone, and the picture has to say so.
    ///
    /// And where the eyes are AIMED while it looks the way a lost player went (ChaseGaze): the lost
    /// spot, the point past it, and the line from the eye. The cones already turn with the gaze;
    /// this is what shows WHY they turned, against the corner it is about.
    /// </summary>
    private void DrawAdaptiveVision(SO_NemesisData data, Transform eye)
    {
        const float Visible = 0.05f;

        float baseRange = data.ViewRange;
        float hold = data.ViewHoldRange;
        float hunt = data.ViewHuntRange;

        bool playing = Application.isPlaying;
        Color ceiling = new Color(VisionColor.r, VisionColor.g, VisionColor.b, 0.3f);

        // One cone when the two ceilings coincide (as shipped: both x2), or the labels stack.
        if (Mathf.Abs(hold - hunt) <= Visible)
        {
            if (hold > baseRange + Visible)
                DrawCone(eye, hold, data.ViewAngle, ceiling, playing ? string.Empty : $"sostiene / caza {hold:0.#} m");
        }
        else
        {
            if (hold > baseRange + Visible)
                DrawCone(eye, hold, data.ViewAngle, ceiling, playing ? string.Empty : $"sostiene {hold:0.#} m");

            if (hunt > baseRange + Visible)
                DrawCone(eye, hunt, data.ViewAngle, ceiling, playing ? string.Empty : $"caza {hunt:0.#} m");
        }

        if (!playing) return;

        NemesisStateManager manager = StateManager;
        FieldOfView view = manager != null ? manager.FieldOfView : null;
        if (view == null) return;

        float effective = view.EffectiveViewRange;
        if (effective > baseRange + Visible)
        {
            DrawCone(eye, effective, data.ViewAngle, AdaptiveVisionColor,
                     $"{DescribeRangeReason(view.ViewRangeReason)} ×{view.ViewRangeScale:0.0#} · {effective:0.#} m");
        }

        NemesisLookAround look = GetComponent<NemesisLookAround>();
        if (look == null || !look.TryGetLostTrail(out Vector3 lostAt, out Vector3 aimPoint)) return;

        Vector3 lift = Vector3.up * 0.1f;

        Gizmos.color = AdaptiveVisionColor;
        Gizmos.DrawLine(lostAt + lift, aimPoint + lift);
        Gizmos.DrawLine(eye.position, aimPoint + lift);
        Gizmos.DrawWireSphere(lostAt, 0.25f);
        Gizmos.DrawWireSphere(aimPoint, 0.35f);
        DrawLabel(aimPoint + Vector3.up * 0.8f, "mira por donde se fue", AdaptiveVisionColor);
    }

    /// <summary>The designer's word for why the range is what it is. The same words the debug HUD
    /// uses, so the two pictures agree.</summary>
    private static string DescribeRangeReason(AdaptiveViewRange.EReason reason)
    {
        switch (reason)
        {
            case AdaptiveViewRange.EReason.Hold: return "sostiene";
            case AdaptiveViewRange.EReason.Hunt: return "caza";
            case AdaptiveViewRange.EReason.Settling: return "vuelve a la base";
            default: return "base";
        }
    }

    /// <summary>
    /// A horizontal arc at <paramref name="range"/> spanning <paramref name="angle"/> degrees,
    /// centred on the transform's forward, plus the two edges back to the origin.
    ///
    /// Halved against forward, matching FieldOfView's own test
    /// (<c>Vector3.Angle(forward, dir) &lt; viewAngle / 2</c>) — drawing the full angle to each
    /// side would show a cone twice as wide as the one the game uses.
    /// </summary>
    private void DrawCone(Transform eye, float range, float angle, Color color, string label) =>
        DrawCone(eye.position, LookDirectionOf(eye), range, angle, color, label);

    /// <summary>
    /// Cone around an explicit front vector.
    ///
    /// The overload exists because the eye no longer necessarily looks where the body points: with
    /// NemesisLookAround driving FieldOfView.LookDirection, a cone drawn off eye.forward while the
    /// Nemesis is scanning is a picture of somewhere it is NOT looking, which is worse than no
    /// picture at all.
    /// </summary>
    private void DrawCone(Vector3 origin, Vector3 front, float range, float angle, Color color,
                          string label)
    {
        if (range <= 0.01f) return;

        float half = Mathf.Clamp(angle, 0f, 360f) * 0.5f;

        Gizmos.color = color;

        Vector3 previous = origin + DirectionAt(front, -half) * range;
        Gizmos.DrawLine(origin, previous);

        for (int i = 1; i <= arcSegments; i++)
        {
            float t = (float)i / arcSegments;
            Vector3 point = origin + DirectionAt(front, Mathf.Lerp(-half, half, t)) * range;

            Gizmos.DrawLine(previous, point);
            previous = point;
        }

        Gizmos.DrawLine(origin, previous);

        DrawLabel(origin + DirectionAt(front, 0f) * range, label, color);
    }

    /// <summary>
    /// Where the cone should be drawn from: the sensor's live look direction in Play mode, the
    /// eye's forward otherwise.
    ///
    /// Reached through the component rather than cached, like everything else here - the Scene view
    /// draws outside Play mode, where Awake has not run.
    /// </summary>
    private Vector3 LookDirectionOf(Transform eye)
    {
        NemesisStateManager manager = StateManager;
        FieldOfView view = manager != null ? manager.FieldOfView : null;

        return view != null && Application.isPlaying ? view.LookDirection : eye.forward;
    }

    /// <summary>Direction <paramref name="degrees"/> off a front vector, flattened so a Nemesis on
    /// a ramp still draws its cone level with the floor.</summary>
    private static Vector3 DirectionAt(Vector3 front, float degrees)
    {
        front.y = 0f;
        if (front.sqrMagnitude < 0.0001f) front = Vector3.forward;

        return Quaternion.AngleAxis(degrees, Vector3.up) * front.normalized;
    }

    // ── Hearing ─────────────────────────────────────────────────────────────

    private void DrawHearing(SO_NemesisData data, NemesisStateManager manager)
    {
        if (!drawHearing || data.ListenRange <= 0.01f) return;

        FieldOfListening listening = manager.FieldOfListening;
        Vector3 origin = listening != null ? listening.transform.position : transform.position;

        // The ceiling, not the range. What the Nemesis actually hears depends on how loud the
        // player is being, which is the three bands below — this outer ring is only the cap.
        DrawDisc(origin, data.ListenRange, HearingColor);
        DrawLabel(origin + Vector3.right * data.ListenRange,
                  $"hearing cap {data.ListenRange:0.#} m", HearingColor);

        DrawGaitBands(data, origin);
        if (drawHearingError) DrawHearingError(listening);
    }

    /// <summary>How long after a noise of the player's its real-versus-perceived pair stays drawn.
    /// </summary>
    private const float HearingErrorShowSeconds = 2f;

    /// <summary>
    /// Where the ear placed the player's last noise, against where they really were
    /// (Plan-Busqueda-Nemesis Fase 1): a line from the real position to the perceived one, and round
    /// the perceived one the longest offset the ear could have put on it. The pair is the whole point
    /// — HearingLocalizationError is a fraction nobody can picture, and a circle that always swallows
    /// the line says the error is doing nothing.
    ///
    /// The real position comes from the sensor's editor-only copy (the game never sees it), so this
    /// is editor and Play mode only, for a couple of seconds after each noise.
    /// </summary>
    private void DrawHearingError(FieldOfListening listening)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying || listening == null) return;
        if (!listening.TryGetLastPlayerNoise(out FieldOfListening.HeardNoise noise)) return;
        if (Time.time - noise.HeardAt > HearingErrorShowSeconds) return;
        if (!listening.TryGetLastPlayerNoiseTruth(out Vector3 truth)) return;

        Gizmos.color = HearingColor;
        Gizmos.DrawLine(truth + Vector3.up * 0.1f, noise.Position + Vector3.up * 0.1f);
        Gizmos.DrawWireSphere(truth, 0.2f);
        Gizmos.DrawWireCube(noise.Position, Vector3.one * 0.35f);
        if (noise.LocalizationError > 0.01f) DrawDisc(noise.Position, noise.LocalizationError, HearingColor);

        Vector3 off = noise.Position - truth;
        off.y = 0f;
        DrawLabel(noise.Position + Vector3.up * 0.8f,
                  $"oyó acá ({off.magnitude:0.0} / ±{noise.LocalizationError:0.0} m)", HearingColor);
#endif
    }

    /// <summary>
    /// The three ranges that actually decide whether you are heard: one per gait, at the player's
    /// own noise radii.
    ///
    /// These are the rings worth looking at, and they did not exist before because the range did
    /// not depend on the player at all — behind a wall a sprint and a crouch were audible at
    /// identical distance. Now they are three different circles, and a level designer can stand
    /// the Nemesis in a corridor and see exactly which of them a doorway falls inside.
    ///
    /// The wall multiplier is applied to all three rather than drawn as three more rings: six
    /// concentric circles stop being readable, and "through a wall" is the case that applies
    /// nearly always, so it is the one worth showing.
    /// </summary>
    private void DrawGaitBands(SO_NemesisData data, Vector3 origin)
    {
        // Read off SO_Movement so the picture cannot drift from the player's real emitter. Falls
        // back to the shipped radii when the asset cannot be found — this is a Scene-view aid and
        // must not throw or vanish just because nothing is loaded.
        float crouch = 1f, walk = 2f, run = 6f;

        SO_Movement movement = FindPlayerMovement();
        if (movement != null)
        {
            crouch = movement.CrouchNoiseRadius;
            walk = movement.FootstepNoiseRadius;
            run = movement.RunNoiseRadius;
        }

        float wall = data.WallOcclusionEnabled ? data.WallOcclusionMultiplier : 1f;
        Color faded = new Color(HearingColor.r, HearingColor.g, HearingColor.b, 0.5f);

        DrawGaitBand(data, origin, crouch, wall, "crouch", faded);
        DrawGaitBand(data, origin, walk,   wall, "walk",   faded);
        DrawGaitBand(data, origin, run,    wall, "run",    HearingColor);
    }

    private void DrawGaitBand(SO_NemesisData data, Vector3 origin, float loudness, float wall,
                              string gait, Color color)
    {
        // Same formula as FieldOfListening.CanHear, deliberately: a gizmo that computes the range
        // its own way is worse than no gizmo, because it is believed.
        float open = Mathf.Min(data.ListenRange, loudness * data.NoiseRangeScale);

        DrawDisc(origin, open, color);
        DrawLabel(origin + Vector3.forward * open,
                  $"{gait} {open:0.#} m  (wall {open * wall:0.#})", color);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Cached: FindAssets searches the whole project, and it used to run on every Scene-view repaint
    /// for as long as a Nemesis was in the scene. Edits to the asset still show, since this is the
    /// asset itself; a deleted or reimported one reads as null and is simply looked up again.
    /// </summary>
    private static SO_Movement cachedMovement;
#endif

    private static SO_Movement FindPlayerMovement()
    {
#if UNITY_EDITOR
        if (cachedMovement != null) return cachedMovement;

        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:SO_Movement");
        if (guids.Length == 0) return null;

        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
        cachedMovement = UnityEditor.AssetDatabase.LoadAssetAtPath<SO_Movement>(path);
        return cachedMovement;
#else
        return null;
#endif
    }

    // ── Capture ─────────────────────────────────────────────────────────────

    private void DrawCatch(SO_NemesisData data)
    {
        if (!drawCatchReach) return;

        float reach = data.CatchMaxReach;
        float height = data.CatchMaxVerticalOffset;
        if (reach <= 0.01f) return;

        Vector3 centre = transform.position;

        // A cylinder and not a sphere: the check is horizontal distance AND vertical offset,
        // tested separately (see NemesisStateManager.CanReachPlayerNow). A sphere would
        // suggest the grab reaches diagonally as far as it reaches flat, which is the mistake that
        // made "it grabbed me from the floor below" hard to reason about.
        DrawDisc(centre + Vector3.up * height, reach, CatchColor);
        DrawDisc(centre - Vector3.up * height, reach, CatchColor);
        DrawDisc(centre, reach, CatchColor);

        Gizmos.color = CatchColor;
        for (int i = 0; i < 4; i++)
        {
            Vector3 offset = Quaternion.AngleAxis(i * 90f, Vector3.up) * Vector3.forward * reach;
            Gizmos.DrawLine(centre + offset - Vector3.up * height,
                            centre + offset + Vector3.up * height);
        }

        DrawLabel(centre + Vector3.up * height, $"catch {reach:0.##} m", CatchColor);
    }

    // ── Search / vignette ───────────────────────────────────────────────────

    private void DrawSearchAndVignette(SO_NemesisData data)
    {
        if (drawSearchSweep)
        {
            // The last-resort scatter of a search with no value on the map. Around the Nemesis here
            // because that is where such a search would start; with value, the map decides instead
            // (DrawSearchPick).
            DrawDisc(transform.position, data.SearchSweepRadius, SearchColor);
            DrawLabel(transform.position + Vector3.forward * data.SearchSweepRadius,
                      $"búsqueda sin valor: al azar {data.SearchSweepRadius:0.#} m", SearchColor);
        }

        if (!drawProximityVignette) return;

        DrawDisc(transform.position, data.ProximityRadius, VignetteColor);
    }

    // ── Primitives ──────────────────────────────────────────────────────────

    /// <summary>
    /// Horizontal circle. Gizmos has no disc primitive and Handles is editor-only, so this is a
    /// line loop — which also keeps the whole component compiling in a player build, where
    /// OnDrawGizmos is simply never called.
    /// </summary>
    private void DrawDisc(Vector3 centre, float radius, Color color)
    {
        if (radius <= 0.01f) return;

        Gizmos.color = color;

        Vector3 previous = centre + Vector3.forward * radius;

        for (int i = 1; i <= arcSegments; i++)
        {
            float angle = 360f * i / arcSegments;
            Vector3 point = centre + Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * radius;

            Gizmos.DrawLine(previous, point);
            previous = point;
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Lazily built, and never in a static initialiser: constructing a GUIStyle before the editor
    /// skin is loaded throws. OnDrawGizmos runs on repaint, which is late enough.
    /// </summary>
    private static GUIStyle labelStyle;
#endif

    /// <summary>
    /// Marks a range with a tick and writes the distance next to it.
    ///
    /// The number is the entire point of this component — "range 5" in the inspector is precisely
    /// the thing nobody could picture. Handles is editor-only, so the text is behind UNITY_EDITOR
    /// while the tick is not; in a player build none of it runs, because OnDrawGizmos does not.
    /// </summary>
    private void DrawLabel(Vector3 position, string label, Color color)
    {
        const float Tick = 0.2f;

        if (!drawLabels) return;

        Gizmos.color = color;
        Gizmos.DrawLine(position - Vector3.up * Tick, position + Vector3.up * Tick);
        Gizmos.DrawLine(position - Vector3.right * Tick, position + Vector3.right * Tick);

#if UNITY_EDITOR
        if (labelStyle == null) labelStyle = new GUIStyle(UnityEditor.EditorStyles.miniLabel);
        labelStyle.normal.textColor = color;

        UnityEditor.Handles.Label(position + Vector3.up * 0.25f, label, labelStyle);
#endif
    }
}
