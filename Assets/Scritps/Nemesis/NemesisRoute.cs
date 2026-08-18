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
    [SerializeField] private string unlockedByPuzzleId;

    private readonly List<Transform> waypoints = new List<Transform>();
    private bool isUnlocked;

    private bool IsPuzzleGated => !string.IsNullOrWhiteSpace(unlockedByPuzzleId);

    public float Weight => weight;
    public bool IsUnlocked => isUnlocked;

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
    /// </summary>
    private void OnDrawGizmos()
    {
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
}
