using System;
using UnityEngine;

/// <summary>
/// What the security cameras see, broadcast so that whatever a sighting should trigger - an
/// alarm, a call to the Nemesis, a HUD warning - can hang off it without any camera knowing
/// about it, and without that system holding a list of cameras.
///
/// Nothing listens yet. This is the seam, not the consequence.
/// </summary>
public static class SecurityCameraEvents
{
    /// <summary>
    /// Static event state survives leaving Play mode when domain reload is disabled. Same guard
    /// NemesisEvents and HidingEvents carry, for the same reason.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnPlayerSpotted = null;
        OnPlayerLost = null;
    }

    /// <summary>A camera has just started following the player. Raised once per sighting, on the
    /// transition, not every frame the camera keeps them in view.</summary>
    public static event Action<SecurityCamera> OnPlayerSpotted;

    /// <summary>A camera has given up on the player and gone back to its sweep - after its
    /// lose-sight delay, not the instant the player stepped out of view. Also raised when a camera
    /// that was following the player is disabled, so a listener never waits for a "lost" that is
    /// not coming.</summary>
    public static event Action<SecurityCamera> OnPlayerLost;

    public static void PlayerSpotted(SecurityCamera camera) => OnPlayerSpotted?.Invoke(camera);
    public static void PlayerLost(SecurityCamera camera)    => OnPlayerLost?.Invoke(camera);
}
