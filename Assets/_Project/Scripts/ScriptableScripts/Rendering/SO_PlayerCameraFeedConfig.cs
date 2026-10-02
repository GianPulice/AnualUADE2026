using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Everything tunable about the player's camera feed (<see cref="PlayerCameraFeed"/>): what it
/// burns into the picture, how it boots at the start of the level, how the picture comes in, and
/// the fisheye lens it wakes up with and calibrates while the player gets up.
///
/// The look of the picture itself (grain, barrel, vignette, overlay colour) is on
/// <c>PlayerCamera.mat</c>. Both can be tuned during Play: the feed reads this asset every frame,
/// and changes to an asset outlive Play mode.
/// </summary>
[CreateAssetMenu(fileName = "SO_PlayerCameraFeed", menuName = "Scriptable Objects/Rendering/Player Camera Feed")]
public class SO_PlayerCameraFeedConfig : ScriptableObject
{
    [Header("On screen")]
    [Tooltip("Top left, outside every CameraAreaZone (inside one, the zone's name shows instead). " +
             "Empty = nothing. Letters, digits and : - / . # > % _ ! + [ ] = | (accents are dropped; " +
             "anything else shows as a space). 32 at most.")]
    [SerializeField] private string label = "";

    [Tooltip("Keep the recording overlay (label, recording dot, readout, brackets) on screen through " +
             "gameplay. Off: it only shows during the wake-up and the game plays with the plain picture.")]
    [SerializeField] private bool overlayDuringGameplay = true;

    [Header("Typewriter (label and readout changes)")]
    [Tooltip("Characters per second a new text types in at.")]
    [SerializeField, Min(1f)] private float typeCharsPerSecond = 30f;

    [Tooltip("Characters per second the old text erases at, before the new one types in.")]
    [SerializeField, Min(1f)] private float eraseCharsPerSecond = 60f;

    [Header("Readout (bottom left; nothing until a module starts)")]
    [Tooltip("When a module starts, the BOMB banner sweeps across the middle of the screen; then the " +
             "readout types in there with the time at 00:00, the time counts up to the module's " +
             "(its ModuleData), and the readout travels to the bottom left, where the countdown " +
             "goes on. Once per module. With none running, it stays on the last one that did.\n" +
             "{0} = module (M1), {1} = status, {2} = time (MM:SS).")]
    [SerializeField] private string moduleReadoutFormat = "{0}: {1} {2}";

    [Tooltip("Seconds the time takes to count up from 00:00 to the module's when it starts.")]
    [SerializeField, Min(0f)] private float activationCountUpSeconds = 1.2f;

    [Tooltip("Seconds the readout takes to travel from the middle of the screen to its corner (it " +
             "shrinks on the way).")]
    [SerializeField, Min(0.01f)] private float travelSeconds = 0.9f;

    [Header("Bomb banner (middle of the screen, when a module starts)")]
    [Tooltip("Same character set as the label.")]
    [SerializeField] private string bannerText = "BOMB ACTIVATED";

    [Tooltip("Seconds the banner takes to sweep in, left to right.")]
    [SerializeField, Min(0.01f)] private float bannerSweepSeconds = 0.5f;

    [Tooltip("Seconds it stays whole, blinking.")]
    [SerializeField, Min(0f)] private float bannerHoldSeconds = 1.4f;

    [Tooltip("Seconds it takes to sweep out, left to right.")]
    [SerializeField, Min(0.01f)] private float bannerWipeSeconds = 0.4f;

    [Tooltip("Blinks per second, the whole time it is up.")]
    [SerializeField, Min(0.1f)] private float bannerBlinkHz = 3f;

    [Header("Readout blink (its time, from amber on, once the readout is in its corner)")]
    [Tooltip("Blinks per second of the recording dot. The readout's blink is measured against it.")]
    [SerializeField, Min(0.1f)] private float recBlinkHz = 1f;

    [Tooltip("When the readout turns amber its time blinks this many beats per minute faster than the dot.")]
    [SerializeField, Min(0f)] private float amberExtraBpm = 20f;

    [Tooltip("When the readout turns red its time blinks this many times as fast as the dot.")]
    [SerializeField, Min(1f)] private float redBlinkMultiplier = 2f;

    [Tooltip("Status of a module that is counting down.")]
    [SerializeField] private string inProgressText = "IN PROGRESS";

    [Tooltip("Status of a module solved in time, with the time it had left.")]
    [SerializeField] private string disarmedText = "DISARMED";

    [Tooltip("Status of a module that went off.")]
    [SerializeField] private string failedText = "FAILED";

    [Tooltip("Part of the module's time (its ModuleData) left under which the readout turns to " +
             "PlayerCamera.mat's Timer Warning Color (amber). 0 = never.")]
    [SerializeField, Range(0f, 1f)] private float moduleWarningFraction = 0.25f;

    [Tooltip("Part of the module's time left under which the readout turns to the Timer Critical " +
             "Color (red). A module that went off shows it too. 0 = never.")]
    [SerializeField, Range(0f, 1f)] private float moduleCriticalFraction = 0.1f;

    [Tooltip("Seconds left under which the readout turns to the Timer Critical Color, whatever " +
             "part of the module's time that is. 0 = never.")]
    [SerializeField, Min(0f)] private float moduleWarningSeconds = 30f;

    [Header("Boot")]
    [Tooltip("Seconds before ARC_01a that the picture comes in and the boot starts, under the boot " +
             "screen. The wake-up's boot then runs the whole cinematic: it ends, and the camera " +
             "starts recording, when the player is on their feet. It starts over the last seconds " +
             "of ArchitectVoiceController's Wake Up Delay, so keep that delay at least this long: " +
             "whatever is left of it before the picture stays black.")]
    [FormerlySerializedAs("bootSeconds")]
    [SerializeField, Min(0f)] private float pictureLeadSeconds = 1.5f;

    [Tooltip("Above the boot bar.")]
    [SerializeField] private string bootTitle = "BOOTING";

    [Tooltip("What the bar shows (0..1) over the boot (0..1: the whole wake-up, or the reboot's " +
             "seconds). The default climbs unevenly, hangs a while at 75% like every progress bar " +
             "ever, and finishes just before the end.")]
    [SerializeField] private AnimationCurve bootBar = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 2.5f),
        new Keyframe(0.12f, 0.3f, 2.2f, 1.2f),
        new Keyframe(0.3f, 0.52f, 1.2f, 0.95f),
        new Keyframe(0.55f, 0.75f, 0.9f, 0.08f),
        new Keyframe(0.8f, 0.77f, 0.08f, 1.5f),
        new Keyframe(0.95f, 1f, 1.5f, 0f),
        new Keyframe(1f, 1f, 0f, 0f));

    [Header("Picture coming in (seconds since it came in, at the start of the boot)")]
    [Tooltip("Static over the picture, 0..1. Starts full, clears, and hiccups once.")]
    [SerializeField] private AnimationCurve revealStatic = new AnimationCurve(
        new Keyframe(0f, 1f, 0f, 0f),
        new Keyframe(0.35f, 0f, 0f, 0f),
        new Keyframe(0.8f, 0f, 0f, 0f),
        new Keyframe(0.86f, 0.35f, 0f, 0f),
        new Keyframe(0.95f, 0f, 0f, 0f));

    [Tooltip("Picture brightness under the static, 0 (black) to 1.")]
    [SerializeField] private AnimationCurve revealPower = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(0.15f, 1f, 0f, 0f));

    [Tooltip("Exposure multiplier: the sensor comes in too bright and adapts. 1 = as rendered.")]
    [SerializeField] private AnimationCurve revealExposure = new AnimationCurve(
        new Keyframe(0f, 2.2f, 0f, 0f),
        new Keyframe(0.3f, 1.9f, -1.2f, -1.2f),
        new Keyframe(1.4f, 1f, 0f, 0f));

    [Tooltip("Focus blur, 0..1: the lens hunts (blurred, sharp, blurred) and settles.")]
    [SerializeField] private AnimationCurve revealFocus = new AnimationCurve(
        new Keyframe(0f, 1f, 0f, 0f),
        new Keyframe(0.45f, 0.15f, 0f, 0f),
        new Keyframe(0.8f, 0.5f, 0f, 0f),
        new Keyframe(1.5f, 0f, 0f, 0f));

    [Tooltip("Seconds after the boot ends (the camera starts recording) before the recording " +
             "overlay shows.")]
    [SerializeField, Min(0f)] private float overlayDelay = 0f;

    [Header("Fisheye (uncalibrated lens)")]
    [Tooltip("Degrees the camera's field of view opens up at full fisheye, on top of the walk FOV " +
             "(SO_CameraConfig). More = more of the room curving round the player: 85 renders at " +
             "160 degrees, a 360-camera look. The render loses sharpness at the centre as this grows.")]
    [SerializeField, Range(0f, 100f)] private float lensWidening = 85f;

    [Tooltip("Projection at full fisheye: 0 = equidistant fisheye (strongest curve), 0.5 = " +
             "stereographic, 1 = an ordinary lens.")]
    [SerializeField, Range(0f, 1f)] private float fisheyeProjection = 0f;

    [Tooltip("Scale of the centre at full fisheye. 1 = the player keeps their size; below 1 zooms " +
             "out and the picture becomes a circle with black round it (0.55 with 85 of widening: a " +
             "circle the height of the screen), above 1 zooms in.")]
    [SerializeField, Range(0.3f, 1.5f)] private float fisheyeZoom = 0.55f;

    [Tooltip("Moments (0..1 of the wake-up: picture in → player on their feet) the lens motor jumps " +
             "one step towards calibrated. N jumps = N equal steps; the last one sets the lens, so " +
             "keep it close to 1. Empty = the lens follows the Calibration curve smoothly instead.")]
    [SerializeField] private float[] lensJumps = { 0.06f, 0.35f, 0.65f, 0.95f };

    [Tooltip("Seconds each motor jump takes.")]
    [SerializeField, Min(0.01f)] private float lensJumpSeconds = 0.3f;

    [Tooltip("How far past each step the lens overshoots before settling back, as a fraction of the " +
             "step. 0 = no bounce.")]
    [SerializeField, Range(0f, 0.5f)] private float lensJumpOvershoot = 0.12f;

    [Tooltip("Focus blur at the middle of each jump, 0..1.")]
    [SerializeField, Range(0f, 1f)] private float lensJumpBlur = 0.5f;

    [Tooltip("Only with no Lens Jumps: how calibrated the lens is (0 = full fisheye, 1 = normal) " +
             "against the wake-up (0..1, picture in → on their feet). Must end on 1.")]
    [SerializeField] private AnimationCurve calibration = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(0.35f, 0.05f, 0.3f, 0.3f),
        new Keyframe(0.8f, 0.8f, 1.6f, 1.6f),
        new Keyframe(1f, 1f, 0f, 0f));

    [Header("Status line (under the label)")]
    [Tooltip("While the lens calibrates. {0} = percentage.")]
    [SerializeField] private string calibratingFormat = "CALIBRATING LENS  {0}%";

    [Tooltip("Blinks per second of the calibrating line. 0 = steady.")]
    [SerializeField, Min(0f)] private float calibratingBlink = 1.5f;

    [Tooltip("Once the player is up.")]
    [SerializeField] private string calibratedText = "LENS OK";

    [Tooltip("Seconds the calibrated line stays. 0 = none.")]
    [SerializeField, Min(0f)] private float calibratedSeconds = 2.5f;

    [Header("Reboot after a capture")]
    [Tooltip("When the Nemesis catches the player the camera loses its signal, and reboots at the " +
             "checkpoint as the black clears: a short boot, then the lens calibrates in the same " +
             "jumps while the player gets up.")]
    [SerializeField] private bool rebootOnCapture = true;

    [Tooltip("Seconds of boot screen in the reboot.")]
    [SerializeField, Min(0.1f)] private float rebootSeconds = 1f;

    [Tooltip("Above the reboot's bar.")]
    [SerializeField] private string rebootTitle = "REBOOTING";

    [Tooltip("Seconds the picture takes to drop to black through static when the signal is lost.")]
    [SerializeField, Min(0.01f)] private float signalLostSeconds = 0.6f;

    [Header("Cuts")]
    [Tooltip("Static when the feed cuts in: back from another camera (a cinematic shot, a security " +
             "camera) or a skipped wake-up. 0..1.")]
    [SerializeField, Range(0f, 1f)] private float cutStatic = 0.6f;

    [Tooltip("Seconds that static takes to clear. 0 = no cut static.")]
    [SerializeField, Min(0f)] private float cutStaticSeconds = 0.3f;

    public string Label => label;
    public bool OverlayDuringGameplay => overlayDuringGameplay;

    public float TypeCharsPerSecond => typeCharsPerSecond;
    public float EraseCharsPerSecond => eraseCharsPerSecond;

    public float ActivationCountUpSeconds => activationCountUpSeconds;
    public float TravelSeconds => travelSeconds;

    public string BannerText => bannerText;
    public float BannerBlinkHz => bannerBlinkHz;

    /// <summary>Seconds the banner is up, sweeping in, holding and sweeping out.</summary>
    public float BannerSeconds => bannerSweepSeconds + bannerHoldSeconds + bannerWipeSeconds;

    /// <summary>How far through its sweep in (x) and its sweep out (y) the banner is, 0..1 each,
    /// <paramref name="seconds"/> after it started.</summary>
    public Vector2 BannerSweep(float seconds) => new Vector2(
        Mathf.Clamp01(seconds / bannerSweepSeconds),
        Mathf.Clamp01((seconds - bannerSweepSeconds - bannerHoldSeconds) / bannerWipeSeconds));

    public float RecBlinkHz => recBlinkHz;

    /// <summary>
    /// Blinks per second of the time in <paramref name="module"/>'s readout, which only blinks from
    /// amber on (<paramref name="stage"/> from <see cref="ModuleTimerStage"/>, 1 or 2): the dot's
    /// rate plus <see cref="amberExtraBpm"/> when it turns amber, rising in a straight line to
    /// <see cref="redBlinkMultiplier"/> times the dot's at the critical fraction, and that from then on.
    /// </summary>
    public float ReadoutBlinkHz(ModuleRuntime module, int stage)
    {
        float amber = recBlinkHz + amberExtraBpm / 60f;
        float red = recBlinkHz * redBlinkMultiplier;

        float duration = module.Data != null ? module.Data.TimerDuration : 0f;
        if (stage >= 2 || duration <= 0f) return stage >= 2 ? red : amber;

        float left = Mathf.Clamp01(module.TimeRemaining / duration);
        return Mathf.Lerp(amber, red, Mathf.InverseLerp(moduleWarningFraction, moduleCriticalFraction, left));
    }

    /// <summary>The seconds the readout shows for <paramref name="module"/> by its state: what it
    /// has left, 0 once it went off, its full time (ModuleData) before it starts.</summary>
    public static float ModuleSeconds(ModuleRuntime module)
    {
        switch (module.Status)
        {
            case ModuleStatus.Exploded: return 0f;
            case ModuleStatus.Inactive: return module.Data != null ? module.Data.TimerDuration : 0f;
            default: return module.TimeRemaining;
        }
    }

    /// <summary>The readout line for <paramref name="module"/>, showing <paramref name="seconds"/>.
    /// Shows as written if the format is typed wrong.</summary>
    public string ModuleReadout(ModuleRuntime module, int index, float seconds)
    {
        string name = module.Data != null && !string.IsNullOrEmpty(module.Data.ModuleLogLabel)
            ? module.Data.ModuleLogLabel
            : "M" + (index + 1);

        string status = module.Status == ModuleStatus.Resolved ? disarmedText
                      : module.Status == ModuleStatus.Exploded ? failedText
                      : inProgressText;

        int whole = Mathf.Max(0, Mathf.FloorToInt(seconds));
        string time = $"{whole / 60:00}:{whole % 60:00}";

        try
        {
            return string.Format(moduleReadoutFormat, name, status, time);
        }
        catch (System.FormatException)
        {
            return moduleReadoutFormat;
        }
    }

    /// <summary>
    /// How alarming <paramref name="module"/>'s readout looks: 0 = the overlay's color, 1 = warning
    /// (amber) under <see cref="moduleWarningFraction"/> of its time, 2 = critical (red) under
    /// <see cref="moduleCriticalFraction"/> or the last <see cref="moduleWarningSeconds"/>, and once
    /// it went off. A module waiting or disarmed: 0.
    /// </summary>
    public int ModuleTimerStage(ModuleRuntime module)
    {
        if (module.Status == ModuleStatus.Exploded) return 2;
        if (module.Status != ModuleStatus.Active) return 0;

        float left = module.TimeRemaining;
        if (moduleWarningSeconds > 0f && left <= moduleWarningSeconds) return 2;

        float duration = module.Data != null ? module.Data.TimerDuration : 0f;
        if (duration <= 0f) return 0;

        float fraction = left / duration;
        if (moduleCriticalFraction > 0f && fraction <= moduleCriticalFraction) return 2;
        return moduleWarningFraction > 0f && fraction <= moduleWarningFraction ? 1 : 0;
    }

    public float PictureLeadSeconds => pictureLeadSeconds;
    public string BootTitle => bootTitle;
    public float BootBar(float t) => Mathf.Clamp01(bootBar.Evaluate(Mathf.Clamp01(t)));

    public float RevealStatic(float seconds) => Mathf.Clamp01(revealStatic.Evaluate(seconds));
    public float RevealPower(float seconds) => Mathf.Clamp01(revealPower.Evaluate(seconds));
    public float RevealExposure(float seconds) => Mathf.Max(0f, revealExposure.Evaluate(seconds));
    public float RevealFocus(float seconds) => Mathf.Clamp01(revealFocus.Evaluate(seconds));
    public float OverlayDelay => overlayDelay;

    public float LensWidening => lensWidening;
    public float FisheyeProjection => fisheyeProjection;
    public float FisheyeZoom => fisheyeZoom;
    public float LensJumpSeconds => lensJumpSeconds;
    public float LensJumpOvershoot => lensJumpOvershoot;
    public float LensJumpBlur => lensJumpBlur;

    /// <summary>The lens moves in motor jumps (else it follows the curve smoothly).</summary>
    public bool LensJumps => lensJumps != null && lensJumps.Length > 0;

    /// <summary>
    /// Where the lens is headed at <paramref name="wakeProgress"/>, 0 (full fisheye) to 1
    /// (calibrated): with jumps, one equal step per jump already passed; without, the curve.
    /// </summary>
    public float CalibrationTarget(float wakeProgress)
    {
        wakeProgress = Mathf.Clamp01(wakeProgress);
        if (!LensJumps) return Mathf.Clamp01(calibration.Evaluate(wakeProgress));

        int passed = 0;
        foreach (float jump in lensJumps)
            if (wakeProgress >= jump) passed++;
        return (float)passed / lensJumps.Length;
    }

    public string CalibratingFormat => calibratingFormat;
    public float CalibratingBlink => calibratingBlink;
    public string CalibratedText => calibratedText;
    public float CalibratedSeconds => calibratedSeconds;

    public bool RebootOnCapture => rebootOnCapture;
    public float RebootSeconds => rebootSeconds;
    public string RebootTitle => rebootTitle;
    public float SignalLostSeconds => signalLostSeconds;

    public float CutStatic => cutStatic;
    public float CutStaticSeconds => cutStaticSeconds;
}
