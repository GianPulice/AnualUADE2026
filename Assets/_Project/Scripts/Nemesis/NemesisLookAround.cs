using UnityEngine;

/// <summary>
/// Where the Nemesis LOOKS when that is not simply where its body is pointing. The one driver of
/// <see cref="FieldOfView.LookDirection"/>: everything else reads the gaze, only this writes it.
///
/// WHY THE GAZE HAD TO BE SPLIT FROM THE BODY
///
/// The vision cone used to be cast from the view transform's forward, and the view transform is a
/// child of a root the NavMeshAgent rotates towards wherever it is walking. That coupling is
/// invisible while the Nemesis is moving and absurd the moment it stops: standing at a waypoint
/// for a second and a half, it stares down the corridor it just walked out of, for the whole wait,
/// with no way to look anywhere else. The player learns very quickly that a stopped Nemesis is a
/// solved Nemesis - its blind spot is wherever it is not currently walking, and it will never
/// check.
///
/// <see cref="FieldOfView.LookDirection"/> is the seam that fixes it, and this component is the
/// only thing that drives it. The body still belongs to the agent.
///
/// TWO REASONS TO LOOK SOMEWHERE ELSE, and a way back
///
/// 1. STANDING STILL ON PURPOSE: it sweeps from side to side. The three moments it is deliberately
///    stationary - waiting out a patrol waypoint, pausing at a search point, and inspecting the spot
///    a noise came from (InvestigationDwellTime).
///
///    On its own a scan makes the pause look busy. Together with the per-waypoint wait roll it makes
///    the pause genuinely dangerous: the player can no longer count the beats, AND the direction the
///    monster happens to be facing when the wait ends is no longer the direction it arrived from.
///    The search case is the one that changes how the game plays. A search that walks point to point
///    without stopping is unreadable from a hiding place; a search that stops and LOOKS tells the
///    player whether it is closing in or has written the area off, which is what makes staying put a
///    gamble instead of a coin flip.
///
/// 2. A CHASE (04/10, playtest: "en las esquinas me pierde muy fácil"): its eyes follow the player.
///    While it sees them the gaze stays ON them, so the body can turn to follow its path round a
///    table without swinging the cone off the person it is looking straight at. When it loses them
///    the gaze goes WHERE THEY WENT - a point a few metres past the lost spot along the heading it
///    last observed (<see cref="ChaseGaze"/>) - turning further into the corner the closer it gets,
///    so that it arrives already looking down the corridor they left by. Before this it arrived
///    facing the wall at the end of its own path, with that corridor ninety degrees off: outside the
///    cone, at the exact moment that mattered. Observed position and observed velocity only; this
///    never reads the player.
///
/// 3. THE WAY BACK. Out of a chase the gaze is turned back to the body's forward at the same capped
///    rate and only then released. That is the hand-over to the search: the body is about to turn
///    towards wherever the search sends it, and snapping the eye to a body still facing the wall
///    would throw away the one thing the chase had got right. A scan that starts in the meantime is
///    centred on wherever the gaze is, so a look-around at the lost spot sweeps the way they went.
///
/// A sweep still ends the way it always did, straight back to the body: only the chase eases.
///
/// Every other state is already pointed at something it cares about - the noise, the waypoint -
/// and swinging the cone off that would make it worse at the one job it is doing. With
/// SO_NemesisData.LostSightLookAhead at 0 the chase is one of them again, and this component is the
/// side-to-side sweep it was before.
/// </summary>
[RequireComponent(typeof(NemesisStateManager))]
public class NemesisLookAround : MonoBehaviour
{
    /// <summary>What is steering the gaze. Runtime only: for the debug HUD and the gizmos, where
    /// "why did it see me from there" needs to say which way it was looking and why.</summary>
    public enum EGaze
    {
        /// <summary>Nothing: the eye looks where the body points.</summary>
        Body,

        /// <summary>Sweeping from side to side while it stands waiting.</summary>
        Scan,

        /// <summary>Chasing a player it sees: the eyes stay on them.</summary>
        OnPlayer,

        /// <summary>Chasing a player it lost: looking the way it saw them go.</summary>
        LostTrail,

        /// <summary>The chase let go of the gaze and it is turning back to the body.</summary>
        Returning,
    }

    /// <summary>Degrees under which the returning gaze counts as back with the body and is released.
    /// Small enough that the hand-back cannot be seen as a jump of the cone.</summary>
    private const float AlignedDegrees = 2f;

    /// <summary>Turn rate when there is no data asset to read one from. Only the way back can get
    /// here: nothing steers without the asset.</summary>
    private const float FallbackTurnSpeed = 200f;

    [SerializeField] private NemesisStateManager stateManager;
    [SerializeField] private FieldOfView fieldOfView;

    /// <summary>Degrees travelled so far in the ping-pong. Offset by the half-angle when a scan
    /// starts so the sweep begins looking STRAIGHT AHEAD and works outwards, rather than snapping
    /// to one extreme on the first frame.</summary>
    private float scanPhase;

    /// <summary>The direction the sweep is centred on, captured once when the scan starts. Taken
    /// once rather than read per frame because reading it live would feed the eye's own rotation
    /// back into itself.</summary>
    private Vector3 scanCentre;

    private EGaze gaze = EGaze.Body;

    private Vector3 lostAt;
    private Vector3 lostAimPoint;

    /// <summary>What is steering the gaze right now. See <see cref="EGaze"/>.</summary>
    public EGaze Gaze => gaze;

    /// <summary>How far the gaze is turned off the body's forward, in degrees along the floor. 0
    /// while nothing is steering it. For the debug HUD.</summary>
    public float DegreesOffBody =>
        fieldOfView != null && gaze != EGaze.Body ? ChaseGaze.Angle(fieldOfView.LookDirection, BodyForward()) : 0f;

    /// <summary>
    /// While it looks the way a lost player went: where it lost them, and the point past it that the
    /// eyes are aimed at. For NemesisGizmos, which draws the two against the corner they are about.
    /// </summary>
    public bool TryGetLostTrail(out Vector3 lostPoint, out Vector3 aimPoint)
    {
        lostPoint = lostAt;
        aimPoint = lostAimPoint;
        return gaze == EGaze.LostTrail;
    }

    private void Awake()
    {
        if (stateManager == null) stateManager = GetComponent<NemesisStateManager>();

        // includeInactive: the sensors are switched off while the Nemesis is dormant, same reason
        // NemesisStateManager.ResolveHierarchyReferences passes it.
        if (fieldOfView == null) fieldOfView = GetComponentInChildren<FieldOfView>(true);
    }

    private void OnDisable()
    {
        // Hand the eye back. A component switched off mid-sweep would otherwise leave the cone
        // frozen at whatever angle it had reached, permanently off-axis from the body, and nothing
        // left alive to straighten it.
        if (gaze != EGaze.Body) HandBack();
    }

    private void Update()
    {
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;
        if (fieldOfView == null) return;

        SO_NemesisData data = stateManager != null ? stateManager.NemesisData : null;

        if (data != null && data.ScanHalfAngle > 0.01f && ShouldScan())
        {
            TickScan(data);
            return;
        }

        if (data != null && TryGetChaseAim(data, out Vector3 aim, out EGaze kind))
        {
            fieldOfView.LookDirection = ChaseGaze.Turn(FlatGaze(), aim, TurnStep(data));
            gaze = kind;
            return;
        }

        ReleaseGaze(data);
    }

    // -- The sweep -----------------------------------------------------------

    /// <summary>
    /// Standing still on purpose: at a patrol waypoint, at a search point, or where a noise came from.
    ///
    /// HasArrived rather than a velocity check: it is the same definition of "got there" the patrol
    /// state uses to start counting down its wait, so the scan begins exactly when the waiting
    /// does. A velocity threshold would also fire every time the agent slowed down at a corner.
    /// </summary>
    private bool ShouldScan()
    {
        if (stateManager == null) return false;

        switch (stateManager.CurrentStateKey)
        {
            // Waiting out PatrolWaypointWaitTime at a marker.
            case NemesisStateManager.ENemesisState.Patrolling:
                return stateManager.HasArrived;

            // Standing at a search point during SearchPauseTime. This is the case the pause was
            // added for: the scan is what makes a search legible from a hiding place. Without it
            // the Nemesis just stands there for a second and moves on, and from the outside that
            // says nothing about whether it is about to find you.
            case NemesisStateManager.ENemesisState.Searching:
                NemesisSearchingState searching = stateManager.SearchingState;
                return searching != null && searching.IsPausing;

            // Standing where it heard the noise, for InvestigationDwellTime (DIS-002). Same reason
            // as the search pause: stopping without looking says nothing about whether it is about
            // to find you.
            case NemesisStateManager.ENemesisState.Investigating:
                NemesisInvestigatingState investigating = stateManager.InvestigatingState;
                return investigating != null && investigating.IsInspecting;

            // Everything else is already pointed at something it cares about, and sweeping the
            // cone off that target would make the Nemesis worse at the one job it is doing. A chase
            // does steer the gaze, but AT something: see TryGetChaseAim.
            default:
                return false;
        }
    }

    private void TickScan(SO_NemesisData data)
    {
        float halfAngle = data.ScanHalfAngle;

        if (gaze != EGaze.Scan) BeginScanning(halfAngle);

        scanPhase += data.ScanSpeed * Time.deltaTime;

        // PingPong over the full width, re-centred: 0 -> +half -> -half -> +half. Starting the
        // phase at halfAngle (see BeginScanning) is what makes the first frame read as 0 degrees
        // off centre instead of hard over to one side.
        float offset = Mathf.PingPong(scanPhase, halfAngle * 2f) - halfAngle;

        fieldOfView.LookDirection = Quaternion.AngleAxis(offset, Vector3.up) * scanCentre;
    }

    private void BeginScanning(float halfAngle)
    {
        gaze = EGaze.Scan;
        scanPhase = halfAngle;

        // Centred on where it is LOOKING, which is the body's forward unless a chase left the gaze
        // somewhere better: a look-around at the spot where it lost the player sweeps the way they
        // went, not the wall its body ended up facing. Flattened: the sweep turns about the world's
        // up axis, so a Nemesis standing on a ramp should still look left and right along the floor
        // rather than tracing a tilted arc.
        scanCentre = FlatGaze();
    }

    // -- The chase -----------------------------------------------------------

    /// <summary>
    /// Where the eyes should be pointed during a chase, if anywhere in particular.
    ///
    /// ONLY WHAT THE EYES OBSERVED. The player it sees is at FieldOfView.LastKnownPosition, refreshed
    /// by every sweep that has them; the one it lost was last seen there, going at
    /// FieldOfView.LastKnownVelocity. Nothing here touches the player's transform - a player who
    /// turns the corner and doubles back is not where this looks, and that is theirs to use.
    ///
    /// The lost spot is the pursuit's own (NemesisPursuit.TryGetRecentSighting), so the eyes and the
    /// legs agree on where "where I lost them" is and on when a sighting is too old to be this chase.
    /// </summary>
    private bool TryGetChaseAim(SO_NemesisData data, out Vector3 aim, out EGaze kind)
    {
        aim = Vector3.zero;
        kind = EGaze.Body;

        if (stateManager == null ||
            stateManager.CurrentStateKey != NemesisStateManager.ENemesisState.Chasing)
            return false;

        // The one switch for both halves: at 0 the gaze stays welded to the body all chase long,
        // which is what it did before this existed.
        float lookAhead = data.LostSightLookAhead;
        if (lookAhead <= 0f) return false;

        // NOT DURING THE ESCAPE. NemesisEscapePursuit feeds the sensor the player's position by hand
        // and paces the run against their sprint; where the cone points decides when "it sees them"
        // flips, and with it how the pursuit steers. That sequence was tuned with the cone on the
        // body, so it keeps it there - the same call FieldOfView makes for its range.
        NemesisDecision decision = stateManager.Decision;
        if (decision != null && decision.ChaseFloor) return false;

        if (!fieldOfView.HasLastKnownPosition) return false;

        Vector3 eye = fieldOfView.ViewTransform.position;

        if (fieldOfView.HasVisualTarget)
        {
            kind = EGaze.OnPlayer;
            return ChaseGaze.TryGetDirectionTo(eye, fieldOfView.LastKnownPosition, out aim);
        }

        NemesisChasingState chasing = stateManager.ChasingState;
        NemesisPursuit pursuit = chasing != null ? chasing.Pursuit : null;
        if (pursuit == null || !pursuit.TryGetRecentSighting(out Vector3 lostPoint)) return false;

        Vector3 heading = fieldOfView.LastKnownVelocity;
        if (!ChaseGaze.TryGetAim(eye, lostPoint, heading, lookAhead, out aim)) return false;

        kind = EGaze.LostTrail;
        lostAt = lostPoint;
        lostAimPoint = ChaseGaze.AimPoint(lostPoint, heading, lookAhead);
        return true;
    }

    // -- The way back --------------------------------------------------------

    /// <summary>
    /// Nothing is steering the gaze this frame: give it back to the body.
    ///
    /// A sweep hands it straight back, as it always did - the wait is over and the body is setting
    /// off. A chase eases it back: see the class comment for why that is the hand-over to the search.
    /// </summary>
    private void ReleaseGaze(SO_NemesisData data)
    {
        if (gaze == EGaze.Body) return;

        if (gaze == EGaze.Scan)
        {
            HandBack();
            return;
        }

        Vector3 body = BodyForward();
        Vector3 turned = ChaseGaze.Turn(FlatGaze(), body, TurnStep(data));

        if (ChaseGaze.Angle(turned, body) <= AlignedDegrees)
        {
            HandBack();
            return;
        }

        fieldOfView.LookDirection = turned;
        gaze = EGaze.Returning;
    }

    private void HandBack()
    {
        gaze = EGaze.Body;
        if (fieldOfView != null) fieldOfView.ResetLookDirection();
    }

    /// <summary>Degrees the chase gaze may turn this frame.</summary>
    private static float TurnStep(SO_NemesisData data)
    {
        float speed = data != null ? data.GazeTurnSpeed : FallbackTurnSpeed;
        return Mathf.Max(1f, speed) * Time.deltaTime;
    }

    /// <summary>Where the eye is pointed now, along the floor. The body's forward while nothing is
    /// steering it: FieldOfView.LookDirection falls back to the view transform by itself.</summary>
    private Vector3 FlatGaze()
    {
        Vector3 look = fieldOfView.LookDirection;
        look.y = 0f;

        return look.sqrMagnitude > 0.0001f ? look.normalized : BodyForward();
    }

    /// <summary>The way the body faces, along the floor: the view transform hangs from the root the
    /// agent rotates, so its forward is the body's.</summary>
    private Vector3 BodyForward()
    {
        Transform eye = fieldOfView != null ? fieldOfView.ViewTransform : null;

        Vector3 forward = eye != null ? eye.forward : transform.forward;
        forward.y = 0f;

        return forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
    }
}
