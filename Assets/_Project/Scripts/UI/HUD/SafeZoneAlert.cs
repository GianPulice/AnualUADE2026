using UnityEngine;

/// <summary>
/// The safe-zone alert: "|| Safe Zone ||" at the top of the screen every time the player walks into
/// the Hub — any volume <see cref="NemesisSafeZones"/> reads, the same answer the Director and the
/// pressure regulator's glow use. It goes through <see cref="HUDMessageEvents.ShowAlert"/>, so it
/// queues with the Architect's alerts in <see cref="HUDAlertView"/> instead of covering them.
///
/// What counts as walking in:
///   - In at the volume's edge (<see cref="NemesisSafeZones.Contains"/>), out only
///     <see cref="rearmDistance"/> past it: hovering in the doorway does not repeat the alert.
///   - The position is only read while the player has control: not under the loading screen, a
///     cinematic, the pause or a menu, and not while disabled, lying down or getting up (the wake-up,
///     a capture). Arriving behind one of those — a respawn inside the Hub — is announced once
///     control is back, never under a black screen.
///   - A level that starts with the player already inside announces it as soon as they can move.
///
/// The Hub used to be announced once, as the alert of the Architect's first line there (ARC_CTX_01).
/// That line no longer carries it — the first visit would show it twice — so every visit, the first
/// one included, is announced from here.
///
/// One per level with a safe zone: in WIRED_Zona1_Blockout it is "Safe Zone Alert", under
/// ---- SISTEMA ---- next to the Safe Area volumes. Untick it to silence the alert.
/// </summary>
public class SafeZoneAlert : MonoBehaviour
{
    [Tooltip("Text of the alert. Same look as the Architect's alerts; rich text works.")]
    [SerializeField] private string message = "|| Safe Zone ||";

    [Tooltip("Seconds the alert stays up before it retracts.")]
    [SerializeField, Min(0.5f)] private float seconds = 3f;

    [Tooltip("Metres the player must get out of the safe zone before walking back in announces it " +
             "again. Keeps the alert from repeating while they hover in the doorway.")]
    [SerializeField, Min(0f)] private float rearmDistance = 1.5f;

    // Same cadence as the other readers of the volumes (SocketEmissionShift, NemesisTension).
    private const float CheckInterval = 0.2f;

    // True while the next time inside will be announced. Starts true: the first time counts.
    private bool armed = true;
    private float nextCheck;

    private void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + CheckInterval;

        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null || !HasControl(player)) return;

        Vector3 position = player.transform.position;
        if (NemesisSafeZones.Contains(position))
        {
            if (!armed) return;

            armed = false;
            HUDMessageEvents.ShowAlert(message, seconds);
        }
        else if (!armed && NemesisSafeZones.DistanceOnSameLevel(position) >= rearmDistance)
        {
            armed = true;
        }
    }

    private static bool HasControl(PlayerStateManager player) =>
        !LoadingScreen.IsLoading && !CinematicState.IsPlaying && !PauseManager.IsGameplayInputBlocked &&
        !player.IsImmobilized && !player.IsRecoveringFromCapture;
}
