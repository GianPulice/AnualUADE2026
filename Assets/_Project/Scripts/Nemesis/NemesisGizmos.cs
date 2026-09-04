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
///     wrong while dressing the level, and turning eleven checkboxes off one at a time is not a
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

    [Tooltip("Inner cone (FocusAngle): where detection is INSTANT. Everything between it and the " +
             "outer cone is peripheral vision, where the Nemesis only builds suspicion instead of " +
             "spotting you outright. Drawn nested inside the vision cone, so the gap between the " +
             "two arcs IS the peripheral band.")]
    [SerializeField] private bool drawFocusCone = true;

    [Tooltip("Hard detection radius: inside it you are seen with no cone and no hiding.")]
    [SerializeField] private bool drawProximityDetection = true;

    [Header("Hearing")]
    [Tooltip("Hearing radius at full strength. The wall and floor multipliers are drawn as inner " +
             "rings, since those are the ranges that actually apply most of the time.")]
    [SerializeField] private bool drawHearing = true;

    [Header("Capture")]
    [Tooltip("Where the grab can happen: CatchMaxReach horizontally by CatchMaxVerticalOffset " +
             "vertically. The only thing drawn in red.")]
    [SerializeField] private bool drawCatchReach = true;

    [Header("Search & feedback")]
    [Tooltip("Radius of the Searching state's fallback scatter (SearchSweepRadius).")]
    [SerializeField] private bool drawSearchSweep = true;

    [Tooltip("The room the search has committed to sweeping, its centre, and the points it has " +
             "already looked at. Play mode only — nothing to draw until a search commits.")]
    [SerializeField] private bool drawRoomSweep = true;

    [Tooltip("ProximityRadius — the HUD vignette only. Detects nothing.")]
    [SerializeField] private bool drawProximityVignette = false;

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
    private static readonly Color HearingColor   = new Color(0.541f, 0.706f, 0.831f);
    private static readonly Color HardDetectColor = new Color(0.95f, 0.55f, 0.25f);
    private static readonly Color CatchColor     = new Color(0.8f, 0.10f, 0.10f);
    private static readonly Color SearchColor    = new Color(0.65f, 0.55f, 0.85f);
    private static readonly Color VignetteColor  = new Color(0.45f, 0.45f, 0.50f);

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
        DrawIntercept();
        if (drawRoomSweep) DrawRoomSweep();
        DrawPursuit();
    }

    /// <summary>
    /// Where the chase is aiming: the predicted point, and the waypoint it decided to route
    /// through when it took one.
    ///
    /// Same argument as DrawIntercept below. A Nemesis that swung round a corner to open the angle
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
    /// A line to wherever the Searching state is aiming its cut-off.
    ///
    /// It is drawn because the interception is the one decision in the system with no visible
    /// tell: a Nemesis heading somewhere clever and a Nemesis heading somewhere by accident look
    /// identical from outside. Without this, "did it cut me off or did it wander into me" is not
    /// answerable, and neither is the tuning that depends on it.
    ///
    /// Nothing is drawn when it fell back to the sweep, so the ABSENCE of the line is information
    /// too: it means the belief, the heading or the waypoints were not good enough to commit to.
    /// </summary>
    private void DrawIntercept()
    {
        // Fetched rather than cached, like the state manager above it: the Scene view draws
        // outside Play mode where Awake has not run.
        NemesisTelemetry telemetry = GetComponent<NemesisTelemetry>();
        if (telemetry == null) return;

        Vector3? intercept = telemetry.SearchInterceptPoint;
        if (!intercept.HasValue) return;

        Gizmos.color = SearchColor;
        Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, intercept.Value);
        Gizmos.DrawWireCube(intercept.Value, Vector3.one * 0.5f);

        DrawLabel(intercept.Value + Vector3.up * 0.8f, "intercepción", SearchColor);
    }

    /// <summary>
    /// The committed room sweep: the area, its centre, and every point already looked at.
    ///
    /// WITHOUT THIS THE FEATURE IS UNTUNABLE. RoomSweepRadius decides how much of a room counts as
    /// the room, and the wall test that clips it is invisible by nature — the difference between
    /// "the radius is too small" and "a wall is cutting the room in half" is not something anyone
    /// can tell from watching the Nemesis walk. Drawing the anchor with its radius and the swept
    /// trail alongside it makes both readable at a glance.
    ///
    /// Play mode only, and deliberately: unlike the ranges below, there is nothing to draw until a
    /// search has actually committed to somewhere.
    /// </summary>
    private void DrawRoomSweep()
    {
        if (!Application.isPlaying) return;

        NemesisStateManager manager = GetComponent<NemesisStateManager>();
        NemesisSearchingState searching = manager != null ? manager.SearchingState : null;
        if (searching == null || !searching.IsSweepingRoom) return;

        NemesisFreeRoam roam = searching.FreeRoam;

        DrawDisc(roam.Anchor, roam.Radius, SearchColor);

        Gizmos.color = SearchColor;
        Gizmos.DrawWireSphere(roam.Anchor, 0.4f);

        IReadOnlyList<Vector3> swept = roam.SweptPoints;
        for (int i = 0; i < swept.Count; i++)
        {
            Gizmos.DrawWireCube(swept[i], Vector3.one * 0.35f);

            // Chained in visit order, so the shape of the sweep is readable: a trail that keeps
            // crossing itself means the swept-point penalty is too weak to spread it out.
            if (i > 0) Gizmos.DrawLine(swept[i - 1], swept[i]);
        }

        DrawLabel(roam.Anchor + Vector3.up * 1.2f,
                  $"barrido de habitación {roam.Radius:0.#} m", SearchColor);
    }

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

        // Nested inside the outer cone, so what the eye reads off the picture is the GAP: that
        // wedge is the band where the Nemesis has to look at you for a moment before it reacts.
        // Drawn in the hard-detection orange rather than a fourth colour, because "inside this you
        // are spotted immediately" is the same statement the proximity ring makes.
        if (drawFocusCone && data.HasPeripheralVision)
        {
            DrawCone(eye, data.ViewRange, data.FocusAngle, HardDetectColor,
                     $"foco {data.FocusAngle:0.#}\u00b0");
        }

        if (!drawCrouchedVisionCone) return;

        // Crouching shortens the range rather than breaking line of sight — see FieldOfView. So it
        // is the same cone at a shorter radius, drawn nested, which is what makes the size
        // difference legible.
        float crouched = data.ViewRange * data.CrouchVisionMultiplier;
        DrawCone(eye, crouched, data.ViewAngle, CrouchColor, $"crouched {crouched:0.#} m");

        if (!drawProximityDetection || data.ProximityDetectionRange <= 0f) return;

        DrawDisc(eye.position, data.ProximityDetectionRange, HardDetectColor);
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

    private static SO_Movement FindPlayerMovement()
    {
#if UNITY_EDITOR
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:SO_Movement");
        if (guids.Length == 0) return null;

        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
        return UnityEditor.AssetDatabase.LoadAssetAtPath<SO_Movement>(path);
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
            DrawDisc(transform.position, data.SearchSweepRadius, SearchColor);
            DrawLabel(transform.position + Vector3.forward * data.SearchSweepRadius,
                      $"search sweep {data.SearchSweepRadius:0.#} m", SearchColor);
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
