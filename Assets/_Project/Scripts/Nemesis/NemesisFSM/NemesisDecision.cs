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
/// HOW THE LADDER IS EVALUATED
///
/// As a decision tree, built once from the rung list: one QuestionNode per rung, its false branch
/// being the rung below. See BuildTree. That is the same first-match-wins answer the for loop it
/// replaced produced, and it lifts the one ceiling a flat list has - a false branch can be a
/// different sub-ladder rather than always the next line down.
///
/// IT KEEPS NO MEMORY BETWEEN DECISIONS, WHICH IS THE PART THAT MATTERS. It does hold two things
/// now: the built tree (rebuilt when the ladder's shape changes) and a handful of per-pass fields
/// the closures read during a single walk. Neither carries anything from one frame's decision into
/// the next, so the property that counts is intact - no clock, no accumulated belief, nothing that
/// would make this a second state machine sitting above the real one. Everything it knows about
/// time still comes from
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

    /// <summary>
    /// Something in the corner of its eye that it has not resolved into a sighting yet.
    ///
    /// Deliberately goes false the moment SeesPlayer goes true, rather than both being true at
    /// once: they are two stages of one event, and a ladder where a rung can match both would fire
    /// the weaker response on the way into the stronger one.
    /// </summary>
    public bool IsSuspicious => stateManager.IsSuspicious;

    /// <summary>Its body is on the freight elevator - waiting for it, boarding, riding or stepping
    /// off. A fact about who is driving, not a sensor reading.</summary>
    public bool IsUsingElevator => stateManager.IsUsingElevator;

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

    /// <summary>
    /// A state held by hand from the test console, or null to let the ladder decide.
    ///
    /// Editor and development builds only, so it cannot ship as a way for anything to steer the
    /// Nemesis. See <see cref="Decide"/> for why pinning the ladder's answer is safe where writing
    /// NextState directly is not.
    /// </summary>
    public NemesisStateManager.ENemesisState? PinnedState { get; set; }

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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // PINNED FROM THE TEST CONSOLE, AND THE ONLY SAFE PLACE TO DO IT.
        //
        // "Force a state" is the obvious debug feature and the obvious implementation of it is
        // wrong: a state is requested by writing NextState, NemesisDecision is the only thing
        // allowed to write it, and a second writer makes the machine transition every frame and
        // never execute a single frame of any state. That reads in game as a monster staring at
        // you and twitching — see NemesisTestConsole's class doc, where it is the reason the panel
        // had no such button.
        //
        // Overriding the ladder's ANSWER has none of that problem. There is still exactly one
        // writer, the state is entered once and then runs normally, and everything downstream
        // behaves as it would if the ladder had chosen it for real. What you are looking at is the
        // genuine state, just entered for a reason you picked.
        //
        // Two caveats it is better to know than to discover: a state can still reject the entry
        // (Catch with nobody to grab falls straight back to Searching, by design), and a pinned
        // state whose preconditions are absent simply has nothing to do — pinned Traversing with
        // no route across floors stands still, which is correct and not a bug.
        if (PinnedState.HasValue)
        {
            LastRungIndex = -1;
            LastReason = $"FIJADO a mano ({PinnedState.Value})";
            return PinnedState.Value;
        }
#endif

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

        currentKey = current;
        holding = current.HasValue && dwell > 0f && stateManager.TimeInCurrentState < dwell;

        decided = null;
        LastRungIndex = -1;

        EnsureTree(ladder);
        root?.Execute();

        if (decided.HasValue) return decided.Value;

        // Nothing matched. Only reachable from a hand-edited ladder with no unconditional rung at
        // the bottom, or from inside the dwell window with no rung asking for the current state.
        // Staying put is the safe answer: the alternative is picking a state nobody asked for.
        LastReason = holding ? "esperando la histéresis" : "ninguna regla se cumplió";
        return current ?? NemesisStateManager.ENemesisState.Patrolling;
    }

    // ── The tree ────────────────────────────────────────────────────────────
    //
    // The ladder is the DATA and the tree is the STRUCTURE. Decide() used to be a for loop over
    // the rungs; it is now a walk down a tree built from those same rungs, which changes nothing
    // about which state wins and one thing about what the ladder is able to express.
    //
    // WHAT IT BUYS. A for loop can only ever ask the rungs in order: rung i not matching means
    // rung i+1, always. A tree's false branch is a child like any other, so a question can open
    // two different sub-ladders instead of one continuation - which is what a flat list cannot
    // say. HasBelief, for instance, is currently repeated across three separate rungs because
    // there was no way to ask it once. As shipped the tree is still a spine, rung after rung, and
    // that is deliberate: this change preserves the shipped behaviour exactly and only removes the
    // ceiling.
    //
    // WHAT IT MUST NOT BECOME. A SECOND VOTER. See the class comment: the last time two decision
    // layers wrote NextState the Nemesis changed state every frame and therefore never ran a
    // single UpdateState. The tree does not sit beside the ladder, it IS the ladder's evaluation.

    private ITreeNode root;

    /// <summary>The rungs the current tree was built from, in order. Compared element by element
    /// against the live ladder to notice a rebuild is due.</summary>
    private NemesisPriorityRung[] builtSnapshot;

    // Per-pass context. The tree is built once and walked every frame, so a question cannot close
    // over anything that changes per frame; these are written at the top of Decide() and read by
    // the closures during that same walk. It is what lets a static tree answer a moving world
    // without giving this class a clock of its own.
    private NemesisStateManager.ENemesisState? currentKey;
    private bool holding;
    private NemesisStateManager.ENemesisState? decided;

    /// <summary>
    /// Rebuilds the tree when the ladder's shape has changed, and not otherwise.
    ///
    /// ELEMENT BY ELEMENT, NOT BY COUNT OR BY REFERENCE. SO_NemesisPriorities.Rungs hands back the
    /// same List every time, and reordering a list changes neither its identity nor its length -
    /// so both of the cheap checks miss a reorder completely. And reordering is not a rare event:
    /// it is the ladder's whole authoring workflow, the reason it is an asset instead of code. A
    /// tree that did not notice would turn "drag a rule up" into "drag a rule up and restart Play",
    /// which is exactly the friction the asset exists to remove.
    ///
    /// What deliberately does NOT force a rebuild is editing a rung's conditions, its note, its
    /// enabled flag or its interrupts flag. Those are read live inside the closures below, so they
    /// take effect on the very next frame with no rebuild at all.
    /// </summary>
    private void EnsureTree(IReadOnlyList<NemesisPriorityRung> ladder)
    {
        if (root != null && MatchesSnapshot(ladder)) return;

        builtSnapshot = new NemesisPriorityRung[ladder.Count];
        for (int i = 0; i < ladder.Count; i++) builtSnapshot[i] = ladder[i];

        root = BuildTree(ladder);
    }

    private bool MatchesSnapshot(IReadOnlyList<NemesisPriorityRung> ladder)
    {
        if (builtSnapshot == null || builtSnapshot.Length != ladder.Count) return false;

        for (int i = 0; i < ladder.Count; i++)
        {
            if (!ReferenceEquals(builtSnapshot[i], ladder[i])) return false;
        }

        return true;
    }

    /// <summary>
    /// One QuestionNode per rung, each one's FALSE branch being the rung below it.
    ///
    /// Built back to front so every node already has its continuation to point at, and so the
    /// bottom rung's false branch is null - falling off the end of the tree decides nothing, which
    /// Decide() then reports as "ninguna regla se cumplio" exactly as the loop's fall-through did.
    /// </summary>
    private ITreeNode BuildTree(IReadOnlyList<NemesisPriorityRung> ladder)
    {
        ITreeNode next = null;

        for (int i = ladder.Count - 1; i >= 0; i--)
        {
            NemesisPriorityRung rung = ladder[i];

            // Copied into a local so each closure captures ITS OWN index rather than sharing the
            // loop variable, which in a foreach over a captured iteration variable is the classic
            // way to end up with every rung reporting the last index.
            int index = i;

            ITreeNode wins = new ActionNode(() => Win(rung, index));
            next = new QuestionNode(() => RungWins(rung), wins, next);
        }

        return next;
    }

    /// <summary>
    /// Whether this rung may win right now. Byte for byte the three tests the for loop ran, in the
    /// same order.
    ///
    /// The middle one is the hysteresis: inside the window, only an interrupt or a rung asking for
    /// where the Nemesis already is may win. Anything else is refused rather than allowed to fall
    /// through to a lower rung, so the window means "stay put" and not "pick the runner-up" - and
    /// because it lives inside the question, refusing here still hands control to the false branch
    /// and the walk carries on, which is the same shape the loop's `continue` had.
    /// </summary>
    private bool RungWins(NemesisPriorityRung rung)
    {
        if (rung == null || !rung.enabled) return false;

        if (holding && !rung.interrupts &&
            (!currentKey.HasValue || currentKey.Value != rung.target))
        {
            return false;
        }

        return Holds(rung);
    }

    /// <summary>
    /// The leaf: records which rung won and why, and stops the walk by virtue of having no
    /// children.
    ///
    /// It writes a field rather than returning, because ActionNode returns void - a tree that has
    /// to produce a value works by having its leaves put the value somewhere the caller reads
    /// afterwards. That is not a limitation worth designing around here: LastRungIndex and
    /// LastReason already had to be written as a side effect for the debug HUD, so the leaf was
    /// always going to be doing this much.
    /// </summary>
    private void Win(NemesisPriorityRung rung, int index)
    {
        LastRungIndex = index;
        LastReason = string.IsNullOrEmpty(rung.note) ? rung.target.ToString() : rung.note;
        decided = rung.target;
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
            ENemesisPredicate.IsSuspicious => IsSuspicious,
            ENemesisPredicate.IsUsingElevator => IsUsingElevator,
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
