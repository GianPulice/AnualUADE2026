using System;
using UnityEngine;

/// <summary>
/// Static bus of the wake-up cinematic (ARC_01a), between the HUD that runs it
/// (<see cref="WakeUpCinematicView"/>, in LevelUI) and the player's camera rig
/// (<see cref="WakeUpCameraPan"/>, in the Player prefab). They live in different scenes, so neither
/// can hold a reference to the other.
///
/// Sequence: LockCamera (screen black, camera held at the start of the pan, look input off) →
/// StartPan (eyes open, the camera travels back to its default framing) → Finish (ARC_01a ended:
/// camera at its default and look input back, on the same frame the player gets control back).
/// </summary>
public static class WakeUpCinematicEvents
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsCameraLocked = false;
        OnPanStarted = null;
        OnFinished = null;
    }

    /// <summary>
    /// The cinematic owns the camera: no look input, the rig is placed by <see cref="WakeUpCameraPan"/>.
    /// A flag and not only an event, so a rig that spawns after the lock still picks it up.
    /// </summary>
    public static bool IsCameraLocked { get; private set; }

    /// <summary>The pan begins. The float is the seconds it has to reach the default framing.</summary>
    public static event Action<float> OnPanStarted;

    /// <summary>The cinematic is over: the camera must be at its default and look input comes back.</summary>
    public static event Action OnFinished;

    public static void LockCamera() => IsCameraLocked = true;

    public static void StartPan(float seconds) => OnPanStarted?.Invoke(Mathf.Max(0f, seconds));

    public static void Finish()
    {
        if (!IsCameraLocked) return;
        IsCameraLocked = false;
        OnFinished?.Invoke();
    }
}
