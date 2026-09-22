using System;
using UnityEngine;

/// <summary>
/// Everything that happens to a hiding spot, broadcast so that whoever counts it and whoever
/// reacts to it never have to know about each other.
///
/// This is the seam the anti-cheese work hangs off (plan §3.1, §5.3). The Nemesis's
/// "I saw you climb in" rule listens to <see cref="OnEntered"/>; the habit tracker counts the same
/// event; neither of them is allowed to call the spot, and the spot is not allowed to call them.
/// Without the seam, HidingSpot ends up holding a reference to the monster, which is exactly how
/// the level stops being authorable.
/// </summary>
public static class HidingEvents
{
    /// <summary>
    /// Static event state survives leaving Play mode when domain reload is disabled, leaving
    /// listeners from the previous run hooked to destroyed objects — and the first one to throw
    /// stops the rest of the invocation list. Same guard NemesisEvents carries, for the same
    /// reason.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnEntered = null;
        OnExited = null;
        OnSpotBurned = null;
    }

    /// <summary>
    /// The player is now inside the spot: raised on the frame hiding actually starts, which is the
    /// END of the entry animation and not the E press. That matters — the window the entry
    /// animation leaves open is precisely what "the Nemesis saw you climb in" reads, so a listener
    /// asking <c>FieldOfView.TimeSinceLastSighting</c> here gets the honest answer.
    /// </summary>
    public static event Action<HidingSpot> OnEntered;

    /// <summary>
    /// The player is no longer inside the spot. Raised on every way out, not just the deliberate
    /// one: a capture, a checkpoint respawn and a scene unload all come through here, so a
    /// listener never has to guess whether its counterpart to <see cref="OnEntered"/> is coming.
    /// </summary>
    public static event Action<HidingSpot> OnExited;

    /// <summary>
    /// The spot has been destroyed for good — the Nemesis tore the door off (plan §3.6). Raised
    /// once per spot per run. Nothing raises it yet; <see cref="HidingSpot.Burn"/> is the seam
    /// phase 6 hangs the behaviour off.
    /// </summary>
    public static event Action<HidingSpot> OnSpotBurned;

    public static void Entered(HidingSpot spot)   => OnEntered?.Invoke(spot);
    public static void Exited(HidingSpot spot)    => OnExited?.Invoke(spot);
    public static void SpotBurned(HidingSpot spot) => OnSpotBurned?.Invoke(spot);
}
