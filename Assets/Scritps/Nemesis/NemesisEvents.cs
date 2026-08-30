using System;
using UnityEngine;

public static class NemesisEvents
{
    /// <summary>
    /// Static event state survives leaving Play mode when domain reload is disabled, which would
    /// leave listeners from the previous run hooked to destroyed CanvasGroups. The first one to
    /// throw stops the rest of the invocation list, so a stale vignette from the last session is
    /// enough to keep the live one from ever being updated.
    ///
    /// Same guard PlayerRegistry, PuzzleStateManager and CheckpointManager already carry — this
    /// class was the only static event hub in the project without it.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnChaseStarted = null;
        OnChaseEnded = null;
        OnProximityChanged = null;
        OnStateChanged = null;
        OnCaptureResolved = null;
    }

    // A single global channel, which is correct ONLY because the design has exactly one Nemesis:
    // two of them would both drive the one HUD vignette, and whichever stopped chasing last would
    // switch it off while the other was still hunting. The other two places that assume this are
    // NemesisNav.AreaMask and NemesisElevatorLink.Active.

    public static event Action OnChaseStarted;
    public static event Action OnChaseEnded;

    // Normalized value [0,1]: 0 = far away / out of range, 1 = minimum distance.
    // The Nemesis is responsible for computing and raising this event every frame.
    public static event Action<float> OnProximityChanged;

    /// <summary>
    /// The Nemesis FSM entered a different state. Raised once per transition, with the state it
    /// moved into. NemesisAudio uses it to crossfade its per-state loops.
    /// </summary>
    public static event Action<NemesisStateManager.ENemesisState> OnStateChanged;

    /// <summary>
    /// The Nemesis has finished repositioning after a capture and is about to go back to
    /// Patrolling. Raised once, right after <see cref="NemesisStateManager.RepositionAfterCapture"/>
    /// runs — i.e. the WARP has already happened by the time anyone hears this.
    ///
    /// This is the "it is now safe to show the Nemesis again" signal. <c>CheckpointManager.
    /// OnRespawned</c> fires much earlier — the instant the PLAYER lands at the checkpoint — and
    /// says nothing about where the Nemesis is. Revealing the screen on that event alone is what
    /// let the player watch the Nemesis pop from the capture spot to a spawn point in plain
    /// sight, sometimes several seconds after the screen had already gone clear again. Nothing
    /// else in the project should reveal a "the capture is fully over" cover before this fires.
    /// </summary>
    public static event Action OnCaptureResolved;

    public static void ChaseStarted()                    => OnChaseStarted?.Invoke();
    public static void ChaseEnded()                      => OnChaseEnded?.Invoke();
    public static void ProximityChanged(float t)         => OnProximityChanged?.Invoke(t);
    public static void StateChanged(NemesisStateManager.ENemesisState state) => OnStateChanged?.Invoke(state);
    public static void CaptureResolved()                 => OnCaptureResolved?.Invoke();
}
