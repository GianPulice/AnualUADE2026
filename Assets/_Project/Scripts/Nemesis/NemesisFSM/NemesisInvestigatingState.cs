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

    public override void EnterState()
    {
        NextState = StateKey;

        hasDestination = false;
        arrivedAt = -1f;
        lastRetargetTime = float.NegativeInfinity;

        nemesisStateManager.SetGait(NemesisStateManager.EGait.Walking,
                                    nemesisStateManager.NemesisMovement.InvestigationSpeed);

        // A suspected hiding spot first; otherwise aim at what was heard straight away — the sensor
        // has already sampled it this frame.
        spotTarget = null;
        if (!TrackSuspectedSpot()) TryRetarget(force: true);
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
            nemesisStateManager.SetGait(NemesisStateManager.EGait.Walking,
                                        nemesisStateManager.NemesisMovement.InvestigationSpeed);
            return;
        }

        // A new noise somewhere else is worth walking to — but only a meaningfully different one,
        // and not more often than the retarget interval. That is the whole difference between
        // investigating a sound and following the player by ear.
        if (spotTarget == null && TryRetarget(force: false))
        {
            arrivedAt = -1f;
            nemesisStateManager.SetGait(NemesisStateManager.EGait.Walking,
                                        nemesisStateManager.NemesisMovement.InvestigationSpeed);
            return;
        }

        if (!hasDestination) return;

        if (arrivedAt < 0f && nemesisStateManager.HasArrived)
        {
            // Got there: stop and look. The ladder holds this state for the dwell through
            // IsInspecting; when it runs out the ladder falls through to whatever comes next.
            arrivedAt = Time.time;
            nemesisStateManager.NavAgent.velocity = Vector3.zero;
            nemesisStateManager.SetGait(NemesisStateManager.EGait.Idle, 0f);
        }

        // Looked at the suspected spot for the whole dwell and nothing came of it: nobody is in
        // there. Forgetting it is what lets the rung that has been holding this state go.
        if (spotTarget != null && arrivedAt >= 0f && !IsInspecting)
        {
            NemesisHidingAwareness awareness = nemesisStateManager.HidingAwareness;
            if (awareness != null) awareness.MarkChecked(spotTarget);
            DropSpot();
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
    /// Points the agent at the last heard position if it is new enough and far enough from the
    /// current destination. Returns true when it did.
    /// </summary>
    private bool TryRetarget(bool force)
    {
        // Entering during the lift ride: writing a destination to the disabled agent errors.
        if (!nemesisStateManager.IsAgentReady) return false;

        FieldOfListening ears = nemesisStateManager.FieldOfListening;
        if (ears == null || !ears.HasLastKnownPosition) return false;

        // Only a sound heard NOW moves the destination. Memory of an old one is what it is already
        // walking to.
        if (!force && !nemesisStateManager.HasAudioTarget) return false;

        SO_NemesisData data = nemesisStateManager.NemesisData;
        float interval = data != null ? data.InvestigationRetargetInterval : 1.5f;
        float minShift = data != null ? data.InvestigationRetargetDistance : 3f;

        Vector3 heard = ears.LastKnownPosition;
        if (!force)
        {
            if (Time.time - lastRetargetTime < interval) return false;
            if (hasDestination && Vector3.Distance(heard, destination) < minShift) return false;
        }

        destination = heard;
        hasDestination = true;
        lastRetargetTime = Time.time;
        nemesisStateManager.NavAgent.destination = destination;
        return true;
    }
}
