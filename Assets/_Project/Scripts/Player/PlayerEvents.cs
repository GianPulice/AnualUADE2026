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
    private static void ResetStatics() => OnPlayerCaptured = null;

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
}
