using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Looking for the player where they can be NOW.
///
/// WHAT CHANGED ON 04/10 (Plan-Busqueda-Nemesis Fase 2b). Since 27/09 this state swept a DISC of
/// NavMesh points around the belief (NemesisFreeRoam): centred on the last evidence, sized off its
/// precision, three to eight metres wide, clipped by the walls its centre could see. That finds the
/// player when they are inside the disc, and it had two faults no weighting could fix. A disc has no
/// direction: in a corridor it searched back the way it had come as readily as onwards (WIR-062). And
/// a disc stops at the door: it could not follow the player through the only exit there was.
///
/// Now it reads the POSSIBILITY MAP (NemesisPossibilityMap, plan §3): where the player can be by now,
/// given where it sensed them, how fast they run and everything it has looked at since.
///   - IT GOES WHERE THE VALUE IS. NemesisSearchPicker cuts what the map still holds on the floor into
///     a few places and rolls among them by value ÷ (1 + seconds to walk there). The value only moves
///     along walkable edges and whatever the Nemesis looks at is emptied, so there is nothing behind
///     it to go back for, and what is left has already run on through the door.
///   - LOOKING IS THE MEMORY. It keeps no list of where it has been. A place it has looked at is a
///     place the map holds no value in, and that is the only reason it does not go back there.
///   - IT DOES NOT WALK ALL THE WAY TO A PLACE IT KNOWS IS EMPTY. When the place it is heading to has
///     lost most of the value it was picked for — it saw it from down the corridor, or new evidence
///     moved the value — it picks again (SearchPickRules.LostItsValue).
///   - NEW EVIDENCE ABOUT THE PLAYER re-seeds the map by itself: the facade does it, on its own tick.
///     This state only asks the same question again — did the place I am going to keep its value?
///     If it did (the player was heard again around there) it carries on, and finishes its look
///     first. If it did not, it goes where the value now is, at once, pause or no pause. A decoy or
///     a Director pulse never moves it: leads do not touch the map (D18), and competing for
///     attention is the ladder's business.
///   - IT STOPS AND LOOKS AROUND at every place it REACHES (SearchPauseTime: NemesisLookAround sweeps
///     the gaze while IsPausing, and that gaze is what clears the map). Not on entering: see
///     EnterState.
///   - IT STANDS ON THE EVIDENCE POINT ITSELF only when that point means something — a sighting, or a
///     noise pinned down beside it (MayVisitEvidence; Plan-Busqueda Fase 1, WIR-057). A vaguer noise
///     is an area the map spread the value over, and the search goes to places in it, never to the
///     last footstep: that is usually a locker door.
///   - WITH NO BELIEF (entered straight from a capture, or on a known hiding spot alone: the facade
///     keeps the map empty) there is nothing to reason from, and it scatters around where it stood on
///     entering, as it always did. The map decides whenever there is a belief for it to hold; the
///     scatter only when there never was one. Never both.
///
/// HOW LONG IT LASTS (plan §18.5 B, Fase 2B part 3): it cools down instead of expiring. The ladder's
/// "la búsqueda sigue tibia" reads <see cref="IsWarm"/>: the search goes on while the silence since
/// the last evidence about the player — counted from when it got to the first place that evidence
/// sent it, see <see cref="Silence"/> — is under a window scaled by how good that evidence was, with a
/// minimum (SearchCooling), and ends early once NO PLACE IS WORTH THE WALK
/// (<see cref="SearchedEverything"/>): the value is too spread out, out of reach on foot, inside
/// hiding spots, or gone into the Hub. Every footstep or exhale it hears renews it — except one heard
/// from inside the Hub (C5). There is NO cap as shipped (SearchHardCap 0, 03/10): it searches for as
/// long as evidence of the player keeps coming, and silence is what ends it. With a cap set, entered
/// from Investigating it is the short search of D26 (a fraction of the cap). The Director stretches
/// or shrinks the window (and the cap, when there is one) through its loan on the SO.
///
/// The half-second floor before anything may pull it out, going back to Chasing on sight and checking
/// a hiding spot are all rungs of NemesisDecision's ladder. What is left here is walking to where the
/// value is, walking to the spot when there is one, and saying whether it is still warm.
/// </summary>
public class NemesisSearchingState : BaseState<NemesisStateManager.ENemesisState>
{
    /// <summary>What it is doing about where to go. Runtime only: for F9 and the gizmos.</summary>
    public enum ETarget
    {
        /// <summary>Heading to (or looking around at) a place the possibility map holds value in.
        /// </summary>
        MapPlace,

        /// <summary>There was never anything to go on (no belief, so the map holds nothing): a random
        /// point around where the search started.</summary>
        Scatter,

        /// <summary>Nowhere to go: the map holds value and no place it can walk to is worth the
        /// walk, or it has looked at everything it believed. It stands where it is and looks around.
        /// </summary>
        Standing,
    }

    /// <summary>Why it chose where it is going. Runtime only: for F9.</summary>
    public enum EPickReason
    {
        None,

        /// <summary>On entering the state: the first place its evidence sends it.</summary>
        Entered,

        /// <summary>It reached the last place and finished looking around.</summary>
        Arrived,

        /// <summary>The place it was walking to lost the value it was picked for: seen empty from a
        /// distance, or the value moved.</summary>
        LostItsValue,

        /// <summary>New evidence about the player put the value somewhere else.</summary>
        NewEvidence,

        /// <summary>It finished with, or was taken off, a hiding spot.</summary>
        SpotDone,

        /// <summary>It was standing with nowhere worth going, and asked the map again.</summary>
        AskedAgain,
    }

    private readonly NemesisStateManager nemesisStateManager;

    /// <summary>
    /// The pick: where the possibility map says it is worth going. Owned by this state and
    /// constructed with it, the same arrangement NemesisChasingState has with NemesisPursuit.
    /// </summary>
    private readonly NemesisSearchPicker picker;

    /// <summary>Where it is heading right now. For the HUD and the gizmos: "what is it searching"
    /// has to be answerable from outside or none of the numbers behind it can be tuned.</summary>
    public Vector3 SearchTarget { get; private set; }

    /// <summary>Standing at a place it reached, looking around, before choosing the next one. Only
    /// while it is actually THERE: NemesisLookAround sweeps the gaze while this is true, and it used
    /// to stay true all the way to the next point (plan §18.1).</summary>
    public bool IsPausing => pausedHere && pauseRemaining > 0f && !watching;

    /// <summary>It has the player in plain view and cannot walk to them: it went as near as the
    /// NavMesh lets it and is standing there, looking at them. See <see cref="TickWatch"/>.</summary>
    public bool IsWatchingPlayer => watching;

    /// <summary>The hiding spot this search is walking to or checking, or null. See TickSpotCheck.
    /// </summary>
    public HidingSpot SpotTarget => spotTarget;

    /// <summary>
    /// Standing at a hiding spot's approach point, checking it (plan §3.5). The ladder's
    /// IsCheckingSpot reads this, so the search budget cannot pull the Nemesis away with its hand on
    /// the door. The arrival frame counts: the ladder decides before this state updates, so on that
    /// frame the check has not been stamped yet — the same race NemesisInvestigatingState.IsInspecting
    /// guards against.
    /// </summary>
    public bool IsCheckingSpot =>
        spotTarget != null && (spotCheckRemaining >= 0f || nemesisStateManager.HasArrived);

    /// <summary>The last pick: the places it weighed and the one the roll chose. For the gizmos and
    /// F9, which is how "why did it go there" gets answered.</summary>
    public NemesisSearchPicker Picker => picker;

    /// <summary>What the place it is heading to came from. See <see cref="ETarget"/>.</summary>
    public ETarget Target => target;

    /// <summary>Whether it is heading to (or looking around at) a place chosen off the possibility
    /// map. False on its way to a hiding spot, on the last-resort scatter and while it stands with
    /// nowhere worth going.</summary>
    public bool IsTargetFromMap => target == ETarget.MapPlace && ReferenceEquals(spotTarget, null);

    /// <summary>The middle of the zone it chose. Usually where it is heading; with precise evidence
    /// inside the zone it stands on the evidence point instead (<see cref="StandingPointIn"/>), and
    /// this stays the place the value is measured around.</summary>
    public Vector3 TargetZone => targetZone;

    /// <summary>How much of the value the place held when it was picked.</summary>
    public float TargetShare => targetShare;

    /// <summary>How much of the value the place holds now. Under SearchMapRepickShare of
    /// <see cref="TargetShare"/> it stops walking there and picks again.</summary>
    public float TargetShareNow => IsTargetFromMap ? picker.ShareAt(targetZone) : 0f;

    /// <summary>Why it chose where it is going. For F9.</summary>
    public EPickReason LastPick => lastPick;

    /// <summary>The spot being walked to or checked. Null outside a spot check.</summary>
    private HidingSpot spotTarget;

    /// <summary>Seconds left standing at the spot, or negative while still walking to it.</summary>
    private float spotCheckRemaining = -1f;

    /// <summary>Seconds left of the look-around at the current place. See
    /// SO_NemesisData.SearchPauseTime for why the search stands still at all.</summary>
    private float pauseRemaining;

    /// <summary>It has reached the current place and started (or finished) looking around there.
    /// Reset every time it sets off somewhere.</summary>
    private bool pausedHere;

    private bool watching;

    /// <summary>How far the seen player has to move before the watch walks to a new closest point:
    /// a destination set every frame is a path query every frame.</summary>
    private const float WatchRetargetDistance = 1f;

    private ETarget target = ETarget.Scatter;
    private EPickReason lastPick = EPickReason.None;
    private Vector3 targetZone;
    private float targetShare;

    /// <summary>When it last chose where to go (Time.time). What the two re-picks that are not an
    /// arrival are throttled against: see <see cref="RepickInterval"/>.</summary>
    private float lastPickAt = float.NegativeInfinity;

    /// <summary>When it may next ask whether the place it is walking to kept its value.</summary>
    private float nextValueCheckAt;

    /// <summary>The evidence the map had folded in (NemesisSearchPicker.EvidenceSequence) when it
    /// last judged its destination. See TrackEvidence.</summary>
    private int consumedSequence;

    /// <summary>Where it stood on entering: the centre of the last-resort scatter, so a search with
    /// nothing to go on stays around one place instead of drifting across the level.</summary>
    private Vector3 scatterCentre;

    /// <summary>
    /// The shortest time between two picks that are not an arrival. A pick is a handful of path
    /// queries, the map only changes when it ticks (four times a second as shipped), and the player's
    /// footsteps move the belief ten times a second: without this a search re-picked on every one of
    /// them. Not on the SO because it is not a design value — it is how often the question is worth
    /// asking. What decides WHETHER it re-picks is SearchMapRepickShare, and that one is.
    /// </summary>
    private const float RepickInterval = 0.5f;

    /// <summary>The least it stands and looks when there is nowhere worth going, whatever
    /// SearchPauseTime says: with the pause switched off (0) it would otherwise ask the map again
    /// every frame.</summary>
    private const float MinStandTime = 1f;

    /// <summary>The used hiding spots already rolled this search (Fase 2D): one roll each per search,
    /// however many places the search goes to near them.</summary>
    private readonly System.Collections.Generic.HashSet<HidingSpot> usedSpotsRolled =
        new System.Collections.Generic.HashSet<HidingSpot>();

    /// <summary>
    /// The spots the player has used around where the search is going are candidates (plan §17.6,
    /// D23, Fase 2D): NemesisHidingAwareness rolls them by how used they are and suspects the one that
    /// comes up, and the spot check below walks over and opens it (case 39). Asked every time it
    /// chooses a place, around that place — never across the level (case 40). The radius is what is
    /// left of the old sweep's widest disc (RoomSweepRadius): how far around a search something
    /// counts as inside it.
    ///
    /// Fase 2e replaces this roll with the value the map holds inside each spot × the habit (D38).
    /// </summary>
    private void ConsiderUsedSpots(Vector3 centre)
    {
        NemesisHidingAwareness awareness = nemesisStateManager.HidingAwareness;
        if (awareness == null) return;

        float radius = Data != null ? Data.RoomSweepRadius : 8f;
        awareness.ConsiderUsedSpots(centre, radius, usedSpotsRolled);
    }

    /// <summary>Entered with the agent switched off (the lift ride): the first place is chosen on the
    /// first UpdateState with an agent to give it to.</summary>
    private bool needsFirstPoint;

    // ── Cooling (plan §18.5 B) ───────────────────────────────────────────────

    /// <summary>When the last evidence that renews the search came in (Time.time), and what kind it
    /// was. Tracked apart from the destination's own sequence because it has to keep counting
    /// through a spot check, where the search does not look at evidence at all.</summary>
    private float lastRenewalAt;

    /// <summary>When it last got to (or gave up walking to) the first place its evidence sent it:
    /// the entry itself, or the last frame it was still on its way there. See <see cref="Silence"/>.
    /// </summary>
    private float reachedEvidenceAt;

    /// <summary>Still on its way to the first place its last evidence sent it. See
    /// <see cref="IsHeadingToEvidence"/>.</summary>
    private bool headingToEvidence;

    private bool renewedBySight;
    private bool renewedMuffled;
    private int renewedSequence;

    /// <summary>The last pick found nowhere worth the walk. See <see cref="SearchedEverything"/>.
    /// </summary>
    private bool searchedEverything;

    /// <summary>The belief sequence that verdict was reached with.</summary>
    private int verdictSequence;

    /// <summary>Entered from Investigating: the short search after an empty investigation of the
    /// player's own noise (D26), with a fraction of the cap.</summary>
    private bool escalated;

    /// <summary>
    /// Whether the search should go on — what the ladder's "la búsqueda sigue tibia" reads
    /// (predicate IsSearchWarm). See <see cref="SearchCooling"/> for the rules.
    /// </summary>
    public bool IsWarm
    {
        get
        {
            SO_NemesisData data = Data;

            // No data asset: the old fixed budget, rather than a search that never ends or never starts.
            if (data == null) return nemesisStateManager.TimeInCurrentState < 15f;

            return SearchCooling.IsWarm(nemesisStateManager.TimeInCurrentState, Silence,
                                        data.SearchMinTime, data.SearchQuietWindow, Quality, Cap,
                                        SearchedEverything);
        }
    }

    /// <summary>
    /// Seconds of silence the search has actually had to listen to: since the last evidence that
    /// renews it, or since it got to the first place that evidence sent it, whichever is later — and
    /// none at all while it is still on its way there. For the HUD and the ladder.
    ///
    /// FROM THE ARRIVAL, NOT FROM THE EVIDENCE (playtest 27/09). Counted from the evidence alone, the
    /// walk to it ate the window: a footstep heard fifteen metres away is five or six seconds of walk,
    /// so the search reached the spot with most of its eight seconds gone and gave up after a look or
    /// two — worst exactly over the long distances where the player had got furthest. Nothing about
    /// that walk is silence the Nemesis has listened to where the player was.
    /// </summary>
    public float Silence =>
        Time.time - Mathf.Max(lastRenewalAt, headingToEvidence ? Time.time : reachedEvidenceAt);

    /// <summary>
    /// Whether it is still on its way to the first place its evidence sent it: the pick made on
    /// entering, or on evidence that moved the value. It stops being true when it gets there, when it
    /// sees from a distance that the place is empty (that is the look it owed it), or when there
    /// turns out to be nowhere to go. No silence is counted meanwhile. For the HUD.
    /// </summary>
    public bool IsHeadingToEvidence => headingToEvidence;

    /// <summary>The silence it tolerates right now: the window (as lent by the Director) times the
    /// quality of the last evidence.</summary>
    public float QuietWindow => Data != null ? Data.SearchQuietWindow * Quality : 0f;

    /// <summary>The most it will search, in seconds in the state: the cap (as lent by the Director),
    /// shortened for the escalated search of D26. 0 or less: no cap — it lasts while evidence comes.
    /// </summary>
    public float Cap
    {
        get
        {
            SO_NemesisData data = Data;
            if (data == null) return 15f;
            return data.SearchHardCap * (escalated ? data.SearchEscalatedCapScale : 1f);
        }
    }

    /// <summary>The short search that follows an empty investigation of the player's noise (D26).
    /// </summary>
    public bool IsEscalated => escalated;

    /// <summary>
    /// "I have looked everywhere here" (plan §3.4): the last time it chose where to go, no place was
    /// worth the walk — the value is spread too thin, out of reach on foot, inside hiding spots, or
    /// gone into the Hub (SearchCooling.NothingWorthTheWalk) — or it has looked at everything it
    /// believed and the map is empty. It used to be "the disc is fully swept at its widest".
    ///
    /// THE VERDICT IS ABOUT THE MAP AS THAT PICK FOUND IT, and new evidence redraws the map. The
    /// ladder reads this BEFORE the state updates, so on the frame a footstep comes in the stale
    /// verdict would still be standing: the search went cold, the ladder dropped it, and the noise
    /// was investigated from scratch. So evidence newer than the verdict voids it until the next pick
    /// reaches its own — unless that evidence came from inside the Hub (C5): all it adds is that the
    /// player went in there, which is the verdict.
    /// </summary>
    public bool SearchedEverything
    {
        get
        {
            if (!searchedEverything) return false;

            // Looking straight at the player is not having looked everywhere (plan §18.5 A): the
            // places around them are worth nothing to the pick because it cannot WALK to them, and
            // that verdict used to cool the search and send it back to patrol with them in view.
            if (nemesisStateManager.HasVisualTarget) return false;

            NemesisBelief belief = nemesisStateManager.Belief;
            if (belief == null || !belief.HasBelief || belief.Sequence == verdictSequence) return true;

            return NemesisSafeZones.Contains(belief.Position);
        }
    }

    private float Quality
    {
        get
        {
            SO_NemesisData data = Data;
            if (data == null) return 1f;
            return SearchCooling.Quality(renewedBySight, renewedMuffled, data.SearchQualitySight,
                                         data.SearchQualityMuffled);
        }
    }

    public NemesisSearchingState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
        picker = new NemesisSearchPicker(stateManager);
    }

    private SO_NemesisData Data => nemesisStateManager.NemesisData;

    /// <summary>"Same floor", as the possibility map measures it: one band for the map that puts the
    /// value and the search that walks to it.</summary>
    private const float FloorBand = NemesisPossibilityMap.FloorBand;

    /// <summary>How much of the value a place had when it was picked it has to keep for the walk to
    /// it to go on (SO_NemesisData.SearchMapRepickShare).</summary>
    private float KeepShare => Data != null ? Data.SearchMapRepickShare : 0.35f;

    /// <summary>
    /// ENTERING FROM A CHASE, IT SETS OFF AT ONCE.
    ///
    /// The disc search stood on the spot where it lost the player for SearchPauseTime before its
    /// first pick: "the search starts where it lost them" — heading straight off to a sweep point
    /// read as never having cared where the player went. With a disc that stop was the only way to
    /// tie the search to the lost spot. It was also 1.2 seconds handed over at every corner, with
    /// the player five metres further on by the time it moved ("en las esquinas me pierde muy
    /// fácil").
    ///
    /// The map makes the stop unnecessary, not just removable. The sighting seeded it with a point and
    /// the heading the player was seen on; it has been spreading at the player's speed ever since; and
    /// everything the Nemesis looked at on the way here, the lost spot included, is already empty. So
    /// the first pick IS "where could they have gone from here", and going there is caring where the
    /// player went. The look-around belongs to the places it reaches afterwards.
    ///
    /// WHAT THE MAP TOOK OVER FROM THE CODE THAT WAS HERE:
    ///   - the stop at the lost spot (IsStandingWhereLost): see above;
    ///   - "it heard them further on since, so skip the stop" (HasNewerEvidenceElsewhere, playtest
    ///     27/09): that noise re-seeded the map, so the first pick already goes there — to the area of
    ///     it, or to the point itself if the noise was precise;
    ///   - the visit owed to the evidence point before anything else (27/09: "it stopped metres short
    ///     of where it lost me", because the disc's roll favoured its near edge): the value sits ON
    ///     the evidence until the Nemesis has looked at it, so the roll goes there by itself, and
    ///     StandingPointIn keeps "the point itself" for evidence precise enough to mean it;
    ///   - the room the player was seen going into (RoomFor, and the floor-collider names it read):
    ///     the heading is in how the map spreads, and a doorway the player went through is where the
    ///     value went.
    /// WHAT STAYS, because it is not about where to look:
    ///   - a hiding spot to check takes the destination before any pick;
    ///   - entered with the agent switched off (the lift ride), the first pick waits for an agent.
    /// </summary>
    public override void EnterState()
    {
        NextState = StateKey;
        pauseRemaining = 0f;
        pausedHere = false;
        watching = false;
        spotTarget = null;
        spotCheckRemaining = -1f;

        picker.Clear();
        target = ETarget.Scatter;
        lastPick = EPickReason.None;
        targetShare = 0f;
        lastPickAt = float.NegativeInfinity;
        searchedEverything = false;
        headingToEvidence = false;

        scatterCentre = nemesisStateManager.transform.position;
        targetZone = scatterCentre;
        SearchTarget = scatterCentre;

        escalated = nemesisStateManager.HasPreviousState &&
                    nemesisStateManager.PreviousStateKey == NemesisStateManager.ENemesisState.Investigating;

        nemesisStateManager.SetGait(NemesisStateManager.EGait.Running,
                                    nemesisStateManager.NemesisMovement.SearchSpeed);

        StartCooling();
        reachedEvidenceAt = Time.time;

        NemesisBelief belief = nemesisStateManager.Belief;
        consumedSequence = picker.EvidenceSequence;
        verdictSequence = belief != null ? belief.Sequence : 0;

        usedSpotsRolled.Clear();

        if (!nemesisStateManager.IsAgentReady)
        {
            needsFirstPoint = true;
            return;
        }

        needsFirstPoint = false;

        // A hiding spot to check takes the destination anyway (TickSpotCheck below): picking a place
        // first would only pay for path queries to throw the answer away. The walk its evidence owes
        // it comes after the spot, so no silence is counted meanwhile — when there is evidence: with
        // an empty map (a known spot and no belief) there is no such walk to wait for.
        NemesisHidingAwareness awareness = nemesisStateManager.HidingAwareness;
        if (awareness != null && awareness.SpotToCheck != null)
        {
            headingToEvidence = picker.HasValue;
            TickSpotCheck();
            return;
        }

        PickNext(EPickReason.Entered, fromEvidence: true);
    }

    public override void ExitState()
    {
        EndSpotCheck();
        picker.Clear();
        pausedHere = false;
        watching = false;
        pauseRemaining = 0f;
        headingToEvidence = false;

        // Whatever happens next, the patrol that follows should prowl this area rather than
        // relocate across the level. Set on EVERY exit, the transition to Chasing included: it is
        // consumed by the next BeginPatrolCycle, so a search that succeeded and turned into a
        // chase simply spends it later, after that chase ends — which is still the right zone.
        nemesisStateManager.NemesisController?.RequestNearbyPatrol();
    }

    public override void UpdateState()
    {
        // Agent switched off (freight elevator ride): nothing to ask of it this frame. See
        // NemesisStateManager.IsAgentReady.
        if (!nemesisStateManager.IsAgentReady) return;

        // Before anything that can return early: the cooling clock has to keep counting through a
        // spot check too. While it is still on its way to the first place its evidence sent it there
        // is no silence to count yet (see Silence); the frame that walk ends is the last one stamped
        // here.
        TrackRenewal();
        if (headingToEvidence) reachedEvidenceAt = Time.time;

        if (needsFirstPoint)
        {
            needsFirstPoint = false;
            PickNext(EPickReason.Entered, fromEvidence: true);
        }

        // A hiding spot to check comes before everything else this state does — fresh evidence
        // included, which with the player in a locker is most likely their own breathing.
        if (TickSpotCheck()) return;

        if (TickWatch()) return;

        TrackEvidence();

        if (!nemesisStateManager.HasArrived)
        {
            TickValueLost();
            return;
        }

        // ARRIVED: STOP AND LOOK BEFORE MOVING ON.
        //
        // Chaining straight to the next destination is what made the search unreadable from the
        // outside: from inside a locker it just looks like an odd patrol. Standing still for a
        // moment at each place, sweeping its gaze, turns the search into something the player can
        // read and gamble against. And since the possibility map, the look is also the work: what
        // that gaze passes over is what the map empties, and the next pick is made off what is left.
        //
        // Armed HERE, on arrival, and not when setting off (plan §18.1): that left the first point
        // without a pause and had IsPausing true all the way to the next one.
        if (!pausedHere)
        {
            pausedHere = true;
            pauseRemaining = Data != null ? Data.SearchPauseTime : 0f;

            // It got to the first place its evidence sent it: from here on, silence counts.
            headingToEvidence = false;
        }

        if (pauseRemaining > 0f || Time.time - lastPickAt < RepickInterval)
        {
            pauseRemaining -= Time.deltaTime;

            nemesisStateManager.NavAgent.velocity = Vector3.zero;
            nemesisStateManager.SetGait(NemesisStateManager.EGait.Idle, 0f);
            return;
        }

        PickNext(target == ETarget.Standing ? EPickReason.AskedAgain : EPickReason.Arrived,
                 fromEvidence: false);
    }

    // ── Where it goes ────────────────────────────────────────────────────────

    /// <summary>
    /// Chooses where to go next and sets off — or stays put, when there is nowhere worth going.
    ///
    /// THE MAP DECIDES WHENEVER THERE IS A BELIEF FOR IT TO HOLD:
    ///   - a place worth the walk: it goes there (the roll is NemesisSearchPicker's);
    ///   - value, and no place worth the walk: it stands where it is and looks around, and the search
    ///     reads as "revisó todo". It does NOT fall back to the scatter: wandering to random points
    ///     while the map says where the player can be is the two mechanisms deciding at once;
    ///   - no value left because it has looked at everything it believed: the same, and "revisó
    ///     todo" in the plainest sense there is.
    /// THE SCATTER ONLY WHEN THERE WAS NEVER ANYTHING TO GO ON: no belief to seed the map (entered
    /// from a capture, or on a known hiding spot alone), or no map at all.
    /// </summary>
    /// <param name="fromEvidence">The pick answers evidence — the entry, or evidence that moved the
    /// value — so the walk it starts is the one <see cref="Silence"/> waits for.</param>
    private void PickNext(EPickReason why, bool fromEvidence)
    {
        lastPick = why;
        lastPickAt = Time.time;
        nextValueCheckAt = Time.time + RepickInterval;
        pausedHere = false;
        pauseRemaining = 0f;

        bool picked = picker.TryPick(out NemesisSearchPicker.Candidate chosen, ChaseHeading(why), IsOwedVisit(why));

        // Whatever it chose, it chose off the map as the newest evidence left it: the pick has the
        // map catch up with the belief before it reads it.
        NemesisBelief belief = nemesisStateManager.Belief;
        bool hasBelief = belief != null && belief.HasBelief;
        consumedSequence = picker.EvidenceSequence;
        verdictSequence = belief != null ? belief.Sequence : 0;

        if (picked)
        {
            target = ETarget.MapPlace;
            targetZone = chosen.Position;

            // Measured the way it will be measured again on the walk (everything searchable around
            // the middle of the zone), not the zone's own share: zones never share a node, so a zone
            // cut after its neighbour holds less than the disc around its middle does.
            targetShare = picker.ShareAt(targetZone);
            searchedEverything = false;
            if (fromEvidence) headingToEvidence = true;

            SetOff(StandingPointIn(targetZone, belief));
            ConsiderUsedSpots(SearchTarget);
            return;
        }

        targetShare = 0f;
        headingToEvidence = false;

        // A belief and an empty map: every place it believed in, it has looked at. That is not the
        // search with nothing to go on — it had something, followed it and finished — so it must not
        // fall into the scatter either: the scatter is around where the search STARTED, and walking
        // back there is the search going backwards (WIR-062) by another road.
        bool lookedAtItAll = !picker.HasValue && hasBelief && picker.HasMap;

        if (picker.HasValue || lookedAtItAll)
        {
            target = ETarget.Standing;
            targetZone = nemesisStateManager.transform.position;
            searchedEverything = lookedAtItAll || picker.NothingWorthTheWalk;
            StandAndLook();

            // Out of places is exactly when the spots around it matter: what is left of the value is
            // most likely inside them.
            ConsiderUsedSpots(targetZone);
            return;
        }

        // Nothing to reason from at all: no belief (or no map to put one on). The search lasts what
        // its silence lasts, around where it started.
        target = ETarget.Scatter;
        searchedEverything = false;

        SetOff(ScatterPoint());
        targetZone = SearchTarget;
        ConsiderUsedSpots(scatterCentre);
    }

    /// <summary>
    /// Whether this pick answers evidence that has just come in — so the walk it starts is a visit
    /// the search owes it, and a long walk weighs in the roll but cannot cancel it
    /// (SearchPickRules.TakesPart, 07/10, WIR-058): evidence that moved the value, and the first pick
    /// out of a chase or a lift ride, which carry the sighting or the noise that set them off.
    ///
    /// Not every other pick: once it has got to a place and looked, "revisó todo" is a verdict about
    /// the map, and a place far away is rightly not worth the walk. Nor the first pick out of an
    /// empty investigation (D26): the noise was the point it just stood on.
    /// </summary>
    private bool IsOwedVisit(EPickReason why)
    {
        if (why == EPickReason.NewEvidence) return true;
        if (why != EPickReason.Entered || !nemesisStateManager.HasPreviousState) return false;

        NemesisStateManager.ENemesisState from = nemesisStateManager.PreviousStateKey;
        return from == NemesisStateManager.ENemesisState.Chasing ||
               from == NemesisStateManager.ENemesisState.Traversing;
    }

    /// <summary>The slowest the player may have been seen going for it to count as a heading: under
    /// this they were standing, and there is no "ahead" to lean towards.</summary>
    private const float MinHeadingSpeed = 0.5f;

    /// <summary>
    /// The way the player was last seen going, for the pick a chase hands over with — and only that
    /// one (playtest 05/10: turning a corner, the search set off sideways as often as after them).
    /// The map already spreads faster along the heading; this leans the first roll the same way
    /// (SearchPickRules.HeadingWeight). Every later pick is the plain roll: by then the Nemesis has
    /// looked down that way, and what the map holds is what is left to look at.
    ///
    /// The EYES' last sighting, like the pursuit that got it here (NemesisPursuit, "the SEEN spot"):
    /// a heading is something it saw, and a sighting old enough not to be this chase is not one.
    /// </summary>
    private NemesisSearchPicker.HeadingHint ChaseHeading(EPickReason why)
    {
        if (why != EPickReason.Entered) return default;

        if (!nemesisStateManager.HasPreviousState ||
            nemesisStateManager.PreviousStateKey != NemesisStateManager.ENemesisState.Chasing) return default;

        FieldOfView eyes = nemesisStateManager.FieldOfView;
        if (eyes == null || !eyes.HasLastKnownPosition) return default;
        if (eyes.TimeSinceLastSighting >= NemesisPursuit.RecentSightingSeconds) return default;

        Vector3 heading = eyes.LastKnownVelocity;
        heading.y = 0f;
        if (heading.sqrMagnitude < MinHeadingSpeed * MinHeadingSpeed) return default;

        return new NemesisSearchPicker.HeadingHint(eyes.LastKnownPosition, heading);
    }

    /// <summary>
    /// Where exactly to stand in the zone the roll chose: its middle, or — when the evidence is
    /// precise and lies inside it — the evidence point ITSELF (Plan-Busqueda-Nemesis Fase 1).
    ///
    /// The roll decides WHICH place; this only decides where in it. So "it goes to where it last saw
    /// you" still holds to the metre while that is where the value is — a fresh sighting puts all of
    /// it there, and the zone around it wins the roll outright — and stops holding by itself once the
    /// map has moved on. For a vague noise the answer is always the middle of the zone: the point of
    /// a noise heard from across a room is the last footstep, and the last footstep of a run that
    /// ends in a locker is the locker door (WIR-057).
    /// </summary>
    private Vector3 StandingPointIn(Vector3 zone, NemesisBelief belief)
    {
        if (!MayVisit(belief)) return zone;

        Vector3 evidence = OnNavMesh(belief.Position);

        Vector3 fromZone = evidence - zone;
        if (Mathf.Abs(fromZone.y) > FloorBand) return zone;

        fromZone.y = 0f;
        float radius = picker.ZoneRadius;
        if (fromZone.sqrMagnitude > radius * radius) return zone;

        // Already standing on it: the middle of the zone is the step that makes it turn and look at
        // the rest. Sent to its own feet it would "arrive" facing the way it already faced.
        Vector3 fromSelf = evidence - nemesisStateManager.transform.position;
        fromSelf.y = 0f;
        float minTravel = picker.MinTravel;
        if (fromSelf.sqrMagnitude < minTravel * minTravel) return zone;

        // The zone was path-tested to its middle; the point is somewhere else in it, and a wall can
        // run between the two.
        return picker.IsReachableOnFoot(evidence) ? evidence : zone;
    }

    /// <summary>
    /// Whether the search may stand on the evidence point itself (<see cref="StandingPointIn"/>): for
    /// a sighting, and for a noise pinned down beside the Nemesis (an evidence radius up to
    /// SearchPreciseNoiseRadius). Never for the player's noise from inside a hiding spot (D22).
    ///
    /// NOT FOR A VAGUE NOISE (Plan-Busqueda-Nemesis Fase 1, WIR-057). It used to be every noise, and
    /// the last footstep of a run that ends in a locker is the locker door: "it heard you go past"
    /// became "it walked straight to your hiding spot", with the proximity rule waiting at the end.
    /// A noise from across a room says "over there", and the places the map spreads it over are what
    /// cover "over there" — the point itself is no more likely than the rest of the area.
    /// </summary>
    private bool MayVisit(NemesisBelief belief) => MayVisitEvidence(belief, Data);

    /// <summary>The rule behind <see cref="MayVisit"/>, static so F9's "ancla" says exactly what the
    /// search does with the same belief.</summary>
    public static bool MayVisitEvidence(NemesisBelief belief, SO_NemesisData data)
    {
        if (belief == null || !belief.HasBelief) return false;
        if (belief.IsAnchoredBySight) return true;
        if (belief.LastEvidenceFromHidingSpot) return false;

        float precise = data != null ? data.SearchPreciseNoiseRadius : 1.5f;
        return belief.EvidenceRadius <= precise;
    }

    /// <summary>
    /// New evidence about the player has redrawn the map (the facade folds it in on its own tick,
    /// before the states run: this watches the MAP's evidence sequence, not the belief's, so it never
    /// judges against a map that has not heard the footstep yet).
    ///
    /// The state does not judge the evidence. It asks what it asks while walking: did the place I am
    /// heading to keep its value? A noise seeds an area and wipes everything outside it, a sighting
    /// puts all of it on one spot — so "the evidence is somewhere else" and "my place lost its value"
    /// are the same fact, and "they were heard again around where I am going" leaves the value there.
    ///   - Kept: carry on. Standing at a place, finish looking first; the next pick reads the new map.
    ///   - Lost: go where the value now is, at once, pause or no pause. Standing around finishing a
    ///     look-around after hearing the player across the room is the opposite of searching.
    ///
    /// This is what SearchSweepRules.Judge (keep / follow / re-centre) used to answer with a disc.
    /// While the player is heard the map is re-seeded on every one of its ticks; the judgement costs
    /// one sum over a few nodes, and the re-pick is throttled (<see cref="RepickInterval"/>) — held
    /// back, not dropped: the sequence is only consumed once it has been judged.
    ///
    /// Leads (decoys, Director pulses) never get here: they do not move the belief's sequence, and
    /// they never touch the map (D18).
    /// </summary>
    private void TrackEvidence()
    {
        int folded = picker.EvidenceSequence;
        if (folded == consumedSequence) return;
        if (Time.time - lastPickAt < RepickInterval) return;

        consumedSequence = folded;

        // Evidence the map could not place (nowhere near its nodes), or a belief that is gone: there
        // is nothing new to go to. A place that lost its value with it is dropped on the walk.
        if (!picker.HasValue) return;

        bool keptItsValue = target == ETarget.MapPlace &&
                            !SearchPickRules.LostItsValue(picker.ShareAt(targetZone), targetShare, KeepShare);
        if (keptItsValue) return;

        PickNext(EPickReason.NewEvidence, fromEvidence: true);
    }

    /// <summary>
    /// On the walk: whether the place it is heading to is still worth getting to. The map empties
    /// what the Nemesis looks at, and it looks ahead as it walks — so by the time a place seven
    /// metres down a corridor comes into view, the map already knows nobody is there. Walking the rest
    /// of the way to stand on it and look around is the search doing something it knows is pointless,
    /// which reads as a patrol and not as a hunt. It picks again instead, off what is left.
    ///
    /// Throttled (<see cref="RepickInterval"/>) and measured against what the place held when it was
    /// picked (SearchPickRules.LostItsValue): the value flows on every tick, and re-picking whenever
    /// somewhere else looks better is turning round in the corridor because a number moved.
    /// </summary>
    private void TickValueLost()
    {
        if (target != ETarget.MapPlace || Time.time < nextValueCheckAt) return;
        nextValueCheckAt = Time.time + RepickInterval;

        if (!SearchPickRules.LostItsValue(picker.ShareAt(targetZone), targetShare, KeepShare)) return;

        // Seeing it empty from here is the look it owed that place: silence counts from now.
        headingToEvidence = false;
        PickNext(EPickReason.LostItsValue, fromEvidence: false);
    }

    /// <summary>
    /// The player is in plain view and it cannot walk to them: go as near as the NavMesh lets it and
    /// stand there looking at them (playtest 04/10, "la búsqueda quieta").
    ///
    /// HOW IT GETS HERE. "lo está viendo" only takes a player it can REACH (WIR-018), so one on a
    /// catwalk, in the Hub or across a gap leaves it in this state with them in sight. The search
    /// then asked the map where to go, every place around the player was worth nothing because none
    /// can be walked to, and it stood wherever it happened to be, sweeping its gaze from side to
    /// side past the person it could see. From the player's side that is the monster ignoring them.
    ///
    /// The destination is the spot the eyes have them at, and the agent does the rest: with no
    /// complete path it walks the partial one and stops at its end, which is the closest it can
    /// get. NemesisLookAround keeps the gaze on them while this is true, and SearchedEverything is
    /// false for as long as it sees them, so the search does not go cold under their nose. The
    /// moment they are out of sight it picks off the map again, which those sightings have just
    /// re-seeded around where they were.
    /// </summary>
    /// <returns>true while it is watching, and the rest of the update should not run.</returns>
    private bool TickWatch()
    {
        FieldOfView eyes = nemesisStateManager.FieldOfView;

        if (eyes == null || !nemesisStateManager.HasVisualTarget || !eyes.HasLastKnownPosition)
        {
            if (!watching) return false;

            watching = false;
            PickNext(EPickReason.NewEvidence, fromEvidence: true);
            return true;
        }

        Vector3 seenAt = eyes.LastKnownPosition;

        if (!watching || (seenAt - SearchTarget).sqrMagnitude > WatchRetargetDistance * WatchRetargetDistance)
        {
            watching = true;
            target = ETarget.Standing;
            targetZone = seenAt;
            targetShare = 0f;
            headingToEvidence = false;
            SetOff(seenAt);
        }

        // Standing: at the end of the partial path, or with no path at all (the spot is too far from
        // anything walkable to snap to). Without the second half it would keep the running gait on
        // an agent that is going nowhere, which is the "corre en el lugar" of WIR-024.
        UnityEngine.AI.NavMeshAgent agent = nemesisStateManager.NavAgent;
        bool nowhereToGo = !agent.pathPending &&
                           agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid;

        if (nemesisStateManager.HasArrived || nowhereToGo)
        {
            agent.velocity = Vector3.zero;
            nemesisStateManager.SetGait(NemesisStateManager.EGait.Idle, 0f);
        }

        return true;
    }

    private void SetOff(Vector3 point)
    {
        nemesisStateManager.SetGait(NemesisStateManager.EGait.Running,
                                    nemesisStateManager.NemesisMovement.SearchSpeed);
        SetDestination(point);
    }

    /// <summary>
    /// Nowhere worth going: stays where it is and looks around, and asks the map again when the look
    /// is over. The value may come back within reach — it leaks out of a hiding spot it could not
    /// see into, the lift gets power, a noise puts it somewhere new — and until then standing and
    /// looking is the honest thing to show: it has run out of places, and the ladder lets go of the
    /// search as soon as its minimum has passed (<see cref="SearchedEverything"/>).
    /// </summary>
    private void StandAndLook()
    {
        SetDestination(nemesisStateManager.transform.position);

        pausedHere = true;
        pauseRemaining = Mathf.Max(MinStandTime, Data != null ? Data.SearchPauseTime : 0f);

        nemesisStateManager.NavAgent.velocity = Vector3.zero;
        nemesisStateManager.SetGait(NemesisStateManager.EGait.Idle, 0f);
    }

    // ── Cooling ──────────────────────────────────────────────────────────────

    /// <summary>Starts the cooling clock on entry: from the belief's last evidence if there is one
    /// (the chase's last sighting, the footstep that brought it here), from now if there is not.
    /// </summary>
    private void StartCooling()
    {
        NemesisBelief belief = nemesisStateManager.Belief;

        if (belief != null && belief.HasBelief)
        {
            Renew(belief);
            return;
        }

        lastRenewalAt = Time.time;
        renewedBySight = false;
        renewedMuffled = false;
        renewedSequence = belief != null ? belief.Sequence : 0;
    }

    /// <summary>
    /// Renews the search with every new piece of evidence about the player — except evidence from
    /// inside the Hub (C5): the player's footsteps heard through the Hub's door would otherwise keep
    /// the Nemesis searching outside it until the cap, camping the one place the game promises is
    /// safe. Leads never get here (they do not move the belief's sequence).
    /// </summary>
    private void TrackRenewal()
    {
        NemesisBelief belief = nemesisStateManager.Belief;
        if (belief == null || !belief.HasBelief || belief.Sequence == renewedSequence) return;

        if (NemesisSafeZones.Contains(belief.Position))
        {
            renewedSequence = belief.Sequence;
            return;
        }

        Renew(belief);
    }

    private void Renew(NemesisBelief belief)
    {
        lastRenewalAt = Time.time - Mathf.Max(0f, belief.Age);
        renewedBySight = belief.IsAnchoredBySight;
        renewedMuffled = belief.LastEvidenceMuffled;
        renewedSequence = belief.Sequence;
    }

    /// <summary>The point on the NavMesh under a belief. The fusion averages positions, and the
    /// average of two points on either side of a table is inside the table.</summary>
    private static Vector3 OnNavMesh(Vector3 point)
    {
        return NemesisNav.TrySnapToNavMesh(point, out Vector3 snapped) ? snapped : point;
    }

    // ── Hiding spots ────────────────────────────────────────────────────────

    /// <summary>
    /// Walks to the hiding spot the Nemesis knows — or, failing that, suspects — the player is in,
    /// stands at its approach point for SearchPauseTime, and if nothing came of it marks it checked
    /// and goes back to searching (plan §3.5: the known spot first, then the area).
    /// Returns true while a spot has this state's attention.
    ///
    /// ARRIVING OPENS IT (plan §17.6). It used to be the proximity rule that found a player inside
    /// the moment the Nemesis stood at the door — but holding your breath now takes a player out of
    /// that rule (D21), and a check that relied on it would come back empty with them in there. So
    /// on arrival the spot is opened (<see cref="NemesisHidingAwareness.Open"/>): a player inside
    /// becomes known, "lo tiene al alcance de la mano" is an interrupt, and Catch pulls them out.
    /// That is not the monster knowing what it has not sensed: it looks inside the one spot it is
    /// standing at, and only one it already suspected or knew. Standing there for the whole pause
    /// and still being in this state IS the check coming back empty.
    /// </summary>
    private bool TickSpotCheck()
    {
        NemesisHidingAwareness awareness = nemesisStateManager.HidingAwareness;
        HidingSpot wanted = awareness != null ? awareness.SpotToCheck : null;

        if (wanted == null)
        {
            // Forgotten from outside mid-check — seen out in the open, burned, expired. Let go and
            // pick the search up from wherever it is standing. ReferenceEquals and not ==: a spot
            // destroyed under it (a scene unloading) compares equal to null through Unity's
            // operator, and skipping EndSpotCheck then would leak the 0.25 m stopping distance
            // into every state after this one.
            if (ReferenceEquals(spotTarget, null)) return false;

            EndSpotCheck();
            ResumeSearch();
            return true;
        }

        if (!ReferenceEquals(wanted, spotTarget))
        {
            BeginSpotCheck(wanted);
            return true;
        }

        if (!nemesisStateManager.HasArrived) return true;

        if (spotCheckRemaining < 0f)
        {
            SO_NemesisData data = Data;
            spotCheckRemaining = data != null ? data.SearchPauseTime : 1f;

            // Arrived: open it. A player inside becomes known here, and "lo tiene al alcance de la
            // mano" takes it from there — holding their breath or not (plan §17.6).
            awareness.Open(spotTarget);
        }

        spotCheckRemaining -= Time.deltaTime;
        nemesisStateManager.NavAgent.velocity = Vector3.zero;
        nemesisStateManager.SetGait(NemesisStateManager.EGait.Idle, 0f);
        if (spotCheckRemaining > 0f) return true;

        awareness.MarkChecked(spotTarget);
        EndSpotCheck();
        ResumeSearch();
        return true;
    }

    private void BeginSpotCheck(HidingSpot spot)
    {
        spotTarget = spot;
        spotCheckRemaining = -1f;
        pausedHere = false;
        pauseRemaining = 0f;

        nemesisStateManager.SetStoppingDistance(NemesisStateManager.SpotCheckStoppingDistance);
        nemesisStateManager.SetGait(NemesisStateManager.EGait.Running,
                                    nemesisStateManager.NemesisMovement.SearchSpeed);
        SetDestination(spot.ApproachPoint.position);
    }

    /// <summary>Drops the spot and hands the agent its normal stopping distance back — everything
    /// else in the search measures arrival against that one.</summary>
    private void EndSpotCheck()
    {
        if (ReferenceEquals(spotTarget, null)) return;   // See TickSpotCheck for why not ==.

        spotTarget = null;
        spotCheckRemaining = -1f;
        nemesisStateManager.SetStoppingDistance(nemesisStateManager.DefaultStoppingDistance);
    }

    /// <summary>Back to the map after a hiding spot: opening it cleared its node, and whatever came
    /// in while it had its hand on the door is already seeded, so the pick reads both.</summary>
    private void ResumeSearch() => PickNext(EPickReason.SpotDone, fromEvidence: false);

    /// <summary>Points the agent somewhere and records it, so the HUD and the gizmos can say what
    /// the search is currently looking at.</summary>
    private void SetDestination(Vector3 point)
    {
        SearchTarget = point;
        pausedHere = false;
        nemesisStateManager.NavAgent.destination = point;
    }

    /// <summary>
    /// A random point on the NavMesh around where the search started: the last resort, for a search
    /// with no value on the map to go by (see <see cref="PickNext"/>). Around the ENTRY point and not
    /// around wherever it has got to: a search with nothing to go on should stay about one place, not
    /// random-walk across the level.
    ///
    /// Returns the position snapped by SamplePosition and not the raw random point: the raw one
    /// usually falls off the mesh, and setting it as a destination made the agent walk to the nearest
    /// edge instead. Horizontal only — a random direction that also varies Y throws points above and
    /// below the floor. The attempts are capped because the original do/while had no way out: with
    /// the agent outside the NavMesh it span forever and hung Unity.
    ///
    /// Sampled on the Nemesis's own area mask and path-tested, on foot like every place the search
    /// goes to (NemesisSearchPicker.IsReachableOnFoot): a point on another island is the wall-hugging
    /// failure NemesisPursuit describes, and one across the lift is a trip nobody decided to take.
    /// </summary>
    private Vector3 ScatterPoint()
    {
        const int maxAttempts = 30;
        const float sampleRadius = 1f;
        const int maxPathTests = 4;

        SO_NemesisData data = Data;
        float range = data != null ? data.SearchSweepRadius : 5f;

        Vector3 self = nemesisStateManager.transform.position;
        float minTravel = picker.MinTravel;
        int pathTests = 0;

        for (int i = 0; i < maxAttempts && pathTests < maxPathTests; i++)
        {
            Vector2 circle = Random.insideUnitCircle * range;
            Vector3 randomPoint = scatterCentre + new Vector3(circle.x, 0f, circle.y);

            if (!NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, sampleRadius, NemesisNav.AreaMask))
                continue;

            // Somewhere it is already standing is not somewhere to go: the agent would report having
            // arrived without taking a step.
            Vector3 fromSelf = hit.position - self;
            fromSelf.y = 0f;
            if (fromSelf.sqrMagnitude < minTravel * minTravel) continue;

            pathTests++;
            if (picker.IsReachableOnFoot(hit.position)) return hit.position;
        }

        // Nothing valid nearby: stay put rather than heading for an unreachable point.
        return self;
    }
}
