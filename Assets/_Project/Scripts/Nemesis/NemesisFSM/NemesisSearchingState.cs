using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Looking for the player where it believes they are.
///
/// WHAT CHANGED ON 27/09 (plan §18, Fase 2B part 2). This state used to pick its destinations among
/// the patrol waypoints: a cut-off at "the waypoint ahead of you it can reach first", a weighted roll
/// over waypoints near the belief, and a room sweep that offered the waypoints inside it before any
/// NavMesh point. On top of that, any noise at all — a decoy, a Director pulse, the player's
/// footsteps — re-aimed it every frame, wiping what it had swept and cancelling its look-around. In
/// play it read as "it went to some node nearby instead of where it lost me, sometimes".
///
/// Now there is one way to search: a sweep of NavMesh points around the BELIEF (NemesisFreeRoam).
///   - The disc is centred on the belief and sized off the precision of the last evidence: tight
///     around a sighting, wider around a footstep through a wall, wider still around a breath from
///     inside a hiding spot (D22).
///   - It moves only with new evidence ABOUT THE PLAYER, and only as much as it has to: evidence
///     inside the disc slides the centre and changes nothing else; evidence outside it re-centres
///     the disc and keeps what was swept (SearchSweepRules: plan §17.4, questions 1 and 4). A decoy
///     or a Director pulse never moves it — those are leads, and competing for attention is the
///     ladder's business (and the plan's Fase 2B part 4).
///   - It walks to the evidence point ITSELF first — where it last saw or heard the player — and
///     sweeps the disc around it after (playtest 27/09: rolling over the disc from the start left it
///     at the near edge, metres short of the point). Not for a noise from inside a hiding spot (D22).
///   - Once the disc is covered it opens a step wider, up to RoomSweepRadius.
///   - It stops and looks around at every point it reaches, the first one included.
///
/// HOW LONG IT LASTS (plan §18.5 B, Fase 2B part 3): it cools down instead of expiring. The ladder's
/// "le queda presupuesto de búsqueda" reads <see cref="IsWarm"/>: the search goes on while the
/// silence since the last evidence about the player — counted from when it got to that evidence, see
/// <see cref="Silence"/> — is under a window scaled by how good that
/// evidence was, with a minimum and a cap (SearchCooling), and ends early once it has searched
/// everything it can reach at its widest. Every footstep or exhale it hears renews it — except one
/// heard from inside the Hub (C5). Entered from Investigating it is the short search of D26 (half the
/// cap). The Director stretches or shrinks the window and the cap through its loan on the SO.
///
/// The half-second floor before anything may pull it out, going back to Chasing on sight and
/// checking a hiding spot are all rungs of NemesisDecision's ladder. What is left here is sweeping,
/// walking to the spot when there is one, and saying whether it is still warm.
/// </summary>
public class NemesisSearchingState : BaseState<NemesisStateManager.ENemesisState>
{
    private readonly NemesisStateManager nemesisStateManager;

    /// <summary>
    /// The sweep. Owned by this state and constructed with it, the same arrangement
    /// NemesisChasingState has with NemesisPursuit.
    /// </summary>
    private readonly NemesisFreeRoam freeRoam;

    /// <summary>Where it is heading right now. For the HUD and the gizmos: "what is it searching"
    /// has to be answerable from outside or none of the numbers behind it can be tuned.</summary>
    public Vector3 SearchTarget { get; private set; }

    /// <summary>Standing at a point it reached, looking around, before choosing the next one. Only
    /// while it is actually THERE: NemesisLookAround sweeps the gaze while this is true, and it used
    /// to stay true all the way to the next point (plan §18.1).</summary>
    public bool IsPausing => pausedHere && pauseRemaining > 0f;

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

    /// <summary>Whether a sweep area is set. For the debug HUD and the gizmos.</summary>
    public bool IsSweeping => freeRoam.IsCommitted;

    /// <summary>The sweep, so the gizmos can draw the area and what has already been swept.</summary>
    public NemesisFreeRoam FreeRoam => freeRoam;

    /// <summary>The spot being walked to or checked. Null outside a spot check.</summary>
    private HidingSpot spotTarget;

    /// <summary>Seconds left standing at the spot, or negative while still walking to it.</summary>
    private float spotCheckRemaining = -1f;

    /// <summary>Seconds left of the look-around at the current point. See
    /// SO_NemesisData.SearchPauseTime for why the search stands still at all.</summary>
    private float pauseRemaining;

    /// <summary>It has reached the current point and started (or finished) looking around there.
    /// Reset every time it sets off somewhere.</summary>
    private bool pausedHere;

    /// <summary>The belief sequence the sweep last acted on. See SearchSweepRules.Judge.</summary>
    private int consumedSequence;

    /// <summary>The used hiding spots already rolled this search (Fase 2D): one roll each per search,
    /// not one per re-centre.</summary>
    private readonly System.Collections.Generic.HashSet<HidingSpot> usedSpotsRolled =
        new System.Collections.Generic.HashSet<HidingSpot>();

    /// <summary>
    /// The spots the player has used inside the area being swept are candidates (plan §17.6, D23,
    /// Fase 2D): NemesisHidingAwareness rolls them by how used they are and suspects the one that comes
    /// up, and the spot check below walks over and opens it (case 39). Asked whenever the area is set,
    /// moves somewhere else or widens — never outside it (case 40).
    /// </summary>
    private void ConsiderUsedSpots()
    {
        NemesisHidingAwareness awareness = nemesisStateManager.HidingAwareness;
        if (awareness == null || !freeRoam.IsCommitted) return;
        awareness.ConsiderUsedSpots(freeRoam.Anchor, freeRoam.Radius, usedSpotsRolled);
    }

    /// <summary>Entered with the agent switched off (the lift ride): the first point is chosen on the
    /// first UpdateState with an agent to give it to.</summary>
    private bool needsFirstPoint;

    // ── Cooling (plan §18.5 B) ───────────────────────────────────────────────

    /// <summary>When the last evidence that renews the search came in (Time.time), and what kind it
    /// was. Tracked apart from the sweep's own sequence because it has to keep counting through a
    /// spot check, where the sweep does not look at evidence at all.</summary>
    private float lastRenewalAt;

    /// <summary>When it last stood at (or gave up walking to) the point its evidence came from: the
    /// entry itself, or the frame the sweep's owed visit to the anchor was settled. See
    /// <see cref="Silence"/>.</summary>
    private float reachedEvidenceAt;

    private bool renewedBySight;
    private bool renewedMuffled;
    private int renewedSequence;

    /// <summary>Entered from Investigating: the short search after an empty investigation of the
    /// player's own noise (D26), with a fraction of the cap.</summary>
    private bool escalated;

    /// <summary>
    /// Whether the search should go on — what the ladder's "le queda presupuesto de búsqueda" reads
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
    /// renews it, or since it got to the point that evidence came from, whichever is later — and none
    /// at all while it is still on its way there. For the HUD and the ladder.
    ///
    /// FROM THE ARRIVAL, NOT FROM THE EVIDENCE (playtest 27/09). Counted from the evidence alone, the
    /// walk to it ate the window: a footstep heard fifteen metres away is five or six seconds of walk,
    /// so the search reached the spot with most of its eight seconds gone and gave up after a look or
    /// two — worst exactly over the long distances where the player had got furthest. Nothing about
    /// that walk is silence the Nemesis has listened to where the player was.
    /// </summary>
    public float Silence =>
        Time.time - Mathf.Max(lastRenewalAt, freeRoam.IsAnchorPending ? Time.time : reachedEvidenceAt);

    /// <summary>Whether it is still on its way to the point the evidence came from. For the HUD.
    /// </summary>
    public bool IsHeadingToEvidence => freeRoam.IsAnchorPending;

    /// <summary>The silence it tolerates right now: the window (as lent by the Director) times the
    /// quality of the last evidence.</summary>
    public float QuietWindow => Data != null ? Data.SearchQuietWindow * Quality : 0f;

    /// <summary>The most it will search, in seconds in the state: the cap (as lent by the Director),
    /// shortened for the escalated search of D26.</summary>
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

    /// <summary>It has covered everything it can reach at the widest the sweep may get: "I have
    /// looked everywhere here".</summary>
    public bool SearchedEverything =>
        freeRoam.IsFullySwept && freeRoam.Radius >= MaxSweepRadius - 0.01f;

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
        freeRoam = new NemesisFreeRoam(stateManager);
    }

    private SO_NemesisData Data => nemesisStateManager.NemesisData;

    /// <summary>"Same floor" for the sweep, the same band NemesisFreeRoam filters its candidates
    /// with: two definitions let evidence count as inside a disc whose candidates it filtered out.
    /// </summary>
    private const float FloorBand = NemesisFreeRoam.FloorBand;

    private float MaxSweepRadius => Mathf.Max(MinSweepRadius, Data != null ? Data.RoomSweepRadius : 8f);

    public override void EnterState()
    {
        NextState = StateKey;
        pauseRemaining = 0f;
        pausedHere = false;
        spotTarget = null;
        spotCheckRemaining = -1f;
        freeRoam.Release();

        escalated = nemesisStateManager.HasPreviousState &&
                    nemesisStateManager.PreviousStateKey == NemesisStateManager.ENemesisState.Investigating;

        nemesisStateManager.SetGait(NemesisStateManager.EGait.Running,
                                    nemesisStateManager.NemesisMovement.SearchSpeed);

        // Measured before anything moves the destination away from here.
        bool standingWhereLost = IsStandingWhereLost();

        StartSweep();
        StartCooling();
        reachedEvidenceAt = Time.time;

        usedSpotsRolled.Clear();
        ConsiderUsedSpots();

        if (!nemesisStateManager.IsAgentReady)
        {
            needsFirstPoint = true;
            return;
        }

        needsFirstPoint = false;

        // A hiding spot to check takes the destination anyway (TickSpotCheck below): picking a sweep
        // point first would only pay for path queries to throw the answer away.
        NemesisHidingAwareness awareness = nemesisStateManager.HidingAwareness;
        if (awareness != null && awareness.SpotToCheck != null)
        {
            TickSpotCheck();
            return;
        }

        // THE SEARCH STARTS WHERE IT LOST THEM. The chase walks back to the last sighting and hands
        // over on arrival; heading straight off to the first sweep point read as the Nemesis never
        // having cared where the player went. Pointing the agent at its own feet makes the arrival
        // logic below stop, look around (NemesisLookAround covers this state) and mark the spot as
        // swept, and only then set off.
        //
        // Unless it heard them somewhere past it since: then the lost spot is old news, and standing
        // there looking around while the player's footsteps lead away read as the Nemesis freezing
        // between states (playtest 27/09). It goes to where it heard them instead.
        if (standingWhereLost && !HasNewerEvidenceElsewhere())
            SetDestination(nemesisStateManager.transform.position);
        else SetDestination(PickNextPoint());
    }

    public override void ExitState()
    {
        EndSpotCheck();
        freeRoam.Release();
        pausedHere = false;
        pauseRemaining = 0f;

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
        // spot check too. While the visit to the evidence point is still owed there is no silence
        // to count yet (see Silence); the frame it is settled is the last one stamped here.
        TrackRenewal();
        if (freeRoam.IsAnchorPending) reachedEvidenceAt = Time.time;

        if (needsFirstPoint)
        {
            needsFirstPoint = false;
            SetDestination(PickNextPoint());
        }

        // A hiding spot to check comes before everything else this state does — fresh evidence
        // included, which with the player in a locker is most likely their own breathing.
        if (TickSpotCheck()) return;

        TrackEvidence();

        if (!nemesisStateManager.HasArrived) return;

        // ARRIVED: STOP AND LOOK BEFORE MOVING ON.
        //
        // Chaining straight to the next destination is what made the search unreadable from the
        // outside: from inside a locker it just looks like an odd patrol. Standing still for a
        // moment at each point, sweeping its gaze, turns the search into something the player can
        // read and gamble against — and it keeps the path queries behind each pick to once a
        // second or so.
        //
        // Armed HERE, on arrival, and not when setting off (plan §18.1): that left the first point
        // without a pause and had IsPausing true all the way to the next one.
        if (!pausedHere)
        {
            pausedHere = true;
            pauseRemaining = Data != null ? Data.SearchPauseTime : 0f;
            freeRoam.MarkSwept(SearchTarget);
        }

        if (pauseRemaining > 0f)
        {
            pauseRemaining -= Time.deltaTime;

            nemesisStateManager.NavAgent.velocity = Vector3.zero;
            nemesisStateManager.SetGait(NemesisStateManager.EGait.Idle, 0f);
            return;
        }

        nemesisStateManager.SetGait(NemesisStateManager.EGait.Running,
                                    nemesisStateManager.NemesisMovement.SearchSpeed);
        SetDestination(PickNextPoint());
    }

    // ── The sweep ────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets up the sweep on entry: around the belief if there is one, around where it stands if
    /// there is not — entered straight from a capture, or on the strength of a known hiding spot
    /// alone (the ladder's "sabe en qué escondite está" asks for no belief).
    /// </summary>
    private void StartSweep()
    {
        NemesisBelief belief = nemesisStateManager.Belief;

        if (belief != null && belief.HasBelief)
        {
            Vector3 centre = OnNavMesh(belief.Position);
            freeRoam.Commit(centre, SweepRadiusFor(belief), RoomFor(belief, centre), MayVisit(belief));
            consumedSequence = belief.Sequence;
            return;
        }

        float radius = Data != null ? Data.SearchSweepRadius : 5f;
        freeRoam.Commit(nemesisStateManager.transform.position, radius, null, false);
        consumedSequence = belief != null ? belief.Sequence : 0;
    }

    /// <summary>
    /// Whether the sweep should walk to the evidence point itself before sweeping around it
    /// (<see cref="NemesisFreeRoam.IsAnchorPending"/>): always, except for the player's noise from
    /// inside a hiding spot — that point is the locker door, and the whole of D22 is keeping the
    /// search off it.
    /// </summary>
    private static bool MayVisit(NemesisBelief belief) => !belief.LastEvidenceFromHidingSpot;

    /// <summary>
    /// On entering at the spot where it lost them: whether the player has been heard since, far
    /// enough from here that the lost spot is no longer the newest thing it knows.
    /// </summary>
    private bool HasNewerEvidenceElsewhere()
    {
        if (!freeRoam.IsAnchorPending) return false;

        Vector3 offset = freeRoam.Anchor - nemesisStateManager.transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude > AnchorRetargetDistance * AnchorRetargetDistance;
    }

    /// <summary>How far the evidence point may move from where the Nemesis is heading before a walk
    /// to it is re-aimed. Coarse on purpose: re-aiming costs a path query, and the player's
    /// footsteps move the belief ten times a second.</summary>
    private const float AnchorRetargetDistance = 2.5f;

    /// <summary>
    /// Follows new evidence about the player — questions 1 and 4 of the plan's §17.4, via
    /// <see cref="SearchSweepRules"/>.
    ///
    /// Evidence inside the disc FOLLOWS it (<see cref="NemesisFreeRoam.Follow"/>): the centre slides
    /// to it, the radius grows if this evidence is vaguer than the one the disc was sized for (a
    /// breath from a locker after a footstep in the open, D22), the room is re-read, and nothing
    /// swept is forgotten. Evidence outside it moves the disc there with a radius from that evidence.
    /// Either way, if the point it was walking to is no longer inside the area, it picks another —
    /// at once after a jump (reacting is the point), and after the look-around if it is standing at
    /// a point already.
    ///
    /// Leads (decoys, Director pulses) never get here: they do not move the belief's sequence.
    /// </summary>
    private void TrackEvidence()
    {
        NemesisBelief belief = nemesisStateManager.Belief;
        if (belief == null || !belief.HasBelief) return;

        SearchSweepRules.EVerdict verdict = SearchSweepRules.Judge(
            consumedSequence, belief.Sequence, freeRoam.IsCommitted, freeRoam.Anchor, freeRoam.Radius,
            belief.Position, FloorBand);

        if (verdict == SearchSweepRules.EVerdict.Keep) return;

        consumedSequence = belief.Sequence;

        Vector3 centre = OnNavMesh(belief.Position);
        bool jumped = verdict == SearchSweepRules.EVerdict.Recenter;
        bool mayVisit = MayVisit(belief);

        if (jumped)
        {
            freeRoam.Recenter(centre, SweepRadiusFor(belief), RoomFor(belief, centre), mayVisit);
            ConsiderUsedSpots();
        }
        else freeRoam.Follow(centre, SweepRadiusFor(belief), RoomFor(belief, centre), mayVisit);

        // Still heading somewhere inside the area: keep going, that point is as good as any — unless
        // it still owes the evidence point a visit, and that point has moved away from where it is
        // heading: then it is the new point it goes to.
        bool onCourse = freeRoam.IsAnchorPending
            ? (SearchTarget - freeRoam.Anchor).sqrMagnitude <= AnchorRetargetDistance * AnchorRetargetDistance
            : SearchSweepRules.IsInside(freeRoam.Anchor, freeRoam.Radius, SearchTarget, FloorBand);
        if (onCourse) return;

        // Standing at a point, looking around: after a small slide, finish looking first — the next
        // pick already uses the moved area. After a jump, react now: standing around finishing a
        // look-around after hearing the player across the room is the opposite of searching.
        if (IsPausing && !jumped) return;

        pausedHere = false;
        pauseRemaining = 0f;
        nemesisStateManager.SetGait(NemesisStateManager.EGait.Running,
                                    nemesisStateManager.NemesisMovement.SearchSpeed);
        SetDestination(PickNextPoint());
    }

    /// <summary>
    /// The next place to look. Opens the area a step wider as soon as it turns out to be covered, so
    /// the search works outwards — where the player could have got to keeps growing while nothing new
    /// is heard — instead of re-walking the same few points. Falls back to a scatter around its
    /// destination only when the area offers nothing reachable at all.
    ///
    /// The widening is checked right after the pick that discovers the area is covered, and the pick
    /// is taken again on the wider disc. Checking only before picking (as it first did) opened the
    /// disc one trip late: the covering pick still sent the Nemesis back to somewhere it had looked.
    /// </summary>
    private Vector3 PickNextPoint()
    {
        if (!freeRoam.IsCommitted) StartSweep();

        if (!freeRoam.TryGetNextPoint(out Vector3 point))
        {
            // Nowhere reachable in the area as it is: open it before falling back to a scatter, which
            // at worst hands back the Nemesis's own feet — a search spent standing in one place.
            while (freeRoam.Widen(MaxSweepRadius))
            {
                if (freeRoam.TryGetNextPoint(out point)) return point;
            }

            return GetRandomPointInNavMesh();
        }

        if (freeRoam.IsFullySwept && freeRoam.Widen(MaxSweepRadius))
        {
            // The area grew: used hiding spots that now fall inside it are candidates too.
            ConsiderUsedSpots();
            if (freeRoam.TryGetNextPoint(out Vector3 wider)) return wider;
        }

        return point;
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

    private float MinSweepRadius => Data != null ? Data.SearchSweepMinRadius : 3f;

    /// <summary>How wide to sweep around the belief's last evidence. See
    /// <see cref="SearchSweepRules.SweepRadius"/>.</summary>
    private float SweepRadiusFor(NemesisBelief belief)
    {
        SO_NemesisData data = Data;
        float margin = data != null ? data.SearchSweepEvidenceMargin : 1f;
        float max = data != null ? data.RoomSweepRadius : 8f;

        return SearchSweepRules.SweepRadius(belief.EvidenceRadius, margin, MinSweepRadius, max);
    }

    /// <summary>
    /// The room the sweep should favour, or null. Only when the evidence is precise enough to say
    /// which side of a doorway the player is on: a sighting, or a noise pinned down to a couple of
    /// metres. For a sighting it looks a step and a half further along the direction the player was
    /// OBSERVED moving — the last sighting is usually the doorway itself, which belongs to neither
    /// side — so a player seen going INTO a room gets that room swept first.
    /// </summary>
    private static string RoomFor(NemesisBelief belief, Vector3 centre)
    {
        const float PreciseEnough = 2f;
        const float LookAhead = 1.5f;
        const float MinSpeed = 0.3f;

        if (belief.EvidenceRadius > PreciseEnough) return null;

        if (belief.IsAnchoredBySight)
        {
            Vector3 velocity = belief.ObservedVelocity;
            velocity.y = 0f;

            if (velocity.magnitude >= MinSpeed &&
                NemesisRooms.TryGetRoom(centre + velocity.normalized * LookAhead, out string ahead))
                return ahead;
        }

        return NemesisRooms.TryGetRoom(centre, out string here) ? here : null;
    }

    /// <summary>
    /// Whether the Nemesis is standing on the spot where it last saw the player — the chase walked
    /// it back there. Read off the sighting the belief keeps, not the fused belief itself: footsteps
    /// heard afterwards are not where it lost them.
    /// </summary>
    private bool IsStandingWhereLost()
    {
        const float Radius = 2.5f;

        NemesisBelief belief = nemesisStateManager.Belief;
        if (belief == null || !belief.TryGetLastSeen(out Vector3 lastSeen, out float age)) return false;
        if (age >= NemesisPursuit.RecentSightingSeconds) return false;

        Vector3 offset = lastSeen - nemesisStateManager.transform.position;

        // Straight above or below is not "where it lost them": a flat test alone said yes one floor
        // off.
        if (Mathf.Abs(offset.y) > FloorBand) return false;

        offset.y = 0f;
        return offset.sqrMagnitude <= Radius * Radius;
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
    /// and goes back to sweeping (plan §3.5: the known spot first, then the area).
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
            // pick the sweep up from wherever it is standing. ReferenceEquals and not ==: a spot
            // destroyed under it (a scene unloading) compares equal to null through Unity's
            // operator, and skipping EndSpotCheck then would leak the 0.25 m stopping distance
            // into every state after this one.
            if (ReferenceEquals(spotTarget, null)) return false;

            EndSpotCheck();
            ResumeSweep();
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
        ResumeSweep();
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

    private void ResumeSweep()
    {
        pausedHere = false;
        pauseRemaining = 0f;
        nemesisStateManager.SetGait(NemesisStateManager.EGait.Running,
                                    nemesisStateManager.NemesisMovement.SearchSpeed);
        SetDestination(PickNextPoint());
    }

    /// <summary>Points the agent somewhere and records it, so the HUD and the gizmos can say what
    /// the search is currently looking at.</summary>
    private void SetDestination(Vector3 point)
    {
        SearchTarget = point;
        pausedHere = false;
        nemesisStateManager.NavAgent.destination = point;
    }

    /// <summary>
    /// A point on the NavMesh near the current destination: the last resort when the sweep has
    /// nowhere reachable to offer.
    ///
    /// Returns the position snapped by SamplePosition and not the raw random point: the raw
    /// one usually falls off the mesh, and setting it as a destination made the agent walk to
    /// the nearest edge instead. The attempts are capped because the original do/while had no
    /// way out — with the agent outside the NavMesh it span forever and hung Unity.
    ///
    /// Sampled on the Nemesis's own area mask and path-tested, since it is now the fallback of every
    /// pick that fails: a point on another island is the wall-hugging failure NemesisPursuit
    /// describes.
    /// </summary>
    private Vector3 GetRandomPointInNavMesh()
    {
        const int maxAttempts = 30;
        const float sampleRadius = 1f;
        const int maxPathTests = 4;

        SO_NemesisData data = Data;
        float range = data != null ? data.SearchSweepRadius : 5f;

        Vector3 origin = nemesisStateManager.NavAgent.destination;

        Vector3 forward = nemesisStateManager.transform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.zero;

        Vector3 self = nemesisStateManager.transform.position;
        int pathTests = 0;

        for (int i = 0; i < maxAttempts && pathTests < maxPathTests; i++)
        {
            // Horizontal only: onUnitSphere also varied Y and threw points above and below
            // the floor. The forward bias is kept so it sweeps ahead of where it is looking.
            Vector2 circle = Random.insideUnitCircle;
            Vector3 randomDir = new Vector3(circle.x, 0f, circle.y) + forward;
            Vector3 randomPoint = origin + randomDir * range;

            if (!NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, sampleRadius, NemesisNav.AreaMask))
                continue;

            pathTests++;
            if (NemesisNav.IsReachable(self, hit.position)) return hit.position;
        }

        // Nothing valid nearby: stay put rather than heading for an unreachable point.
        return nemesisStateManager.transform.position;
    }
}
