using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A named patrol route/zone for the Nemesis: an ordered sequence of waypoints, how often this
/// route gets picked relative to the other routes on the same <see cref="NemesisController"/>,
/// and whether it is unlocked yet.
///
/// Waypoints are not dragged into an inspector list one by one: this component collects its own
/// direct children tagged <see cref="WaypointTag"/>, in Hierarchy order, in <see cref="Awake"/>.
/// Reordering the route means reordering the children in the Hierarchy window. This keeps a route
/// a plain, obvious group in the scene tree (Tier 3.1 task: "componente agrupador en jerarquía").
/// </summary>
public class NemesisRoute : MonoBehaviour
{
    /// <summary>
    /// Tag every waypoint child must have to be picked up by <see cref="Awake"/>. Created by hand
    /// in Project Settings > Tags and Layers — see Tier 3.1 setup notes.
    /// </summary>
    public const string WaypointTag = "NemesisWaypoint";

    [Tooltip("Relative frequency this route is picked among the unlocked routes on the " +
             "NemesisController, i.e. how often this zone gets patrolled. 0 effectively " +
             "disables it without unassigning it from the controller.")]
    [SerializeField] private float weight = 1f;

    [Tooltip("Whether this route/zone is selectable from the start of the level. Leave off for " +
             "routes that should stay closed until NemesisController.UnlockRoute() is called " +
             "(e.g. gated behind puzzle progress).\n\n" +
             "Ignored when 'Unlocked By Puzzle Id' is filled in — the puzzle owns the gate then.")]
    [SerializeField] private bool startUnlocked = true;

    [Tooltip("Puzzle that opens this route/zone. Must match the PuzzleId on the puzzle's " +
             "SO_PuzzleData / SO_ValvePuzzleData / etc.\n\n" +
             "Filling this in is the whole setup: the route wires itself to the puzzle and no " +
             "one has to call UnlockRoute() by hand. Leave empty to fall back to 'Start Unlocked'.")]
    [PuzzleId]
    [SerializeField] private string unlockedByPuzzleId;

    private readonly List<Transform> waypoints = new List<Transform>();
    private bool isUnlocked;

    private bool IsPuzzleGated => !string.IsNullOrWhiteSpace(unlockedByPuzzleId);

    /// <summary>
    /// How often this route gets picked, as everything that rolls a route sees it: the weight the
    /// designer authored, times whatever the <see cref="NemesisDirector"/> is currently doing to
    /// it.
    ///
    /// Two values and not one on purpose. The authored number is the design — "this corner is not
    /// worth patrolling often" — and it has to survive being leaned on: a route weighted 0.2 stays
    /// the least frequent of its neighbours even under maximum pressure, and a route switched off
    /// at 0 stays off, because zero times anything is still zero. A director that SET the weight
    /// instead of scaling it would flatten exactly the tuning it is supposed to bend.
    /// </summary>
    public float Weight => weight * pressureMultiplier;

    /// <summary>The weight as authored, ignoring any pressure. For tooling and gizmos that should
    /// show what the designer typed rather than what the roll is currently seeing.</summary>
    public float AuthoredWeight => weight;

    public bool IsUnlocked => isUnlocked;

    /// <summary>
    /// Runtime-only, never serialised: pressure is a thing that is happening, not a property of
    /// the level, and a multiplier left in an asset by a playtest is a mystery for whoever opens
    /// the scene next.
    /// </summary>
    private float pressureMultiplier = 1f;

    /// <summary>Scales this route's frequency for as long as the Director says so. 1 is "no
    /// pressure" and is what every path out of a request restores.</summary>
    public void SetPressureMultiplier(float multiplier) =>
        pressureMultiplier = Mathf.Max(0f, multiplier);

    /// <summary>Ordered waypoints for this route, collected from tagged direct children.</summary>
    public IReadOnlyList<Transform> Waypoints => waypoints;

    private void Awake()
    {
        // A puzzle id wins over startUnlocked: the route starts closed and the puzzle opens it.
        // Otherwise a designer who fills in the id but forgets to untick the checkbox gets a
        // route that was never gated at all — and it fails silently, which is the worst kind.
        isUnlocked = !IsPuzzleGated && startUnlocked;
        CollectWaypoints();
    }

    // ── Puzzle gating ───────────────────────────────────────────────────────
    //
    // Same shape as Checkpoint's own puzzle hook (subscribe + catch-up in Start), on purpose:
    // one pattern for "this thing turns on when a puzzle is solved" across the project, so
    // setting up a gated route is the same job as setting up a gated checkpoint.

    private void OnEnable()
    {
        if (!IsPuzzleGated) return;
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
    }

    private void OnDisable()
    {
        if (!IsPuzzleGated) return;
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
    }

    private void Start()
    {
        // Catch-up: the puzzle may already be solved by the time this route loads (the level
        // comes in additively, or a snapshot was restored). The event only fires on the
        // transition, so without this the route would stay closed for the rest of the run.
        if (!IsPuzzleGated || !PuzzleStateManager.Exists) return;

        if (PuzzleStateManager.Instance.IsPuzzleCompleted(unlockedByPuzzleId)) Unlock();
    }

    private void HandlePuzzleCompleted(string completedId)
    {
        if (completedId != unlockedByPuzzleId) return;
        Unlock();
    }

    private void CollectWaypoints()
    {
        waypoints.Clear();

        // Direct children only, in Hierarchy order: a route is a flat sequence, not a tree, and
        // sibling order is exactly the order a designer can see and rearrange in the Hierarchy.
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.CompareTag(WaypointTag)) waypoints.Add(child);
        }

        if (waypoints.Count == 0)
            Debug.LogWarning($"[NemesisRoute] '{name}' has no children tagged '{WaypointTag}' — " +
                             "it will never be picked as a usable patrol route.", this);
    }

    /// <summary>
    /// Opens this route up for selection. Called by NemesisController.UnlockRoute(), and by this
    /// component itself when <see cref="unlockedByPuzzleId"/> is solved. Idempotent.
    ///
    /// Note the route stays open after a capture rolls puzzle progress back: RestoreSnapshot
    /// deliberately raises no events, and re-closing a zone mid-run would read as a bug to the
    /// player, not as a consequence.
    /// </summary>
    public void Unlock() => isUnlocked = true;

    /// <summary>
    /// Re-scans the tagged children. Only needed if waypoints are spawned or reparented under
    /// this route at runtime — a static hand-placed route never needs this.
    /// </summary>
    public void RefreshWaypoints() => CollectWaypoints();

    /// <summary>
    /// Visualizes the route in the Scene view so the hand-built waypoint hierarchy can be
    /// checked without entering Play mode. Scans children directly instead of using the cached
    /// <see cref="waypoints"/> list, which is only populated at runtime.
    ///
    /// Colour tells the gate apart at a glance: cyan = open, amber = closed. Out of Play mode
    /// that reads the configured intent (a puzzle-gated route draws amber because that is how
    /// it will start), in Play mode it reads the live state.
    ///
    /// Unlabelled on purpose — every route in a level, all at once. Names live in
    /// <see cref="OnDrawGizmosSelected"/> instead: a level with a dozen routes drawing every
    /// waypoint's name over every other route's, all the time, stops being readable well before
    /// it stops being technically correct.
    ///
    /// The one condition is <see cref="NemesisGizmos.DrawingEnabled"/>, the master switch on the
    /// Nemesis itself. The routes are the bulk of what is on screen — a dozen polylines through
    /// the whole level — so a switch that turned off the Nemesis's own rings and left these would
    /// not have cleared anything worth clearing.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!NemesisGizmos.DrawingEnabled) return;

        bool openNow = Application.isPlaying ? isUnlocked : (!IsPuzzleGated && startUnlocked);
        Gizmos.color = openNow ? new Color(0.2f, 0.8f, 1f) : new Color(1f, 0.65f, 0.1f);

        Transform previous = null;
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (!child.CompareTag(WaypointTag)) continue;

            Gizmos.DrawWireSphere(child.position, 0.35f);
            if (previous != null) Gizmos.DrawLine(previous.position, child.position);
            previous = child;
        }
    }

    /// <summary>
    /// Names — this route and every one of its waypoints — drawn only while the route (the
    /// waypoints' parent) is the selected object. Click it to read it; every other route in the
    /// level stays as the plain spheres and lines above.
    ///
    /// Each waypoint carries its own scene name and its order in the route. The name matters
    /// because it is the only handle NemesisSetupValidator's report gives you ("Waypoint (11)
    /// does not land on the NavMesh") — without it, finding which of a dozen identical-looking
    /// spheres that refers to means opening the Hierarchy and counting. The order matters because
    /// two waypoints sitting close together make the polyline alone ambiguous about which end is
    /// the start.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        // Suppressed too, not just the always-on polyline above. A master switch that still let
        // the labels through the moment you clicked a route would not be one — and clicking a
        // route is exactly what you do while dressing the level, which is when the switch is off.
        if (!NemesisGizmos.DrawingEnabled) return;

        bool openNow = Application.isPlaying ? isUnlocked : (!IsPuzzleGated && startUnlocked);
        Color color = openNow ? new Color(0.2f, 0.8f, 1f) : new Color(1f, 0.65f, 0.1f);

        Transform first = null;
        int index = 0;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (!child.CompareTag(WaypointTag)) continue;

            if (first == null) first = child;

            DrawWaypointLabel(child, index, color);
            index++;
        }

        DrawRouteLabel(first, openNow, color);
    }

    private static void DrawWaypointLabel(Transform waypoint, int index, Color color)
    {
#if UNITY_EDITOR
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.Label(waypoint.position + Vector3.up * 0.45f, $"#{index} {waypoint.name}");
#endif
    }

    /// <summary>
    /// Names the route itself — GameObject name, open/closed, weight — above its first waypoint,
    /// or at the route's own transform when it has none at all. That second case is deliberate: a
    /// route with zero tagged children is exactly what the validator flags as "will never be
    /// picked", and an empty route with no label is the hardest of all of these to locate by eye.
    /// </summary>
    private void DrawRouteLabel(Transform first, bool openNow, Color color)
    {
#if UNITY_EDITOR
        Vector3 position = first != null ? first.position : transform.position;
        string state = openNow ? "abierta" : "cerrada";

        UnityEditor.Handles.color = color;
        UnityEditor.Handles.Label(position + Vector3.up * 0.9f, $"{name} ({state}, peso {weight:0.##})");
#endif
    }
}
