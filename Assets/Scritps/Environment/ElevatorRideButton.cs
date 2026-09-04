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

    public override string GetInteractText() =>
        isConfigured ? "Operate freight elevator" : string.Empty;

    /// <summary>
    /// Carries the "why not" for the two refusals, which is the whole reason this button reads as
    /// a button rather than as scenery: pressed from the landing it has to say to step aboard, and
    /// pressed on a claimed cabin it has to say the lift is busy.
    /// </summary>
    public override string GetInfoText()
    {
        if (!isConfigured)               return string.Empty;
        if (!platform.IsPlayerAboard)    return "Step onto the platform first.";
        return platform.IsAvailable ? string.Empty : "Freight elevator in use.";
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

    private void PlaySound(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !AudioManager.Exists) return;
        AudioManager.Instance.PlaySFX(id, transform.position);
    }
}
