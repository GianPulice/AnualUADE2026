using System;
using UnityEngine;

/// <summary>
/// Player-originated events other systems react to without needing to be called directly.
/// Mirrors the pattern already used by NemesisEvents/InventoryEvents.
/// </summary>
public static class PlayerEvents
{
    /// <summary>
    /// Static event state survives leaving Play mode when domain reload is disabled, leaving
    /// listeners from the previous run hooked to destroyed objects — and the first one to throw
    /// stops the rest of the invocation list. The same guard NemesisEvents and HidingEvents carry.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnPlayerCaptured = null;
        CaptureShotSeconds = 0f;
    }

    /// <summary>
    /// The player was captured by the Nemesis. Raised by PlayerStateManager.OnCaptured() and
    /// nothing else.
    ///
    /// This is the seam the Nemesis spec requires: NemesisCatchState only calls OnCaptured() and
    /// stops there — it never reaches into the save system or the UI directly. CheckpointManager
    /// listens here instead of being called, which is what keeps that boundary real instead of
    /// just documented.
    /// </summary>
    public static event Action<PlayerStateManager> OnPlayerCaptured;

    public static void PlayerCaptured(PlayerStateManager player) => OnPlayerCaptured?.Invoke(player);

    /// <summary>
    /// Seconds a capture stays on screen before anything covers it or takes the player away: the
    /// length of the grab's shot. Everything that times itself off <see cref="OnPlayerCaptured"/>
    /// (the black cover, the respawn, the escape's defeat) waits this long first, on top of its
    /// own delay, so the gaps between them stay as they were tuned.
    ///
    /// 0 with no shot in the scene, which is the capture as it always was: covered at once.
    ///
    /// A standing value and not something written at the grab: <see cref="CaptureGrabCamera"/>
    /// holds it while it exists, so the listeners read the same number whatever order the event
    /// reaches them in.
    /// </summary>
    public static float CaptureShotSeconds { get; set; }
}
