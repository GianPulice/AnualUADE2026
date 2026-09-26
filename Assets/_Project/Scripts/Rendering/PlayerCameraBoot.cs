using UnityEngine;

/// <summary>
/// What the player's camera is doing when it is not simply on: off, booting, or recording with its
/// lens still calibrating. Two things put it through that — the wake-up cinematic
/// (<see cref="WakeUpCinematicView"/>, in LevelUI) at the start of the level, and the reboot after a
/// capture (<see cref="PlayerCameraFeed"/> itself) — and <see cref="PlayerCameraFeed"/> draws it.
///
/// Flags and not events: the feed reads them every frame, whichever scene loaded first.
/// </summary>
public static class PlayerCameraBoot
{
    public enum Phase
    {
        None,       // No boot running: the camera shows its normal picture.
        Off,        // Powered down, or the signal lost: black.
        Booting,    // The picture is in, uncalibrated, under the boot screen (BootProgress).
        Live,       // Booted and recording; the lens goes on calibrating as the player gets up.
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Current = Phase.None;
        IsReboot = false;
        BootProgress = 0f;
        PictureSince = 0f;
        LiveSince = 0f;
        WakeProgress = 0f;
        SignalLostAt = float.NegativeInfinity;
        CutAt = float.NegativeInfinity;
        CalibratedAt = float.NegativeInfinity;
    }

    public static Phase Current { get; private set; }

    /// <summary>This boot is the reboot after a capture (the boot screen says so).</summary>
    public static bool IsReboot { get; private set; }

    /// <summary>0..1 through the boot, in plain time. The feed shapes what the bar shows.</summary>
    public static float BootProgress { get; private set; }

    /// <summary><see cref="Time.unscaledTime"/> the picture came in: the start of the boot.</summary>
    public static float PictureSince { get; private set; }

    /// <summary><see cref="Time.unscaledTime"/> the boot ended and the camera started recording.</summary>
    public static float LiveSince { get; private set; }

    /// <summary>
    /// 0..1 from the moment the picture came in to the moment the player is on their feet, in plain
    /// stand-up progress. The feed shapes how the lens calibrates over it.
    /// </summary>
    public static float WakeProgress { get; private set; }

    /// <summary><see cref="Time.unscaledTime"/> the signal was lost (a capture): the feed goes to
    /// black through static.</summary>
    public static float SignalLostAt { get; private set; } = float.NegativeInfinity;

    /// <summary><see cref="Time.unscaledTime"/> a boot was cut short (a skip): the feed covers the
    /// jump to the normal picture with a burst of static.</summary>
    public static float CutAt { get; private set; } = float.NegativeInfinity;

    /// <summary><see cref="Time.unscaledTime"/> a boot ended with the lens calibrated: the feed says
    /// so on screen for a moment.</summary>
    public static float CalibratedAt { get; private set; } = float.NegativeInfinity;

    /// <summary>The camera goes off. <paramref name="signalLost"/>: it drops out through static
    /// instead of simply being dark.</summary>
    public static void SetOff(bool signalLost = false)
    {
        if (signalLost && Current != Phase.Off) SignalLostAt = Time.unscaledTime;
        Current = Phase.Off;
        BootProgress = 0f;
    }

    /// <summary>The boot screen, <paramref name="progress"/> through. The first call brings the
    /// picture in.</summary>
    public static void SetBooting(float progress, bool reboot = false)
    {
        if (Current != Phase.Booting && Current != Phase.Live)
        {
            PictureSince = Time.unscaledTime;
            WakeProgress = 0f;
        }

        Current = Phase.Booting;
        IsReboot = reboot;
        BootProgress = Mathf.Clamp01(progress);
    }

    /// <summary>Booted: the camera starts recording. Brings the picture in too if the boot never
    /// ran. Repeated calls keep the first moment.</summary>
    public static void SetLive()
    {
        if (Current == Phase.Live) return;
        if (Current != Phase.Booting)
        {
            PictureSince = Time.unscaledTime;
            WakeProgress = 0f;
        }

        Current = Phase.Live;
        BootProgress = 1f;
        LiveSince = Time.unscaledTime;
    }

    public static void SetWakeProgress(float progress) => WakeProgress = Mathf.Clamp01(progress);

    /// <summary>
    /// Straight back to None with no static and no "calibrated" line: a new level's camera, so a
    /// boot left half-way by the last one (a capture that ended the run) cannot leave it black.
    /// </summary>
    public static void Clear()
    {
        Current = Phase.None;
        IsReboot = false;
        WakeProgress = 1f;
    }

    /// <summary>
    /// Back to the normal picture. <paramref name="cut"/>: the boot did not play out (a skip, a
    /// torn-down cinematic), so the lens jumps instead of having calibrated. A boot that played out
    /// — ending while booting (the wake-up's runs the whole cinematic) or recording — says so.
    /// </summary>
    public static void End(bool cut)
    {
        if (Current == Phase.None) return;

        if (cut) CutAt = Time.unscaledTime;
        else if (Current == Phase.Live || Current == Phase.Booting) CalibratedAt = Time.unscaledTime;

        Current = Phase.None;
        IsReboot = false;
        WakeProgress = 1f;
    }
}
