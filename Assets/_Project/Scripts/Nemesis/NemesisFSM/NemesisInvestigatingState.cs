using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Walking to where the Nemesis HEARD something, and looking around once it gets there.
///
/// GO TO THE NOISE, DO NOT FOLLOW IT (WIR-006). This used to set the destination to the live
/// noise position on every frame the player was audible — which, with the player walking nearby,
/// is a chase: the monster homing in on you at walking pace, with none of the chase feedback,
/// because the red vignette and the music belong to Chasing. The destination is now the point the
/// noise came from, and it only moves when a new noise lands far enough away
/// (<see cref="SO_NemesisData.InvestigationRetargetDistance"/>) and no more often than
/// <see cref="SO_NemesisData.InvestigationRetargetInterval"/>.
///
/// WHAT IT WALKS TO (playtest 27/09): what brought it here, in the ladder's order — a glimpse, the
/// player's own noise, a lead — and, for the player, their LATEST noise even after they have gone
/// out of earshot, so the walk ends where they were last heard. It used to walk to the loudest noise
/// of the last sweep whoever made it.
///
/// ARRIVING IS NOT FINISHING (DIS-002). The ladder used to let go the moment the agent arrived,
/// so a noise closer than the stopping distance was investigated for about a second. On arrival the
/// Nemesis now stops and sweeps its gaze for <see cref="SO_NemesisData.InvestigationDwellTime"/>
/// (NemesisLookAround does the sweeping), and <see cref="IsInspecting"/> is what the rung
/// "revisa donde escuchó el ruido" reads to hold the state meanwhile. A fresh noise elsewhere sends
/// it walking again.
///
/// A SUSPECTED HIDING SPOT OUTRANKS THE NOISE (plan §3.4, level A's grey zone): the Nemesis caught
/// the player getting into it out of the corner of its eye, so it walks to the spot's approach
/// point and looks around there for the same dwell. The rung "sospecha de un escondite" holds the
/// state until the look comes back empty — had the player been inside, the proximity rule would
/// have found them from where it is standing — and then the spot is marked checked.
///
/// The ladder still owns every way in and out; this only walks, stops and looks.
/// </summary>
public class NemesisInvestigatingState : BaseState<NemesisStateManager.ENemesisState>
{
    private readonly NemesisStateManager nemesisStateManager;

    private Vector3 destination;
    private bool hasDestination;
    private float lastRetargetTime;
    private float arrivedAt = -1f;

    /// <summary>What the destination came from. Runtime only.</summary>
    private enum ESource
    {
        None,
        Player,
        Lead,
        Glimpse,

        /// <summary>A suspected hiding spot. Not a glimpse: looking at it and finding nothing is not
        /// the choice's glimpse checked (review 28/09).</summary>
        Spot,
    }

    private ESource source;

    /// <summary>When the evidence the destination came from was sensed (Time.time), so a newer one
    /// can be told from the same one heard again.</summary>
    private float sourceAt = float.NegativeInfinity;

    // The focus (NemesisChoice) as last followed: a switch is re-aimed at once, an update of a lead
    // when it moved.
    private int consumedSwitchSequence = int.MinValue;
    private int consumedFocusSequence = int.MinValue;

    /// <summary>The lead or glimpse it walked to has been reported as checked (NemesisChoice
    /// .MarkFocusChecked): once per destination.</summary>
    private bool reportedChecked;

    /// <summary>
    /// Changing its mind shows (plan §17.3): on a switch of focus it stops, turns to the new thing and
    /// only then walks. The player reads "it changed its mind", not "it glitched".
    /// </summary>
    private const float ReactPause = 0.4f;

    /// <summary>The area around the noise whose used hiding spots are candidates (Fase 2D): the room
    /// it is standing in, give or take.</summary>
    private const float UsedSpotsRadius = 6f;

    private readonly System.Collections.Generic.HashSet<HidingSpot> usedSpotsRolled =
        new System.Collections.Generic.HashSet<HidingSpot>();
    private float reactUntil = float.NegativeInfinity;
    private bool reacting;

    /// <summary>The suspected hiding spot this investigation is walking to or looking at, or null.
    /// </summary>
    private HidingSpot spotTarget;

    /// <summary>The suspected hiding spot being walked to or looked at, for the HUD and the gizmos.
    /// </summary>
    public HidingSpot SpotTarget => spotTarget;

    public NemesisInvestigatingState(NemesisStateManager.ENemesisState key, NemesisStateManager stateManager) : base(key)
    {
        nemesisStateManager = stateManager;
    }

    /// <summary>
    /// True while the Nemesis is standing where it heard the noise, looking around, and the dwell
    /// has not run out. The predicate IsInspectingNoise reads this; NemesisLookAround sweeps the
    /// gaze on it.
    /// </summary>
    public bool IsInspecting
    {
        get
        {
            if (!hasDestination) return false;

            // The frame it arrives counts. The ladder decides BEFORE this state updates, so on
            // that frame arrivedAt has not been stamped yet — reading only the stamp, the ladder
            // would see "arrived, not inspecting" and let go on the very frame the inspection was
            // meant to begin, which is DIS-002 all over again.
            if (arrivedAt < 0f) return nemesisStateManager.HasArrived;

            SO_NemesisData data = nemesisStateManager.NemesisData;
            float dwell = data != null ? data.InvestigationDwellTime : 0f;
            return Time.time - arrivedAt < dwell;
        }
    }

    /// <summary>
    /// For the ladder's "investigó un ruido tuyo y sigue tibio" (D26): the silence about the player is
    /// still inside the search's window (as lent by the Director, scaled by how good the evidence
    /// was), so the investigation turns into a short search instead of back into patrol.
    ///
    /// WALKING TO THEIR OWN NOISE, THE SILENCE COUNTS FROM WHEN IT GOT THERE, like the search's
    /// (NemesisSearchingState.Silence), and it is warm all the way there: the walk is not silence it
    /// has listened to. It used to be the plain belief age, so a noise far enough away to take five or
    /// six seconds to reach, plus the four of the look-around, was past the window by the end of it —
    /// the far noise never turned into a search and the Nemesis went back to patrol (playtest 27/09).
    ///
    /// Anything else (a lead, a glimpse, a suspected spot) keeps the plain gate: the belief itself is
    /// younger than the window. A lead alone never escalates that way (D26) — a decoy found empty says
    /// nothing about the player — and a glimpse is not evidence the belief keeps.
    /// </summary>
    public bool IsWarm
    {
        get
        {
            NemesisBelief belief = nemesisStateManager.Belief;
            SO_NemesisData data = nemesisStateManager.NemesisData;
            if (data == null || belief == null || !belief.HasBelief) return false;

            float window = data.SearchQuietWindow *
                           SearchCooling.Quality(belief.IsAnchoredBySight, belief.LastEvidenceMuffled,
                                                 data.SearchQualitySight, data.SearchQualityMuffled);

            if (source != ESource.Player) return belief.Age < window;

            if (arrivedAt < 0f) return true;

            float evidenceAt = Time.time - belief.Age;
            return Time.time - Mathf.Max(evidenceAt, arrivedAt) < window;
        }
    }

    public override void EnterState()
    {
        NextState = StateKey;

        hasDestination = false;
        arrivedAt = -1f;
        lastRetargetTime = float.NegativeInfinity;
        source = ESource.None;
        sourceAt = float.NegativeInfinity;
        reportedChecked = false;
        reacting = false;

        // One roll per used hiding spot per investigation (Fase 2D), not per arrival.
        usedSpotsRolled.Clear();

        nemesisStateManager.SetGait(NemesisStateManager.EGait.Walking,
                                    nemesisStateManager.NemesisMovement.InvestigationSpeed);

        // A suspected hiding spot first; otherwise aim at what brought it here straight away — the
        // sensors have already sampled it this frame.
        spotTarget = null;
        if (!TrackSuspectedSpot()) AimOnEntry();
    }

    public override void ExitState()
    {
        arrivedAt = -1f;
        hasDestination = false;
        DropSpot();
    }

    public override void UpdateState()
    {
        // Agent switched off (freight elevator ride): nothing to ask of it this frame. See
        // NemesisStateManager.IsAgentReady.
        if (!nemesisStateManager.IsAgentReady) return;

        // A suspected hiding spot outranks a noise: it is a place, and the noise is most likely the
        // breathing coming out of it.
        if (TrackSuspectedSpot())
        {
            arrivedAt = -1f;
            reacting = false;
            nemesisStateManager.SetGait(NemesisStateManager.EGait.Walking,
                                        nemesisStateManager.NemesisMovement.InvestigationSpeed);
            return;
        }

        // Going to a spot, nothing retargets it (below). What the choice changed its mind about on the
        // way is not news once the spot is done: without this, the first frame after the check
        // re-aimed at a switch seconds old, with its turning beat (review 28/09).
        if (spotTarget != null) ConsumeFocusChanges();

        // Something newer somewhere else is worth walking to — but only a meaningfully different
        // place, and not more often than the retarget interval. That is the whole difference between
        // investigating a sound and following the player by ear.
        if (spotTarget == null && TryRetarget())
        {
            arrivedAt = -1f;
            if (!reacting)
            {
                nemesisStateManager.SetGait(NemesisStateManager.EGait.Walking,
                                            nemesisStateManager.NemesisMovement.InvestigationSpeed);
            }
            return;
        }

        // Just changed its mind: a beat facing the new thing before walking (plan §17.3).
        if (TickReact()) return;

        if (!hasDestination) return;

        if (arrivedAt < 0f && nemesisStateManager.HasArrived)
        {
            // Got there: stop and look. The ladder holds this state for the dwell through
            // IsInspecting; when it runs out the ladder falls through to whatever comes next.
            arrivedAt = Time.time;
            nemesisStateManager.NavAgent.velocity = Vector3.zero;
            nemesisStateManager.SetGait(NemesisStateManager.EGait.Idle, 0f);

            // At a suspected spot, looking means opening it: a player inside is found here, holding
            // their breath or not (plan §17.6). At a plain noise it is only the look around —
            // NemesisLookAround scans while IsInspecting — and a player holding their breath in a
            // spot it does not suspect is not found by it standing nearby (D21).
            NemesisHidingAwareness hiding = nemesisStateManager.HidingAwareness;
            if (spotTarget != null)
            {
                if (hiding != null) hiding.Open(spotTarget);
            }
            else if (hiding != null)
            {
                // At the noise: the hiding spots the player has used around here are candidates
                // (plan §17.6, D23, Fase 2D; case 39). One that comes up is suspected, and the next
                // frame walks to it and opens it. Only this area: never one across the level (case 40).
                hiding.ConsiderUsedSpots(destination, UsedSpotsRadius, usedSpotsRolled);
            }
        }

        // Looked at the suspected spot for the whole dwell and nothing came of it: nobody is in
        // there. Forgetting it is what lets the rung that has been holding this state go.
        if (spotTarget != null && arrivedAt >= 0f && !IsInspecting)
        {
            NemesisHidingAwareness awareness = nemesisStateManager.HidingAwareness;
            if (awareness != null) awareness.MarkChecked(spotTarget);
            DropSpot();
        }

        // Looked where the lead or the glimpse was for the whole dwell, and nothing: the choice counts
        // the visit (a decoy's habituation, §17.5) and lets go of it, which is what releases the
        // "su atención está en una pista" rung that has been holding this state.
        if (!reportedChecked && spotTarget == null && arrivedAt >= 0f && !IsInspecting &&
            (source == ESource.Lead || source == ESource.Glimpse))
        {
            reportedChecked = true;
            NemesisChoice choice = nemesisStateManager.Choice;
            if (choice != null) choice.MarkFocusChecked();
        }
    }

    /// <summary>
    /// Points the investigation at the suspected hiding spot when there is one it is not already
    /// heading for. Returns true on the frame it switched to it.
    /// </summary>
    private bool TrackSuspectedSpot()
    {
        HidingSpot suspected = nemesisStateManager.SuspectedHidingSpot;
        if (ReferenceEquals(suspected, spotTarget)) return false;

        if (suspected == null)
        {
            // Forgotten from outside — seen out in the open, or the suspicion expired. Back to
            // listening; the destination it had is as good as any until something new is heard.
            DropSpot();
            return false;
        }

        if (!nemesisStateManager.IsAgentReady) return false;

        spotTarget = suspected;
        destination = suspected.ApproachPoint.position;
        hasDestination = true;
        lastRetargetTime = Time.time;

        // A suspected spot is the player glimpsed getting in: a look that comes back empty may still
        // escalate to a short search (IsWarm, the plain gate).
        source = ESource.Spot;
        sourceAt = Time.time;

        // Right up to the approach point: see NemesisStateManager.SpotCheckStoppingDistance.
        nemesisStateManager.SetStoppingDistance(NemesisStateManager.SpotCheckStoppingDistance);
        nemesisStateManager.NavAgent.destination = destination;
        return true;
    }

    private void DropSpot()
    {
        // ReferenceEquals: a spot destroyed under it still has to hand the stopping distance back.
        if (ReferenceEquals(spotTarget, null)) return;

        spotTarget = null;
        nemesisStateManager.SetStoppingDistance(nemesisStateManager.DefaultStoppingDistance);
    }

    /// <summary>
    /// Aims at whatever brought it here, in the ladder's own order: its focus on a lead ("su atención
    /// está en una pista", the highest Investigating rung), a glimpse ("vio algo de reojo"), the
    /// player's noise ("escucha un ruido").
    ///
    /// It used to aim at the loudest noise of the last sweep whatever the rung (playtest 27/09): a
    /// Director pulse over the player's footsteps, and — entered on a glimpse — whatever it had last
    /// heard, however long ago, or nothing at all. That was half of "it contradicts itself". A lead is
    /// only ever reached through the focus now (plan §17.4, Fase 2B part 4): hearing one is not a
    /// reason on its own.
    /// </summary>
    private void AimOnEntry()
    {
        // Entering during the lift ride: writing a destination to the disabled agent errors.
        if (!nemesisStateManager.IsAgentReady) return;

        NemesisChoice choice = nemesisStateManager.Choice;
        if (choice != null)
        {
            consumedSwitchSequence = choice.SwitchSequence;
            consumedFocusSequence = choice.FocusSequence;

            if (choice.IsFocusOnLead)
            {
                Aim(choice.FocusPosition, ESource.Lead, Time.time);
                return;
            }
        }

        FieldOfListening ears = nemesisStateManager.FieldOfListening;
        NemesisBelief belief = nemesisStateManager.Belief;

        if (nemesisStateManager.IsSuspicious && belief != null &&
            belief.TryGetGlimpse(out Vector3 glimpse, out float glimpseAge) && glimpseAge < GlimpseFreshness)
        {
            Aim(glimpse, ESource.Glimpse, Time.time - glimpseAge);
            return;
        }

        if (ears != null && nemesisStateManager.HearsPlayer && ears.TryGetLastPlayerNoise(out FieldOfListening.HeardNoise noise))
        {
            Aim(noise.Position, ESource.Player, noise.HeardAt);
            return;
        }

        // Entered on a rung that asks none of those this frame: where it believes the player is. It
        // used to be the last thing the ear caught, whoever made it.
        if (belief != null && belief.HasBelief) Aim(belief.Position, ESource.Player, Time.time - belief.Age);
    }

    /// <summary>How old a glimpse may be and still be where the corner of its eye caught something
    /// this moment. A few sensor sweeps.</summary>
    private const float GlimpseFreshness = 0.5f;

    /// <summary>
    /// Moves the destination when there is something newer worth walking to. Returns true when it did.
    ///
    /// THE FOCUS FIRST (plan §17.4, Fase 2B part 4). When the choice changes its mind — the player's
    /// step beats the chains (case 28), the radio beats a cold belief (case 31) — the walk follows at
    /// once, with a short beat of turning to the new thing (ReactPause): the choice has already weighed
    /// it, so no interval on top. While the focus is a lead, nothing else moves the walk: a player
    /// noise that deserved it would have changed the focus first.
    ///
    /// THEN THE PLAYER, AND THEIR LATEST NOISE, HEARD NOW OR NOT. Only a sound heard this very sweep
    /// used to move the destination, and the interval swallowed the ones in between: a player running
    /// out of earshot left the Nemesis walking to where they were a second and a half before their
    /// last footstep (playtest 27/09). A player noise newer than the one it is walking to wins once the
    /// interval allows, so the walk ends where they were last heard. The interval and the minimum
    /// shift stay for the player (WIR-006: go to the noise, do not follow it); plan part 5 revisits
    /// them.
    /// </summary>
    private bool TryRetarget()
    {
        if (!nemesisStateManager.IsAgentReady) return false;

        NemesisChoice choice = nemesisStateManager.Choice;
        if (choice != null)
        {
            if (choice.SwitchSequence != consumedSwitchSequence)
            {
                consumedSwitchSequence = choice.SwitchSequence;
                consumedFocusSequence = choice.FocusSequence;
                if (AimAtFocus(choice))
                {
                    React();
                    return true;
                }
            }

            if (choice.IsFocusOnLead)
            {
                if (choice.FocusSequence == consumedFocusSequence) return false;
                consumedFocusSequence = choice.FocusSequence;

                if (!IsWorthMoving(choice.FocusPosition, 1f)) return false;
                Aim(choice.FocusPosition, ESource.Lead, Time.time);
                return true;
            }
        }

        SO_NemesisData data = nemesisStateManager.NemesisData;
        float interval = data != null ? data.InvestigationRetargetInterval : 1.5f;
        float minShift = data != null ? data.InvestigationRetargetDistance : 3f;

        if (Time.time - lastRetargetTime < interval) return false;

        FieldOfListening ears = nemesisStateManager.FieldOfListening;

        FieldOfListening.HeardNoise noise = default;
        bool hasPlayerNoise = ears != null && ears.TryGetLastPlayerNoise(out noise);

        // Walking to their noise: any newer one of theirs. Walking to anything else: their noise heard
        // right now — a memory from before this walk started is not a reason to change it.
        bool newerPlayerNoise = hasPlayerNoise &&
            (source == ESource.Player ? noise.HeardAt > sourceAt : nemesisStateManager.HearsPlayer);

        if (newerPlayerNoise && IsWorthMoving(noise.Position, minShift))
        {
            Aim(noise.Position, ESource.Player, noise.HeardAt);
            return true;
        }

        // A glimpse that moved, while the glimpse is still all it has.
        NemesisBelief belief = nemesisStateManager.Belief;
        if ((source == ESource.Glimpse || source == ESource.Spot || source == ESource.None) &&
            nemesisStateManager.IsSuspicious &&
            belief != null && belief.TryGetGlimpse(out Vector3 glimpse, out float glimpseAge) &&
            glimpseAge < GlimpseFreshness && IsWorthMoving(glimpse, minShift))
        {
            Aim(glimpse, ESource.Glimpse, Time.time - glimpseAge);
            return true;
        }

        return false;
    }

    /// <summary>Points the walk at what the choice is now following. False when there is nothing to
    /// point at (the focus was dropped).</summary>
    private bool AimAtFocus(NemesisChoice choice)
    {
        switch (choice.FocusKind)
        {
            case FocusArbiter.EKind.Lead:
                Aim(choice.FocusPosition, ESource.Lead, Time.time);
                return true;

            case FocusArbiter.EKind.Glimpse:
                Aim(choice.FocusPosition, ESource.Glimpse, Time.time);
                return true;

            case FocusArbiter.EKind.Player:
                NemesisBelief belief = nemesisStateManager.Belief;
                if (belief == null || !belief.HasBelief) return false;
                Aim(belief.Position, ESource.Player, Time.time - belief.Age);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Marks every change of focus so far as seen, without acting on it.</summary>
    private void ConsumeFocusChanges()
    {
        NemesisChoice choice = nemesisStateManager.Choice;
        if (choice == null) return;

        consumedSwitchSequence = choice.SwitchSequence;
        consumedFocusSequence = choice.FocusSequence;
    }

    private void React()
    {
        reacting = true;
        reactUntil = Time.time + ReactPause;
    }

    /// <summary>Stands still, turning towards the new destination, for the React beat. True while it
    /// lasts.</summary>
    private bool TickReact()
    {
        if (!reacting) return false;

        if (Time.time >= reactUntil)
        {
            reacting = false;
            nemesisStateManager.SetGait(NemesisStateManager.EGait.Walking,
                                        nemesisStateManager.NemesisMovement.InvestigationSpeed);
            return false;
        }

        nemesisStateManager.NavAgent.velocity = Vector3.zero;
        nemesisStateManager.SetGait(NemesisStateManager.EGait.Idle, 0f);

        Vector3 facing = destination - nemesisStateManager.transform.position;
        facing.y = 0f;
        if (facing.sqrMagnitude > 0.0001f)
        {
            float turnSpeed = nemesisStateManager.NemesisMovement != null
                ? nemesisStateManager.NemesisMovement.AngularSpeed
                : 180f;
            nemesisStateManager.transform.rotation = Quaternion.RotateTowards(
                nemesisStateManager.transform.rotation, Quaternion.LookRotation(facing), turnSpeed * Time.deltaTime);
        }

        return true;
    }

    private bool IsWorthMoving(Vector3 point, float minShift) =>
        !hasDestination || Vector3.Distance(point, destination) >= minShift;

    private void Aim(Vector3 point, ESource from, float sensedAt)
    {
        destination = point;
        hasDestination = true;
        source = from;
        sourceAt = sensedAt;
        lastRetargetTime = Time.time;
        reportedChecked = false;
        nemesisStateManager.NavAgent.destination = destination;
    }
}
