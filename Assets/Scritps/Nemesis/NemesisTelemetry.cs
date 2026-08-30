using UnityEngine;

/// <summary>
/// Everything the Nemesis reports about itself to the rest of the game: the two HUD vignettes and
/// the per-state audio loops, all through <see cref="NemesisEvents"/>.
///
/// Extracted from NemesisStateManager, which had grown to twelve separate jobs. This is the
/// easiest of them to justify pulling out: nothing here decides anything. It reads the FSM's
/// current state and the distance to the player and announces both. It never steers, never
/// transitions, never touches the agent. A class that only observes and broadcasts has no
/// business sharing a file with the class that navigates.
///
/// Raised from one place and not from each state's EnterState, which is the property worth
/// keeping: "am I being chased" is a question about a SET of states (Chasing plus an unresolved
/// Catch), and spreading that across five states is how the definition drifts.
///
/// SETUP: goes on the Nemesis root. NemesisStateManager finds it, adds it if missing, and drives
/// it — this component has no Update of its own on purpose, because the order matters (see
/// <see cref="TickProximity"/>).
/// </summary>
public class NemesisTelemetry : MonoBehaviour
{
    private NemesisStateManager stateManager;

    private float proximityRecalcTimer;
    private float proximityTarget;
    private float proximityEmitted;

    private bool wasBeingChased;
    private NemesisStateManager.ENemesisState? lastReportedState;

    /// <summary>Called by NemesisStateManager during its Awake, so this is wired before any tick.
    /// </summary>
    public void Initialize(NemesisStateManager manager) => stateManager = manager;

    /// <summary>
    /// Where the Searching state is currently aiming its cut-off, or null when it is not searching
    /// or fell back to the sweep.
    ///
    /// It lives here and not on the state manager because reporting is this class's whole job, and
    /// because the facade should not be reaching into a specific state's internals to answer a
    /// question about it. Nothing in the FSM reads this — the debug HUD and the gizmos do.
    ///
    /// It is worth surfacing at all because the interception is the one decision in the system
    /// with no visible tell: a Nemesis heading somewhere clever and a Nemesis heading somewhere by
    /// accident look identical from outside, and the absence of a point is information too — it
    /// means the belief, the heading or the waypoints were not good enough to commit to.
    /// </summary>
    public Vector3? SearchInterceptPoint
    {
        get
        {
            if (stateManager == null) return null;

            NemesisSearchingState searching = stateManager.SearchingState;
            return searching != null && searching.HasIntercept
                ? searching.InterceptPoint
                : (Vector3?)null;
        }
    }

    // ── Proximity ───────────────────────────────────────────────────────────

    /// <summary>
    /// Intensity of the proximity vignette: 0 out of range, 1 right on top of the player.
    ///
    /// Ticked by NemesisStateManager BEFORE the FSM and not after, which is not a detail. It used
    /// to sit below base.Update(), so anything a state threw — an unassigned Animator, an agent
    /// knocked off the NavMesh by a Warp that did not land — took this down with it every frame.
    /// The first symptom anyone noticed was the vignette silently never lighting up, which points
    /// at the UI instead of at the state that was actually failing.
    ///
    /// Uses the player's real position rather than FieldOfView.LastKnownPosition, because
    /// proximity has to rise even if the Nemesis has never seen you — that is exactly the warning
    /// that it is close by without you knowing.
    ///
    /// The distance is measured over the NavMesh and not in a straight line. This was the "the
    /// threat UI shows up when it is on another floor" bug: the Nemesis on floor 0 directly below
    /// the player is 4 metres away as the crow flies — vignette near maximum — and half a storey
    /// away on foot. Path distance gives the real 40 metres and the vignette stays dark, which is
    /// the honest reading.
    /// </summary>
    public void TickProximity()
    {
        SO_NemesisData data = stateManager.NemesisData;
        Transform player = stateManager.PlayerTransform;

        float radius = data != null ? data.ProximityRadius : 0f;

        if (player == null || radius <= 0f)
        {
            proximityTarget = 0f;
            proximityEmitted = 0f;
            NemesisEvents.ProximityChanged(0f);
            return;
        }

        // Straight-line prefilter, which is free: no path can be shorter than the straight line,
        // so outside the radius in a straight line it is already 0 with nothing computed. This is
        // what avoids paying for a CalculatePath during the 95% of the run when it is far away.
        float straightLine = Vector3.Distance(transform.position, player.position);
        if (straightLine >= radius)
        {
            proximityRecalcTimer = 0f;
            proximityTarget = 0f;
        }
        else if (data == null || !data.ProximityUsesPathDistance)
        {
            proximityTarget = 1f - Mathf.Clamp01(straightLine / radius);
        }
        else
        {
            RecalculatePathProximity(data, player, radius);
        }

        // Interpolated rather than written straight through: the measurement runs at ~5Hz and the
        // vignette reads the value every frame, so without this it looks stepped when moving fast.
        float interval = data != null ? Mathf.Max(0.05f, data.ProximityRecalcInterval) : 0.2f;
        proximityEmitted = Mathf.MoveTowards(proximityEmitted, proximityTarget, Time.deltaTime / interval);

        NemesisEvents.ProximityChanged(proximityEmitted);
    }

    /// <summary>
    /// Refreshes <see cref="proximityTarget"/> with path distance, at most once every
    /// ProximityRecalcInterval.
    ///
    /// With no complete path the result is 0, not "very far": if the player is on a NavMesh island
    /// the Nemesis cannot reach, there is no threat to announce however much the straight line
    /// insists they are two metres apart.
    /// </summary>
    private void RecalculatePathProximity(SO_NemesisData data, Transform player, float radius)
    {
        proximityRecalcTimer -= Time.deltaTime;
        if (proximityRecalcTimer > 0f) return;

        proximityRecalcTimer = Mathf.Max(0.05f, data.ProximityRecalcInterval);

        proximityTarget = NemesisNav.TryGetPathDistance(transform.position, player.position,
                                                        out float pathDistance)
            ? 1f - Mathf.Clamp01(pathDistance / radius)
            : 0f;
    }

    // ── State transitions ───────────────────────────────────────────────────

    /// <summary>
    /// Announces the two FSM-derived events. Ticked after the FSM, so it reports the state the
    /// Nemesis is actually in this frame.
    /// </summary>
    public void TickStateEvents(bool isCaptureResolved)
    {
        EmitChaseTransitions(isCaptureResolved);
        EmitStateTransitions();
    }

    /// <summary>
    /// Raises ChaseStarted/ChaseEnded when entering and leaving the "it is hunting you" set.
    ///
    /// Catch is part of the set on purpose: if it were cut when leaving Chasing, the red vignette
    /// would switch off on the very frame it grabs you, which is when it needs to be showing the
    /// most.
    ///
    /// Traversing is NOT in the set, deliberately. It is a pursuit, but the player is a storey
    /// away and cannot be reached — holding the red vignette lit through a lift ride would spend
    /// the strongest signal the HUD has on a threat that is, right now, somewhere else.
    /// </summary>
    private void EmitChaseTransitions(bool isCaptureResolved)
    {
        NemesisStateManager.ENemesisState? key = stateManager.CurrentStateKey;

        // Catch only counts while the capture is unresolved. Once the player has respawned at a
        // checkpoint it is free again, and leaving the red vignette lit for the whole grace
        // period would tell it it is still being hunted when it is not.
        bool isBeingChased = key.HasValue &&
                             (key.Value == NemesisStateManager.ENemesisState.Chasing ||
                              (key.Value == NemesisStateManager.ENemesisState.Catch && !isCaptureResolved));

        if (isBeingChased == wasBeingChased) return;

        wasBeingChased = isBeingChased;

        if (isBeingChased) NemesisEvents.ChaseStarted();
        else               NemesisEvents.ChaseEnded();
    }

    /// <summary>
    /// Raises <see cref="NemesisEvents.StateChanged"/> once per FSM transition.
    ///
    /// Detected by comparing against the last reported key, rather than by editing the six states'
    /// EnterState methods: one place instead of six, and no change to the shared FSM base.
    /// </summary>
    private void EmitStateTransitions()
    {
        NemesisStateManager.ENemesisState? key = stateManager.CurrentStateKey;
        if (!key.HasValue) return;

        if (lastReportedState.HasValue && lastReportedState.Value == key.Value) return;

        lastReportedState = key.Value;
        NemesisEvents.StateChanged(key.Value);
    }

    /// <summary>
    /// Closes an open chase without a state transition to trigger it.
    ///
    /// Needed because ChaseStarted/ChaseEnded is a PAIR held open across frames, and the thing
    /// that would normally close it is the FSM ticking — which stops happening the moment the
    /// Nemesis is disabled. Left unclosed, the red vignette stays burned over the HUD for the rest
    /// of the run with nothing alive to take it down.
    /// </summary>
    public void CloseChase()
    {
        if (!wasBeingChased) return;

        wasBeingChased = false;
        NemesisEvents.ChaseEnded();
    }
}
