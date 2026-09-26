using UnityEngine;

/// <summary>
/// The button INSIDE the freight elevator cabin: it is what actually sends the lift to the other
/// landing. One per cabin.
///
/// It exists because <see cref="ElevatorCallPanel"/> only ever summoned the cabin TO a landing —
/// there was no way to command a trip while standing on it. Riding used to be started by the
/// boarding trigger itself, which meant walking into the cabin sent it away with you; see
/// MovingPlatform.departOnPlayerEnter for why that was split apart.
///
/// There is no up/down pair on purpose. The shaft has exactly two landings, so "somewhere else"
/// names only one destination, and MovingPlatform already tracks which one in its own goingUp
/// (flipped on every step-off). A second button would be a second source of truth for a direction
/// that is not a choice.
///
/// SETUP:
///   1. Empty GameObject parented under the cabin (so it rides along), with a Collider on the
///      Interactable layer — the InteractionManager's SphereCast needs something to hit.
///   2. Leave 'platform' empty to have it resolved from the parents.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ElevatorRideButton : BaseRangeInteractable
{
    [Header("Shaft")]
    [Tooltip("The cabin this button drives. Left empty it is looked up in the parents, so a " +
             "button parented under the cabin needs no wiring at all.")]
    [SerializeField] private MovingPlatform platform;

    [Header("Audio")]
    [Tooltip("Played when the trip is accepted. Leave empty for silence.")]
    [SoundId]
    [SerializeField] private string rideAcceptedSoundId = string.Empty;

    [Tooltip("Played when the press is refused — moving, or claimed by the Nemesis.")]
    [SoundId]
    [SerializeField] private string rideRefusedSoundId = string.Empty;

    private bool isConfigured;

    /// <summary>
    /// What the prompt was last drawn from — player aboard, cabin free — or null while the crosshair
    /// is on something else. Those two flags are everything CanInteract and both texts read, so a
    /// change in either is a change in what the prompt has to say. See <see cref="LateUpdate"/>.
    /// </summary>
    private (bool aboard, bool available)? promptState;

    protected override void Awake()
    {
        base.Awake();

        if (platform == null) platform = GetComponentInParent<MovingPlatform>();

        isConfigured = platform != null;
        if (!isConfigured)
        {
            Debug.LogError($"[{nameof(ElevatorRideButton)}] '{name}' has no {nameof(MovingPlatform)} " +
                           "assigned and none in its parents. The button is inert.", this);
        }
    }

    // ── IInteractable ───────────────────────────────────────────────────────

    /// <summary>
    /// Only pressable while actually aboard a parked cabin. Requiring the player to be aboard is
    /// what stops this doubling as a second call panel reachable from the landing, which would
    /// send the empty cabin away from the floor the player is standing on.
    /// </summary>
    protected override bool CanInteractInCloseRange() =>
        isConfigured && platform.IsPlayerAboard && platform.IsAvailable;

    public override string GetPromptText() =>
        isConfigured ? "Operate forklift" : string.Empty;

    /// <summary>
    /// Carries the "why not" for the two refusals, which is the whole reason this button reads as
    /// a button rather than as scenery: pressed from the landing it has to say to step aboard, and
    /// pressed on a claimed cabin it has to say the lift is busy.
    /// </summary>
    public override string GetInfoText()
    {
        if (!isConfigured)               return string.Empty;
        if (!platform.IsPlayerAboard)    return "Step onto the platform first.";
        return platform.IsAvailable ? string.Empty : "Forklift in use.";
    }

    /// <summary>Repeatable: a lift you can only ride once is a lift that strands you upstairs.</summary>
    public override bool IsRepeatable() => true;

    protected override void OnInteract()
    {
        if (!isConfigured) return;

        // RequestRide can still come back false: CanInteract was evaluated a frame earlier by the
        // InteractionManager, and the Nemesis can claim the platform in between. Treated as a
        // refusal rather than ignored, so the player gets told instead of pressing a dead button.
        if (platform.RequestRide()) PlaySound(rideAcceptedSoundId);
        else                        PlaySound(rideRefusedSoundId);
    }

    /// <summary>
    /// Where the refusal actually lands. CanInteract is false while the cabin is moving, claimed, or
    /// the player is not aboard, so the InteractionManager never calls OnInteract for those presses
    /// and the refusal branch there only covers the one-frame race with the Nemesis.
    /// </summary>
    public override void OnInteractAttemptBlocked()
    {
        if (!isConfigured) return;
        PlaySound(rideRefusedSoundId);
    }

    private void PlaySound(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !AudioManager.Exists) return;
        AudioManager.Instance.PlaySFX(id, transform.position);
    }

    // ── Prompt ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-draws the prompt when the button's answer changes under the crosshair — the same watch as
    /// PushableBox's range check and ElevatorCallPanel's. The prompt only re-reads a target when the
    /// target itself changes, and a rider keeps looking at this button while its answer changes: the
    /// press makes it refuse for the whole trip, and stepping off turns that into "step onto the
    /// platform first". Without it the window kept offering "Operate forklift" with its [E] for the
    /// entire ride.
    ///
    /// LateUpdate so it sees this frame's target, and a press InteractionManager.Update has just
    /// handled. Only while this button is the target: the refresh is global and redraws whatever
    /// the crosshair is on.
    /// </summary>
    private void LateUpdate()
    {
        if (!isConfigured) return;

        if (!InteractionManager.Exists ||
            !ReferenceEquals(InteractionManager.Instance.CurrentInteractable, this))
        {
            promptState = null;
            return;
        }

        (bool aboard, bool available) state = (platform.IsPlayerAboard, platform.IsAvailable);
        if (promptState == state) return;

        promptState = state;
        InteractionEvents.RequestPromptRefresh();
    }
}
