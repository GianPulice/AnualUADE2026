using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Decides WHICH state the Nemesis should be in. The FSM owns entering it, running it and leaving
/// it.
///
/// WHY THE DECISION LEFT THE STATES
///
/// Every transition used to be written inside the state it left from, which meant each state had
/// to know about the others. That cost is documented all over the old code: NemesisTraversingState
/// exists in large part because Chasing and Searching each re-derived the same borderline path
/// verdict and read it differently, and NemesisSearchingState carried a hand-placed
/// MinimumDwellTime to stop the two of them trading the Nemesis every other frame. Both are
/// symptoms of the same thing — a decision taken in six places cannot be made consistent, only
/// patched.
///
/// Here it is one prioritised ladder, read top to bottom, and the patches become visible rules.
///
/// WHY THIS IS AN EVALUATOR AND THE LADDER IS AN ASSET
///
/// The ladder itself lives in <see cref="SO_NemesisPriorities"/>, which is a reorderable list the
/// designer edits without a recompile. This class is the part that cannot live in an asset: the
/// predicates. Each one is a single side-effect-free question about the world, and every rung in
/// the asset is a reference to one of them — so the asset can reorder the reasoning but can never
/// hold a second, subtly different definition of what "sees the player" means.
///
/// That split is what a Unity Behavior graph was going to provide, and it is why the graph came
/// back out: every branch of it was [conditions] -> "put the Nemesis in X" hung off one Selector,
/// which is a list drawn sideways. What it added on top of the list was a package dependency, an
/// asset that only merges through UnityYAMLMerge, eight wrapper classes that did nothing but call
/// the predicates below — and, in practice, a SECOND voter: the graph agent ticked itself from
/// its own Update while this ladder ticked from NemesisStateManager, both writing the same
/// NextState channel. Two voters is how the Nemesis ended up changing state every frame and
/// therefore never running a single UpdateState, which reads in game as a monster that sees you
/// and stands there twitching.
///
/// IT IS STATELESS ON PURPOSE. Everything it needs to know about time comes from
/// <see cref="StateManager{EState}.TimeInCurrentState"/> and
/// <see cref="NemesisStateManager.BeliefAge"/>. A decision layer with its own memory is a second
/// state machine, and then there are two. The dwell hysteresis below is measured the same way,
/// which is what lets it exist without giving this class a clock.
/// </summary>
public sealed class NemesisDecision
{
    private readonly NemesisStateManager stateManager;

    /// <summary>
    /// The ladder used when the Nemesis has no <see cref="SO_NemesisPriorities"/> assigned.
    ///
    /// Built from the same static method the asset's own Reset() uses, so "the default" is one
    /// definition and not two. Static because it never varies and there is one Nemesis; built
    /// lazily so a project that always assigns the asset never pays for it.
    /// </summary>
    private static List<NemesisPriorityRung> fallbackLadder;

    /// <summary>
    /// Static state survives leaving Play mode when domain reload is disabled. Nothing here is
    /// per-session — the list is rebuilt identically every time — but a stale one would be built
    /// from whatever the code said in the previous session, which is confusing to debug for no
    /// gain.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => fallbackLadder = null;

    public NemesisDecision(NemesisStateManager manager)
    {
        stateManager = manager;
    }

    private SO_NemesisData Data => stateManager.NemesisData;

    private IReadOnlyList<NemesisPriorityRung> Ladder
    {
        get
        {
            SO_NemesisPriorities priorities = stateManager.Priorities;
            if (priorities != null && priorities.Rungs != null && priorities.Rungs.Count > 0)
                return priorities.Rungs;

            return fallbackLadder ??= SO_NemesisPriorities.BuildDefaultLadder();
        }
    }

    // ── Predicates ──────────────────────────────────────────────────────────
    //
    // Public and side-effect free. Each one is exactly one question, so a rung in the asset is a
    // reference to a property rather than a second copy of the reasoning.

    public bool SeesPlayer => stateManager.HasVisualTarget;

    public bool HearsPlayer => stateManager.HasAudioTarget;

    public bool HasBelief => stateManager.TryGetBelief(out _);

    /// <summary>Seconds since either sensor last caught the player. Infinity if neither ever has.
    /// </summary>
    public float BeliefAge => stateManager.BeliefAge;

    public bool IsIn(NemesisStateManager.ENemesisState key) => stateManager.CurrentStateKey == key;

    /// <summary>Whether the agent has reached the end of its current path.</summary>
    public bool HasArrived => stateManager.HasArrived;

    /// <summary>Whether the player is close enough, level enough and unobstructed enough to grab,
    /// and the post-capture cooldown has expired.</summary>
    public bool CanCatchPlayer => stateManager.CanEnterCatch && stateManager.CanReachPlayerNow;

    /// <summary>
    /// Whether getting to where the Nemesis believes the player is means taking the freight
    /// elevator.
    ///
    /// GOES THROUGH THE THROTTLED ORACLE, AND THAT IS NOT AN OPTIMISATION. NemesisPathOracle holds
    /// one answer for RouteVerdictInterval seconds so that everything asking this question reads
    /// the SAME number. Querying NemesisNav directly here would give a freshly measured verdict
    /// that can differ from the one taken a frame ago on a borderline path — which is precisely
    /// the flip that used to leave the Nemesis shuddering in place directly below the player, and
    /// which NemesisTraversingState was built to stop. It would come back looking like a new bug.
    /// </summary>
    public bool RouteToBeliefCrossesFloors
    {
        get
        {
            if (!stateManager.TryGetBelief(out Vector3 belief)) return false;

            return stateManager.TryGetThrottledRoute(belief, out NemesisNav.NavRoute route) &&
                   stateManager.IsRouteAcrossFloors(route);
        }
    }

    // ── Why it decided what it decided ──────────────────────────────────────

    /// <summary>Index of the rung that won this frame, or -1 when none did. For the debug HUD:
    /// without it, "why is it doing this" is not a question anyone can answer while playing.
    /// </summary>
    public int LastRungIndex { get; private set; } = -1;

    /// <summary>The winning rung's note, or a short explanation when nothing won.</summary>
    public string LastReason { get; private set; } = "—";

    // ── The ladder ──────────────────────────────────────────────────────────

    /// <summary>
    /// The state the Nemesis should be in this frame: the first rung whose conditions all hold.
    ///
    /// Order is the whole design, and it is the designer's to change — see
    /// <see cref="SO_NemesisPriorities"/> for the shipped order and why it reads the way it does.
    /// </summary>
    public NemesisStateManager.ENemesisState Decide()
    {
        IReadOnlyList<NemesisPriorityRung> ladder = Ladder;
        NemesisStateManager.ENemesisState? current = stateManager.CurrentStateKey;

        // THE HYSTERESIS WINDOW, AND WHY THE LADDER NEEDS ONE.
        //
        // Every predicate above is sampled fresh each frame, and two of them come off sensors
        // that sweep on their own cadence. A player sitting exactly at the edge of the hearing
        // range flips HearsPlayer on and off several times a second, and each flip is a genuine
        // state change: the machine exits, enters, and — because StateManager.Update runs either
        // UpdateState OR a transition, never both — never gets to run a single frame of the state
        // it just entered. Nothing sets a destination, so the Nemesis stands still changing its
        // mind, which is exactly what it looks like from the outside.
        //
        // Holding the answer for a fraction of a second costs nothing a player can perceive and
        // removes the entire class of problem. The two things that must never wait — seeing the
        // player, and being able to grab them — are marked as interrupts in the asset.
        //
        // Measured off TimeInCurrentState rather than a counter here, which is what keeps this
        // class stateless: no clock of its own means no second state machine.
        float dwell = DwellWindow;
        bool holding = current.HasValue && dwell > 0f && stateManager.TimeInCurrentState < dwell;

        for (int i = 0; i < ladder.Count; i++)
        {
            NemesisPriorityRung rung = ladder[i];
            if (rung == null || !rung.enabled) continue;

            // Inside the window, only an interrupt or a rung asking for where it already is may
            // win. Anything else is skipped rather than allowed to fall through to a lower rung,
            // so the window means "stay put", not "pick the runner-up".
            if (holding && !rung.interrupts && current.Value != rung.target) continue;

            if (!Holds(rung)) continue;

            LastRungIndex = i;
            LastReason = string.IsNullOrEmpty(rung.note) ? rung.target.ToString() : rung.note;
            return rung.target;
        }

        // Nothing matched. Only reachable from a hand-edited ladder with no unconditional rung at
        // the bottom, or from inside the dwell window with no rung asking for the current state.
        // Staying put is the safe answer: the alternative is picking a state nobody asked for.
        LastRungIndex = -1;
        LastReason = holding ? "esperando la histéresis" : "ninguna regla se cumplió";
        return current ?? NemesisStateManager.ENemesisState.Patrolling;
    }

    private float DwellWindow
    {
        get
        {
            SO_NemesisPriorities priorities = stateManager.Priorities;
            return priorities != null ? priorities.MinimumStateDwell : 0.35f;
        }
    }

    /// <summary>Whether every condition on a rung holds. An empty list holds, which is what makes
    /// the bottom rung the ladder's safety net.</summary>
    private bool Holds(NemesisPriorityRung rung)
    {
        List<NemesisCondition> conditions = rung.conditions;
        if (conditions == null) return true;

        for (int i = 0; i < conditions.Count; i++)
        {
            if (!Evaluate(conditions[i])) return false;
        }

        return true;
    }

    private bool Evaluate(NemesisCondition condition)
    {
        bool value = condition.predicate switch
        {
            ENemesisPredicate.SeesPlayer => SeesPlayer,
            ENemesisPredicate.HearsPlayer => HearsPlayer,
            ENemesisPredicate.HasBelief => HasBelief,
            ENemesisPredicate.CanCatchPlayer => CanCatchPlayer,
            ENemesisPredicate.RouteToBeliefCrossesFloors => RouteToBeliefCrossesFloors,
            ENemesisPredicate.HasArrived => HasArrived,
            ENemesisPredicate.IsInState => IsIn(condition.state),
            ENemesisPredicate.BeliefAgeUnder => BeliefAge < Resolve(condition),
            ENemesisPredicate.TimeInStateUnder => stateManager.TimeInCurrentState < Resolve(condition),
            _ => false,
        };

        return condition.negate ? !value : value;
    }

    /// <summary>
    /// The number behind a time condition, read off <see cref="SO_NemesisData"/>.
    ///
    /// Falls back to the same defaults the states used before the ladder existed, so a Nemesis
    /// with no data asset degrades to the shipped tuning rather than to zero — which would make
    /// every "under" condition false and strand it in whatever state it happened to be in.
    /// </summary>
    private float Resolve(NemesisCondition condition)
    {
        if (condition.threshold == ENemesisThreshold.Custom) return condition.customSeconds;

        SO_NemesisData data = Data;

        return condition.threshold switch
        {
            ENemesisThreshold.VisionLossGracePeriod => data != null ? data.VisionLossGracePeriod : 2f,
            ENemesisThreshold.InvestigationTimeOut => data != null ? data.InvestigationTimeOut : 8f,
            ENemesisThreshold.SearchTimeOut => data != null ? data.SearchTimeOut : 4f,
            ENemesisThreshold.ElevatorCommitTime => data != null ? data.ElevatorCommitTime : 12f,
            ENemesisThreshold.BeliefMemoryTime => data != null ? data.BeliefMemoryTime : 45f,
            _ => 0f,
        };
    }
}
