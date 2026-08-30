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

    public static void ChaseStarted()                    => OnChaseStarted?.Invoke();
    public static void ChaseEnded()                      => OnChaseEnded?.Invoke();
    public static void ProximityChanged(float t)         => OnProximityChanged?.Invoke(t);
    public static void StateChanged(NemesisStateManager.ENemesisState state) => OnStateChanged?.Invoke(state);
}
