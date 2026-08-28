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
///     behind per-block toggles instead, so the cost of always drawing is a checkbox.
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
    [Header("Vision")]
    [Tooltip("Vision cone at full range, with the real ViewAngle. An arc and two edges rather " +
             "than a sphere: the angle is half the information and a sphere throws it away.")]
    [SerializeField] private bool drawVisionCone = true;

    [Tooltip("The same cone shortened by CrouchVisionMultiplier — how close you can get while " +
             "crouched. Usually far smaller than anyone expects.")]
    [SerializeField] private bool drawCrouchedVisionCone = true;

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

    private void OnDrawGizmos()
    {
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
    private void DrawCone(Transform eye, float range, float angle, Color color, string label)
    {
        if (range <= 0.01f) return;

        Vector3 origin = eye.position;
        float half = Mathf.Clamp(angle, 0f, 360f) * 0.5f;

        Gizmos.color = color;

        Vector3 previous = origin + DirectionAt(eye, -half) * range;
        Gizmos.DrawLine(origin, previous);

        for (int i = 1; i <= arcSegments; i++)
        {
            float t = (float)i / arcSegments;
            Vector3 point = origin + DirectionAt(eye, Mathf.Lerp(-half, half, t)) * range;

            Gizmos.DrawLine(previous, point);
            previous = point;
        }

        Gizmos.DrawLine(origin, previous);

        DrawLabel(origin + eye.forward * range, label, color);
    }

    /// <summary>Direction <paramref name="degrees"/> off the transform's forward, flattened so a
    /// Nemesis on a ramp still draws its cone level with the floor.</summary>
    private static Vector3 DirectionAt(Transform eye, float degrees)
    {
        Vector3 forward = eye.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;

        return Quaternion.AngleAxis(degrees, Vector3.up) * forward.normalized;
    }

    // ── Hearing ─────────────────────────────────────────────────────────────

    private void DrawHearing(SO_NemesisData data, NemesisStateManager manager)
    {
        if (!drawHearing || data.ListenRange <= 0.01f) return;

        FieldOfListening listening = manager.FieldOfListening;
        Vector3 origin = listening != null ? listening.transform.position : transform.position;

        DrawDisc(origin, data.ListenRange, HearingColor);
        DrawLabel(origin + Vector3.right * data.ListenRange,
                  $"hearing {data.ListenRange:0.#} m", HearingColor);

        if (!data.WallOcclusionEnabled) return;

        // The attenuated rings are the ranges that apply most of the time: the Nemesis is almost
        // never in an open room with the player. Dimmed so the full radius stays the outer edge.
        Color faded = new Color(HearingColor.r, HearingColor.g, HearingColor.b, 0.45f);

        DrawDisc(origin, data.ListenRange * data.WallOcclusionMultiplier, faded);
        DrawDisc(origin, data.ListenRange * data.FloorOcclusionMultiplier, faded);
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
        // tested separately (see NemesisChasingState.CanActuallyReachPlayer). A sphere would
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
