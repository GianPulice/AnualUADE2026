using UnityEngine;

/// <summary>
/// Sweeps the Nemesis's GAZE from side to side while it stands waiting at a patrol waypoint.
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
/// WHY IT PAIRS WITH THE RANDOMISED WAIT
///
/// On its own a scan makes the pause look busy. Together with the per-waypoint wait roll it makes
/// the pause genuinely dangerous: the player can no longer count the beats, AND the direction the
/// monster happens to be facing when the wait ends is no longer the direction it arrived from.
///
/// WHERE IT APPLIES
///
/// The two moments the Nemesis is deliberately standing still: waiting out a patrol waypoint, and
/// pausing at a search point. Every other state is already pointed at something it cares about -
/// the belief, the noise, the player - and swinging the cone off that target would make it worse
/// at the one job it is doing.
///
/// The search case is the one that changes how the game plays. A search that walks point to point
/// without stopping is unreadable from a hiding place; a search that stops and LOOKS tells the
/// player whether it is closing in or has written the area off, which is what makes staying put a
/// gamble instead of a coin flip.
/// </summary>
[RequireComponent(typeof(NemesisStateManager))]
public class NemesisLookAround : MonoBehaviour
{
    [SerializeField] private NemesisStateManager stateManager;
    [SerializeField] private FieldOfView fieldOfView;

    /// <summary>Degrees travelled so far in the ping-pong. Offset by the half-angle when a scan
    /// starts so the sweep begins looking STRAIGHT AHEAD and works outwards, rather than snapping
    /// to one extreme on the first frame.</summary>
    private float scanPhase;

    private bool scanning;

    /// <summary>The direction the sweep is centred on, captured once when the scan starts. Taken
    /// once rather than read per frame because reading it live would feed the eye's own rotation
    /// back into itself.</summary>
    private Vector3 scanCentre;

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
        StopScanning();
    }

    private void Update()
    {
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;
        if (fieldOfView == null) return;

        if (!ShouldScan())
        {
            StopScanning();
            return;
        }

        SO_NemesisData data = stateManager != null ? stateManager.NemesisData : null;
        if (data == null) return;

        float halfAngle = data.ScanHalfAngle;
        if (halfAngle <= 0.01f)
        {
            StopScanning();
            return;
        }

        if (!scanning) BeginScanning(halfAngle);

        scanPhase += data.ScanSpeed * Time.deltaTime;

        // PingPong over the full width, re-centred: 0 -> +half -> -half -> +half. Starting the
        // phase at halfAngle (see BeginScanning) is what makes the first frame read as 0 degrees
        // off centre instead of hard over to one side.
        float offset = Mathf.PingPong(scanPhase, halfAngle * 2f) - halfAngle;

        fieldOfView.LookDirection = Quaternion.AngleAxis(offset, Vector3.up) * scanCentre;
    }

    /// <summary>
    /// Standing still on patrol, and nothing else.
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

            // Everything else is already pointed at something it cares about, and swinging the
            // cone off that target would make the Nemesis worse at the one job it is doing.
            default:
                return false;
        }
    }

    private void BeginScanning(float halfAngle)
    {
        scanning = true;
        scanPhase = halfAngle;

        Transform eye = fieldOfView.ViewTransform;

        // Flattened: the sweep turns about the world's up axis, so a Nemesis standing on a ramp
        // should still look left and right along the floor rather than tracing a tilted arc.
        Vector3 forward = eye != null ? eye.forward : transform.forward;
        forward.y = 0f;

        scanCentre = forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
    }

    private void StopScanning()
    {
        if (!scanning) return;

        scanning = false;
        if (fieldOfView != null) fieldOfView.ResetLookDirection();
    }
}
