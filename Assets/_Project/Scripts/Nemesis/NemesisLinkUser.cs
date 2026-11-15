using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Crosses by hand the NavMeshLinks the Nemesis steps onto: a jump, a link the NavMeshSurface generated
/// itself, or a low pass (<see cref="NemesisBreakLink"/>).
///
/// It turns <c>autoTraverseOffMeshLink</c> off in Awake, so it is responsible for ALL links. A plain one
/// is resolved with a simple interpolation, which also looks better than the instant hop of the
/// automatic mode, and a low pass whose barrier still stands is crossed the hard way first: stop, face
/// it, strike, and only then walk through.
///
/// There is no lift and no drop between floors any more: the Nemesis lives only in Zona 2, which is
/// joined by stairs (ramps in the NavMesh), so the freight-elevator ride, the drop and the Traversing
/// state they fed were taken out (the file used to be NemesisElevatorUser).
///
/// Written on UniTask rather than coroutines, per the project's async convention, and here that is also
/// the only safe option. A coroutine dies the moment its MonoBehaviour is disabled and Unity does not
/// dispose the iterator, so the cleanup in the finally block below would simply never run. UniTask runs
/// on the PlayerLoop, so the continuation still arrives and the stuck watchdog is always given back.
///
/// SETUP: goes on the Nemesis root, next to the NemesisStateManager. It needs no references.
/// </summary>
[RequireComponent(typeof(NemesisStateManager))]
public class NemesisLinkUser : MonoBehaviour
{
    // Nothing is serialised on this component. The speeds live in SO_NemesisMovement next to the ones
    // the NavMeshAgent uses. The fallbacks are only reached when the SO is missing, which
    // ValidateReferences already reports as an error: they exist so a broken prefab still crosses its
    // links instead of freezing halfway across one on a speed of 0.
    private const float FallbackLinkTraversalSpeed = 2.5f;
    private const float FallbackWalkSpeed = 1.5f;
    private const float FallbackTurnSpeed = 180f;

    private NemesisStateManager stateManager;
    private NavMeshAgent agent;
    private bool isCrossing;

    /// <summary>One per crossing, linked to the destroy token. Exists so a capture can end the crossing it
    /// interrupts: with only the destroy token, a Nemesis that grabbed the player mid-strike would go on
    /// with the pose until the barrier finished breaking.</summary>
    private CancellationTokenSource crossingCts;

    /// <summary>
    /// A link crossing is in flight (the walk across, or the strike at a low pass). Public because the
    /// rest of the system has to know that the body is being driven by hand and the agent may be
    /// standing on a link: the Director does not stage a peek over it.
    /// </summary>
    public bool IsCrossing => isCrossing;

    private SO_NemesisMovement Movement => stateManager != null ? stateManager.NemesisMovement : null;

    /// <summary>Metres per second crossing a plain link.</summary>
    private float LinkTraversalSpeed =>
        Movement != null ? Movement.LinkTraversalSpeed : FallbackLinkTraversalSpeed;

    /// <summary>Walking speed the body is handed back at after a strike.</summary>
    private float WalkSpeed => Movement != null ? Movement.BoardingSpeed : FallbackWalkSpeed;

    /// <summary>Degrees per second it turns while this component is moving it by hand.</summary>
    private float TurnSpeed => Movement != null ? Movement.TraversalTurnSpeed : FallbackTurnSpeed;

    private void Awake()
    {
        stateManager = GetComponent<NemesisStateManager>();
        agent = GetComponent<NavMeshAgent>();
        if (agent == null && stateManager != null) agent = stateManager.NavAgent;

        if (agent == null)
        {
            Debug.LogError($"[{nameof(NemesisLinkUser)}] '{name}' could not find the " +
                           "NavMeshAgent. Component disabled.", this);
            enabled = false;
            return;
        }

        // From here on this component crosses the links. Without it Unity resolves them on its own and
        // there is no window to step in and make the Nemesis strike a barrier: by the time
        // isOnOffMeshLink turns true the automatic traversal has already started.
        agent.autoTraverseOffMeshLink = false;
    }

    private void Update()
    {
        if (isCrossing) return;
        if (stateManager == null || !stateManager.IsActive) return;

        // Holding the player. A crossing cancelled by the capture leaves the agent standing on the
        // link, and without this the next frame would start the same crossing over.
        if (IsCapturing) return;
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        if (!agent.isOnOffMeshLink) return;

        OffMeshLinkData data = agent.currentOffMeshLinkData;
        if (!data.valid) return;

        // owner is typed as UnityEngine.Object, but NavMeshLink registers itself through
        // NavMesh.SetLinkOwner(instance, this), so in practice it is always a Component. The cast
        // covers a link created some other way, which simply has no barrier.
        Component linkOwner = data.owner as Component;
        NemesisBreakLink barrier = linkOwner != null ? linkOwner.GetComponent<NemesisBreakLink>() : null;

        // Safe to dispose: this line is only reached with no crossing in flight (isCrossing).
        crossingCts?.Dispose();
        crossingCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

        CrossAsync(data, barrier, crossingCts.Token).Forget();
    }

    /// <summary>
    /// The FSM is holding the player right now. It can happen in the middle of a crossing, and the two
    /// things this component does on its way out — reset the gait, give the watchdog back — are both
    /// wrong over a grab in progress (the gait is Catch's).
    /// </summary>
    private bool IsCapturing =>
        stateManager != null &&
        stateManager.CurrentStateKey == NemesisStateManager.ENemesisState.Catch;

    /// <summary>
    /// Freezes the agent where it stands and drops it to the idle gait, or hands it back walking.
    ///
    /// <c>isStopped</c> and not <c>ResetPath</c>: on a link Unity completes the link before it clears
    /// the path, which would teleport the body to the far end. The gait goes through the state manager
    /// rather than the Animator, because gait and speed are one decision there.
    /// </summary>
    private void HoldStill(bool hold)
    {
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.isStopped = hold;

            // Zeroed as well as stopped: isStopped halts the steering but leaves whatever velocity the
            // agent had built up, and a body that keeps drifting for a few tenths of a second after
            // being told to wait ends up inside the barrier.
            if (hold) agent.velocity = Vector3.zero;
        }

        // Not over a capture: Catch set the grab gait on entry and owns it.
        if (stateManager == null || IsCapturing) return;

        if (hold) stateManager.SetGait(NemesisStateManager.EGait.Idle, 0f);
        else      stateManager.SetGait(NemesisStateManager.EGait.Walking, WalkSpeed);
    }

    private void OnEnable()
    {
        PlayerEvents.OnPlayerCaptured += HandlePlayerCaptured;
        CheckpointManager.OnRespawned += HandlePlayerRespawned;
    }

    private void OnDisable()
    {
        PlayerEvents.OnPlayerCaptured -= HandlePlayerCaptured;
        CheckpointManager.OnRespawned -= HandlePlayerRespawned;
    }

    /// <summary>A capture ends a crossing that is still at the strike: the finally in
    /// <see cref="CrossAsync"/> then releases the watchdog on the next frame.</summary>
    private void HandlePlayerCaptured(PlayerStateManager player) => CancelCrossing();

    /// <summary>The player is back at a checkpoint: whatever crossing survived the capture has nothing
    /// left to do.</summary>
    private void HandlePlayerRespawned(Checkpoint checkpoint) => CancelCrossing();

    private void CancelCrossing()
    {
        if (!isCrossing || crossingCts == null || crossingCts.IsCancellationRequested) return;

        crossingCts.Cancel();
    }

    /// <summary>
    /// Crossing a link: interpolate end to end at constant speed and report completion. This replaces
    /// the automatic traversal switched off in Awake.
    ///
    /// A link across a low pass (<see cref="NemesisBreakLink"/>) whose barrier still stands is crossed
    /// the hard way first: stop, face it, strike, and only then walk through. That pause is the whole
    /// point of the barrier: it is what the player gained by going under it.
    /// </summary>
    private async UniTaskVoid CrossAsync(OffMeshLinkData data, NemesisBreakLink barrier, CancellationToken token)
    {
        isCrossing = true;
        stateManager.PushStuckSuppression();

        try
        {
            Vector3 start = transform.position;
            Vector3 end = data.endPos + Vector3.up * agent.baseOffset;

            if (barrier != null && !barrier.IsBroken)
            {
                HoldStill(true);
                await TurnToFaceAsync(barrier.BarrierPosition, token);

                stateManager.PlayStrike();
                await WaitSecondsAsync(barrier.StrikeSeconds, token);
                barrier.Break();
                await WaitSecondsAsync(barrier.RecoverSeconds, token);

                stateManager.EndStrike();
                HoldStill(false);
            }

            await MoveTransformToAsync(start, end, LinkTraversalSpeed, token);

            if (agent.isActiveAndEnabled && agent.isOnOffMeshLink) agent.CompleteOffMeshLink();
            if (barrier != null) barrier.NotifyCrossed();
        }
        finally
        {
            // A strike cut short (a capture, a respawn) must not leave it frozen in the pose.
            if (stateManager != null) stateManager.EndStrike();
            if (stateManager != null) stateManager.PopStuckSuppression();
            isCrossing = false;
        }
    }

    /// <summary>Waits a number of seconds of game time: a paused game pauses the wait.</summary>
    private static async UniTask WaitSecondsAsync(float seconds, CancellationToken token)
    {
        float waited = 0f;

        while (waited < seconds)
        {
            waited += Time.deltaTime;
            await UniTask.Yield(token);
        }
    }

    /// <summary>
    /// Interpolates the Transform at constant speed, turned into the direction of travel as it goes. The
    /// agent is off the mesh's control for the whole crossing, so this is the only thing writing
    /// rotation.
    /// </summary>
    private async UniTask MoveTransformToAsync(Vector3 from, Vector3 to, float speed, CancellationToken token)
    {
        float distance = Vector3.Distance(from, to);
        if (distance < 0.01f) return;

        Quaternion targetRotation = FlatLookRotation(to - from);

        float duration = distance / Mathf.Max(0.1f, speed);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation,
                                                          TurnSpeed * Time.deltaTime);
            await UniTask.Yield(token);
        }

        transform.position = to;
    }

    /// <summary>Turns on the spot to face a point, and does not return until it is facing it.</summary>
    private async UniTask TurnToFaceAsync(Vector3 point, CancellationToken token)
    {
        Quaternion target = FlatLookRotation(point - transform.position);

        while (Quaternion.Angle(transform.rotation, target) > 1f)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target,
                                                          TurnSpeed * Time.deltaTime);
            await UniTask.Yield(token);
        }

        transform.rotation = target;
    }

    /// <summary>
    /// Look rotation with the vertical component discarded, so a target above or below does not tip the
    /// Nemesis onto its face. Falls back to the current rotation for a purely vertical direction:
    /// Quaternion.LookRotation of a zero vector is undefined and Unity warns about it.
    /// </summary>
    private Quaternion FlatLookRotation(Vector3 direction)
    {
        direction.y = 0f;

        return direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction)
            : transform.rotation;
    }
}
