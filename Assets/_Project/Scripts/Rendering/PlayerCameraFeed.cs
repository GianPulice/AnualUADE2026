using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Makes the gameplay camera read as a camera — the one that follows the player — in the game's own
/// colours: while the player's rig is live, the frame goes through <c>PlayerCamera.mat</c> (drawn by
/// <see cref="PlayerFeedRendererFeature"/> on PC_Renderer) with a light lens barrel and grain, and
/// burnt in: the area the player is in (<see cref="CameraAreaZone"/>) top left, a blinking red
/// recording dot top right, viewfinder brackets, and bottom left a readout that is empty until a
/// module starts. A start sweeps "BOMB ACTIVATED" across the middle of the screen in red, blinking;
/// then the device's module types in there at 00:00, its time counts up to the module's, and the
/// readout travels to the corner, shrinking, and counts down: "M1: IN PROGRESS 14:35", DISARMED,
/// FAILED. The whole readout turns amber with a quarter of the module's time left, red with a
/// tenth or in the last 30 seconds (<see cref="SO_PlayerCameraFeedConfig.ModuleTimerStage"/>); until
/// then it is steady. From amber on its digits go out and come back, faster as the time runs out
/// (the text stays).
/// Both texts retype themselves when they change.
///
/// At the start of the level the camera boots (<see cref="PlayerCameraBoot"/>, run by
/// <see cref="WakeUpCinematicView"/>): black, then the picture comes in through static —
/// overexposed, hunting for focus, an extreme fisheye — under a boot screen that stays up the whole
/// cinematic, ARC_01a's subtitles under it, its bar hanging a while at 75%. The lens calibrates in
/// motor jumps while the player gets up; on their feet the boot ends and the camera starts
/// recording (its overlay comes on). The fisheye is real: the Cinemachine lens opens up by
/// <see cref="LensFovOffset"/> (<see cref="CameraSprintEffect"/> adds it) and the shader folds the
/// wider frame into a fisheye.
///
/// After a capture it reboots on its own: the signal drops to black through static as the capture
/// fade covers the screen, and when the fade starts lifting at the checkpoint
/// (<see cref="CaptureFadeView.OnCaptureRevealStarted"/>) a short reboot runs, the lens calibrating
/// over the checkpoint stand-up.
///
/// Nothing has to call it: every frame the renderer feature asks whether the camera's brain has the
/// player's rig on the air (<see cref="FindLive"/>), so a cut to any other shot — a cinematic, a
/// security camera — drops the look on the very frame of the cut, and cutting back brings it in with
/// a little static.
///
/// SETUP: on the FreeLook Camera of the Player prefab, with SO_PlayerCameraFeed.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class PlayerCameraFeed : MonoBehaviour
{
    public enum PreviewStage
    {
        Gameplay,       // The settled feed.
        Boot,           // The boot, Preview Amount through it: the picture coming in under the boot screen.
        Calibrating,    // Recording, the lens calibrating: Preview Amount = how far up the player is.
        Banner,         // The bomb banner, Preview Amount through its sweep in, hold and sweep out.
    }

    private const int LineCapacity = 32;
    private const int LabelLine = 0;
    private const int ClockLine = 1;
    private const int StatusLine = 2;
    private const int BootTitleLine = 3;
    private const int BootPercentLine = 4;
    private const int BannerLine = 5;
    private const int LineCount = 6;

    // For the preview only: seconds from the picture coming in to the player on their feet — the
    // wake-up's stand-up clip.
    private const float PreviewWakeSeconds = 11.4f;

    [SerializeField] private SO_PlayerCameraFeedConfig config;

    [Header("Edit-mode preview")]
    [Tooltip("Shows the feed on the Game view outside Play, to tune PlayerCamera.mat and the config. " +
             "Out of Play the lens cannot open up, so the fisheye shows more black round the picture " +
             "than it will in the game. In Play this does nothing: the wake-up drives the feed.")]
    [SerializeField] private bool previewInEditMode;

    [SerializeField] private PreviewStage previewStage = PreviewStage.Gameplay;

    [Tooltip("How far through the previewed stage: the boot, or the player getting up.")]
    [SerializeField, Range(0f, 1f)] private float previewAmount = 1f;

    private enum Status { None, Calibrating, Calibrated }

    // One player camera. The text buffer is shared by nothing else (the security feeds have theirs).
    private static PlayerCameraFeed current;
    private static readonly float[] Text = new float[LineCapacity * LineCount];

    private static readonly int TextId = Shader.PropertyToID("_PlayerFeedText");
    private static readonly int InfoId = Shader.PropertyToID("_PlayerFeedInfo");
    private static readonly int BootId = Shader.PropertyToID("_PlayerFeedBoot");
    private static readonly int LensId = Shader.PropertyToID("_PlayerFeedLens");
    private static readonly int SignalId = Shader.PropertyToID("_PlayerFeedSignal");
    private static readonly int ThreatId = Shader.PropertyToID("_PlayerFeedThreat");
    private static readonly int ReadoutId = Shader.PropertyToID("_PlayerFeedReadout");
    private static readonly int ReadoutTimeId = Shader.PropertyToID("_PlayerFeedReadoutTime");
    private static readonly int BannerId = Shader.PropertyToID("_PlayerFeedBanner");
    private static readonly int RecBlinkId = Shader.PropertyToID("_PlayerFeedRecBlink");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => LensFovOffset = 0f;

    /// <summary>
    /// Degrees the camera's lens must open up this frame for the fisheye; 0 outside the wake-up.
    /// <see cref="CameraSprintEffect"/>, the one component that writes the lens FOV, adds it. Anything
    /// that measures the screen from the camera's FOV wants the FOV without it.
    /// </summary>
    public static float LensFovOffset { get; private set; }

    /// <summary>A feed with its config is on the player's camera: the wake-up can boot it.</summary>
    public static bool IsAvailable => current != null && current.config != null && current.isActiveAndEnabled;

    /// <summary>Seconds before ARC_01a the picture comes in and the boot starts; 0 when
    /// <see cref="IsAvailable"/> is false.</summary>
    public static float PictureLeadSeconds => IsAvailable ? current.config.PictureLeadSeconds : 0f;

    private CinemachineVirtualCameraBase shot;
    private PlayerCameraController cameraController;

    // This frame's feed, worked out in Update (Play) or right before drawing (preview).
    private float power = 1f;
    private float staticAmount;
    private float blur;
    private float exposure = 1f;
    private float fisheye;
    private float overlay;
    private float boot;
    private float bar;
    private Status status;
    private bool statusShown;

    // The lens motor: how calibrated the lens is (0 = full fisheye, 1 = set) and the jump it is in.
    private float lensShown = 1f;
    private float lensFrom = 1f;
    private float lensTo = 1f;
    private float lensJumpStart = float.NegativeInfinity;
    private float lensBack;
    private float lastEvaluated = float.NegativeInfinity;

    // What the text buffer holds, so it is only rewritten when something on screen changes.
    private bool textDirty = true;
    private string writtenBootTitle;
    private string writtenBanner;
    private Status writtenStatus = (Status)(-1);
    private int writtenStatusPercent = -1;
    private int writtenBootPercent = -1;
    private int statusLength;
    private int bootTitleLength;
    private int bootPercentLength;

    // The two lines that retype themselves when they change: the label (top left) and the readout
    // (bottom left). Typed in again whenever the recording overlay comes back on screen.
    private readonly TypedLine labelLine = new TypedLine(LabelLine * LineCapacity);
    private readonly TypedLine readoutLine = new TypedLine(ClockLine * LineCapacity);
    private bool overlayOnline;

    // The readout's look: how far it has travelled from the middle of the screen to its corner, its
    // color stage (ModuleTimerStage), its blink, and the length it will have typed (to centre on).
    private float readoutTravel = 1f;
    private int readoutStage;
    private float readoutBlinkPhase = -1f;   // 0..1 through a blink (lit for the first half); -1 = steady
    private int readoutTargetLength;

    // Where the time sits in the readout's text (first glyph, glyph count): the part that blinks.
    private int readoutTimeStart;
    private int readoutTimeLength;

    // The bomb banner: how far it has swept in and out, and its length (0 = not up).
    private Vector2 bannerSweep;
    private int bannerLength;

    // The readout: nothing until a module starts, then that module. A start sweeps the banner, then
    // types the readout in the middle at 00:00, counts the time up to the module's and sends it to
    // its corner (once per module), where the countdown goes on.
    private enum Intro { Banner, Middle, Travel, Docked }
    private Intro intro = Intro.Docked;
    private float introStart;
    private ModuleRuntime focusModule;
    private ModuleRuntime lastActive;
    private bool countUpPending;
    private float countUpStart = -1f;
    private int readoutIdentity = int.MinValue;
    private int readoutKey = int.MinValue;

    // Readout identity of the empty readout; a module's is its index and status.
    private const int IdleIdentity = -1;

    // What the edit-mode preview shows in the corner.
    private const string PreviewReadout = "M1: IN PROGRESS 14:57";

    private int lastShownFrame = int.MinValue;
    private float cutInAt = float.NegativeInfinity;

    // The reboot after a capture, which this component runs itself.
    private enum Reboot { Idle, SignalLost, Booting, Recording }
    private Reboot reboot;
    private float rebootStart;
    private bool rebootStandUpSeen;

    // Awake/OnDestroy for the static event, as the project does everywhere (docs/UI-System.md §7.1).
    private void Awake()
    {
        CaptureFadeView.OnCaptureRevealStarted += HandleCaptureRevealStarted;
        NemesisEvents.OnProximityChanged += HandleNemesisProximity;
        NemesisEvents.OnChaseStarted += HandleChaseStarted;
        NemesisEvents.OnChaseEnded += HandleChaseEnded;
    }

    private void OnDestroy()
    {
        CaptureFadeView.OnCaptureRevealStarted -= HandleCaptureRevealStarted;
        NemesisEvents.OnProximityChanged -= HandleNemesisProximity;
        NemesisEvents.OnChaseStarted -= HandleChaseStarted;
        NemesisEvents.OnChaseEnded -= HandleChaseEnded;
    }

    // The Nemesis closing in disturbs the signal: 0 = at or beyond its proximity radius, 1 = on
    // top of the player. Already interpolated by NemesisTelemetry, so it is used as it comes.
    private float nemesisProximity;
    private bool nemesisChasing;

    private void HandleNemesisProximity(float t) => nemesisProximity = Mathf.Clamp01(t);
    private void HandleChaseStarted() => nemesisChasing = true;
    private void HandleChaseEnded() => nemesisChasing = false;

    /// <summary>0..1: how far the Nemesis's closeness disturbs the picture right now.</summary>
    private float Threat()
    {
        if (config == null || !Application.isPlaying) return 0f;
        if (config.ThreatOnlyWhileChasing && !nemesisChasing) return 0f;
        return Mathf.Pow(nemesisProximity, config.ThreatCurve);
    }

    private void OnEnable()
    {
        shot = GetComponent<CinemachineVirtualCameraBase>();
        cameraController = GetComponent<PlayerCameraController>();
        current = this;
        textDirty = true;

        // A new level's camera: nothing a previous one left half-booted carries over.
        reboot = Reboot.Idle;
        if (Application.isPlaying) PlayerCameraBoot.Clear();
    }

    private void OnDisable()
    {
        if (current != this) return;
        current = null;
        LensFovOffset = 0f;
    }

    private void OnValidate() => textDirty = true;

    // Update, not LateUpdate: CameraSprintEffect writes the lens in its LateUpdate and must find
    // this frame's offset already here.
    private void Update()
    {
        if (!Application.isPlaying) return;
        TickReboot(Time.unscaledTime);
        Evaluate(Time.unscaledTime);
        TickReadouts(Time.unscaledTime);
        LensFovOffset = config != null ? config.LensWidening * fisheye : 0f;
    }

    /// <summary>
    /// The feed to draw over <paramref name="camera"/> this frame, or null. In Play: the player's,
    /// while its rig is the live shot on that camera's brain. Out of Play: the player's, when it is
    /// previewing.
    /// </summary>
    public static PlayerCameraFeed FindLive(Camera camera)
    {
        PlayerCameraFeed feed = current;
        if (feed == null || feed.config == null || camera == null || !feed.isActiveAndEnabled) return null;

        if (!Application.isPlaying) return feed.previewInEditMode ? feed : null;

        if (!camera.TryGetComponent(out CinemachineBrain brain)) return null;
        return feed.shot != null && brain.IsLiveChild(feed.shot, true) ? feed : null;
    }

    /// <summary>
    /// Publishes this frame's feed for <paramref name="camera"/>. Called by the renderer feature,
    /// once per frame the feed is on the air: a frame without a call is a frame off the air, so the
    /// next call is a cut in.
    /// </summary>
    /// <returns>False when there is nothing to draw (settled, with the overlay off for gameplay):
    /// the pass is then skipped.</returns>
    public bool PushGlobals(Camera camera)
    {
        bool playing = Application.isPlaying;
        float now = playing ? Time.unscaledTime : Time.realtimeSinceStartup;

        if (playing)
        {
            int frame = Time.frameCount;
            if (frame != lastShownFrame)
            {
                // Back on the air after a gap: another camera had it. Not on the very first frame.
                if (lastShownFrame != int.MinValue && frame - lastShownFrame > 1) cutInAt = now;
                lastShownFrame = frame;
            }
        }
        else
        {
            EvaluatePreview();
        }

        float cut = playing ? CutStatic(now) : 0f;
        float noise = Mathf.Max(staticAmount, cut);
        float threat = Threat();

        bool plain = PlayerCameraBoot.Current == PlayerCameraBoot.Phase.None &&
                     overlay <= 0f && noise <= 0f && fisheye <= 0f && threat <= 0f;
        if (plain && playing) return false;

        UpdateText(playing);

        Shader.SetGlobalVector(InfoId, new Vector4(labelLine.Length, readoutLine.Length,
                                                    statusShown ? statusLength : 0, overlay));
        Shader.SetGlobalVector(ReadoutId, new Vector4(readoutTravel, readoutTargetLength, readoutStage,
                                                       readoutBlinkPhase));
        Shader.SetGlobalVector(ReadoutTimeId, new Vector4(readoutTimeStart, readoutTimeLength, 0f, 0f));
        Shader.SetGlobalVector(BannerId, new Vector4(bannerSweep.x, bannerSweep.y, bannerLength,
                                                      config.BannerBlinkHz));
        Shader.SetGlobalFloat(RecBlinkId, config.RecBlinkHz);
        Shader.SetGlobalVector(BootId, new Vector4(boot, bar, bootTitleLength, bootPercentLength));
        Shader.SetGlobalVector(LensId, LensGlobals(camera, playing));
        Shader.SetGlobalVector(SignalId, new Vector4(power, noise, blur, exposure));
        Shader.SetGlobalVector(ThreatId, new Vector4(threat * config.ThreatStatic,
                                                      threat * config.ThreatGlitchChance, 0f, 0f));
        return true;
    }

    // ── Evaluation ──────────────────────────────────────────────────────────────────────

    private void Evaluate(float now)
    {
        if (config == null) return;

        status = Status.None;
        boot = 0f;
        bar = 0f;

        switch (PlayerCameraBoot.Current)
        {
            case PlayerCameraBoot.Phase.Off:
                SetDark();

                // A lost signal drops to black through static, lens untouched; black, the lens
                // waits wide open for the next boot.
                float lost = SignalLostFade(now);
                if (lost > 0f)
                {
                    power = lost;
                    staticAmount = Mathf.Sqrt(lost);
                }
                else
                {
                    SnapLens(0f);
                }
                break;

            case PlayerCameraBoot.Phase.Booting:
                SetPicture(now - PlayerCameraBoot.PictureSince);
                MoveLens(config.CalibrationTarget(PlayerCameraBoot.WakeProgress), now);
                boot = 1f;
                bar = config.BootBar(PlayerCameraBoot.BootProgress);
                overlay = 0f;
                break;

            case PlayerCameraBoot.Phase.Live:
                SetPicture(now - PlayerCameraBoot.PictureSince);
                MoveLens(config.CalibrationTarget(PlayerCameraBoot.WakeProgress), now);
                overlay = now - PlayerCameraBoot.LiveSince >= config.OverlayDelay ? 1f : 0f;
                status = overlay > 0f ? Status.Calibrating : Status.None;
                break;

            default:
                // A skip snaps the lens (under the cut's static); a wake-up that played out lets the
                // last jump finish.
                // (>=: the skip may land after this Update in the frame it happens.)
                if (PlayerCameraBoot.CutAt >= lastEvaluated) SnapLens(1f);
                else MoveLens(1f, now);
                SetSettled(now - PlayerCameraBoot.CalibratedAt);
                break;
        }

        fisheye = Mathf.Clamp01(1f - lensShown);
        statusShown = status == Status.Calibrated ||
                      (status == Status.Calibrating &&
                       (config.CalibratingBlink <= 0f || Mathf.Repeat(now * config.CalibratingBlink, 1f) < 0.65f));
        lastEvaluated = now;
    }

    private void EvaluatePreview()
    {
        if (config == null) return;

        status = Status.None;
        boot = 0f;
        bar = 0f;

        // The readout docked and steady, the banner down: only the Banner stage shows it.
        readoutTravel = 1f;
        readoutStage = 0;
        readoutBlinkPhase = -1f;
        bannerLength = 0;

        switch (previewStage)
        {
            case PreviewStage.Boot:
                SetPicture(previewAmount * PreviewWakeSeconds);
                lensShown = config.CalibrationTarget(previewAmount);
                boot = 1f;
                bar = config.BootBar(previewAmount);
                overlay = 0f;
                break;

            case PreviewStage.Calibrating:
                // Past the picture's arrival: settled signal, the lens where the player is.
                SetPicture(1000f);
                lensShown = config.CalibrationTarget(previewAmount);
                overlay = 1f;
                status = Status.Calibrating;
                break;

            case PreviewStage.Banner:
                SetPicture(1000f);
                lensShown = 1f;
                overlay = 1f;
                bannerLength = Mathf.Min(config.BannerText.Length, LineCapacity);
                bannerSweep = config.BannerSweep(previewAmount * config.BannerSeconds);
                break;

            default:
                lensShown = 1f;
                SetSettled(float.PositiveInfinity);
                break;
        }

        fisheye = Mathf.Clamp01(1f - lensShown);
        statusShown = status != Status.None;
    }

    /// <summary>Camera off: black.</summary>
    private void SetDark()
    {
        power = 0f;
        staticAmount = 0f;
        blur = 0f;
        exposure = 1f;
        overlay = 0f;
    }

    /// <param name="seconds">Seconds since the picture came in.</param>
    private void SetPicture(float seconds)
    {
        power = config.RevealPower(seconds);
        staticAmount = config.RevealStatic(seconds);
        blur = config.RevealFocus(seconds);
        exposure = config.RevealExposure(seconds);
    }

    /// <param name="sinceCalibrated">Seconds since the wake-up ended with the lens set.</param>
    private void SetSettled(float sinceCalibrated)
    {
        power = 1f;
        staticAmount = 0f;
        blur = LensJumpBlur(Time.unscaledTime);
        exposure = 1f;

        bool justCalibrated = sinceCalibrated >= 0f && sinceCalibrated < config.CalibratedSeconds;
        overlay = config.OverlayDuringGameplay || justCalibrated ? 1f : 0f;
        status = justCalibrated ? Status.Calibrated : Status.None;
    }

    // ── Lens motor ──────────────────────────────────────────────────────────────────────

    private void SnapLens(float calibration)
    {
        lensShown = lensFrom = lensTo = calibration;
        lensJumpStart = float.NegativeInfinity;
    }

    /// <summary>
    /// Heads the lens for <paramref name="target"/>. With motor jumps a new target starts a jump from
    /// wherever the lens is — quick, overshooting a little and settling, blurring as it goes;
    /// without, the lens simply follows.
    /// </summary>
    private void MoveLens(float target, float now)
    {
        if (!config.LensJumps)
        {
            SnapLens(target);
            return;
        }

        if (!Mathf.Approximately(target, lensTo))
        {
            lensFrom = lensShown;
            lensTo = target;
            lensJumpStart = now;
            lensBack = BackAmount(config.LensJumpOvershoot);
        }

        float u = Mathf.Clamp01((now - lensJumpStart) / config.LensJumpSeconds);
        float v = u - 1f;
        float eased = 1f + (lensBack + 1f) * v * v * v + lensBack * v * v;
        lensShown = Mathf.LerpUnclamped(lensFrom, lensTo, eased);

        blur = Mathf.Max(blur, LensJumpBlur(now));
    }

    /// <summary>The focus blur of a jump under way, peaking half way through it.</summary>
    private float LensJumpBlur(float now)
    {
        if (config == null || !config.LensJumps) return 0f;
        float u = (now - lensJumpStart) / config.LensJumpSeconds;
        return u >= 0f && u < 1f ? config.LensJumpBlur * Mathf.Sin(u * Mathf.PI) : 0f;
    }

    /// <summary>
    /// The "back" constant of an ease-out-back whose overshoot peaks at <paramref name="overshoot"/>
    /// of the step: that peak is 4s³ / (27 (s + 1)²), solved for s.
    /// </summary>
    private static float BackAmount(float overshoot)
    {
        if (overshoot <= 0f) return 0f;

        float s = 1.70158f;   // The classic constant: a 10% overshoot.
        for (int i = 0; i < 8; i++)
        {
            float f = 4f * s * s * s - 27f * overshoot * (s + 1f) * (s + 1f);
            float df = 12f * s * s - 54f * overshoot * (s + 1f);
            if (Mathf.Abs(df) < 1e-5f) break;
            s -= f / df;
        }
        return Mathf.Max(s, 0f);
    }

    /// <summary>1 the moment the signal is lost, down to 0 once the picture is gone.</summary>
    private float SignalLostFade(float now)
    {
        float u = (now - PlayerCameraBoot.SignalLostAt) / config.SignalLostSeconds;
        return u >= 0f && u < 1f ? 1f - u : 0f;
    }

    // ── Reboot after a capture ──────────────────────────────────────────────────────────
    //
    // Keyed to the player's own capture span (IsRecoveringFromCapture: from the grab until they are
    // up with control) and to the capture fade lifting — the fade covers the teleport and the
    // Nemesis's retreat, so the reboot has to wait for it to be on screen at all.

    private void TickReboot(float now)
    {
        if (config == null || !config.RebootOnCapture) return;

        PlayerStateManager player = PlayerRegistry.Current;
        bool recovering = player != null && player.IsRecoveringFromCapture;

        switch (reboot)
        {
            case Reboot.Idle:
                // The grab: the signal drops out as the capture fade covers the screen.
                if (recovering && PlayerCameraBoot.Current == PlayerCameraBoot.Phase.None)
                {
                    reboot = Reboot.SignalLost;
                    PlayerCameraBoot.SetOff(signalLost: true);
                }
                break;

            case Reboot.SignalLost:
                // Normally the fade lifting starts the boot (HandleCaptureRevealStarted). Without a
                // HUD to say so, the stand-up starting does.
                if (!recovering) EndReboot(cut: true);
                else if (player.IsCaptureStandingUp && player.StandUpProgress > 0f) StartRebootBoot(now);
                break;

            case Reboot.Booting:
                if (!recovering)
                {
                    EndReboot(cut: true);
                    break;
                }

                float progress = (now - rebootStart) / Mathf.Max(config.RebootSeconds, 0.01f);
                if (progress >= 1f)
                {
                    reboot = Reboot.Recording;
                    PlayerCameraBoot.SetLive();
                }
                else
                {
                    PlayerCameraBoot.SetBooting(progress, reboot: true);
                }
                TickRebootLens(player);
                break;

            case Reboot.Recording:
                // Up with control: the lens has landed with the stand-up's last frame.
                if (!recovering) EndReboot(cut: false);
                else TickRebootLens(player);
                break;
        }
    }

    private void HandleCaptureRevealStarted()
    {
        if (reboot == Reboot.SignalLost) StartRebootBoot(Time.unscaledTime);
    }

    private void StartRebootBoot(float now)
    {
        reboot = Reboot.Booting;
        rebootStart = now;
        rebootStandUpSeen = false;
        PlayerCameraBoot.SetBooting(0f, reboot: true);
    }

    /// <summary>The lens follows the checkpoint stand-up, which starts as the fade finishes lifting:
    /// until then it waits, and once the stand-up is over it is set, even if control takes a frame
    /// longer to come back.</summary>
    private void TickRebootLens(PlayerStateManager player)
    {
        float progress;
        if (player.IsCaptureStandingUp)
        {
            progress = player.StandUpProgress;
            if (progress > 0f) rebootStandUpSeen = true;
        }
        else
        {
            progress = rebootStandUpSeen ? 1f : 0f;
        }
        PlayerCameraBoot.SetWakeProgress(progress);
    }

    private void EndReboot(bool cut)
    {
        reboot = Reboot.Idle;
        PlayerCameraBoot.End(cut);
    }

    /// <summary>Static of a cut in (back from another camera) or of a skipped wake-up.</summary>
    private float CutStatic(float now)
    {
        if (config == null || config.CutStaticSeconds <= 0f) return 0f;

        float since = now - Mathf.Max(cutInAt, PlayerCameraBoot.CutAt);
        return config.CutStatic * Mathf.Clamp01(1f - since / config.CutStaticSeconds);
    }

    /// <summary>
    /// The lens globals: the focal length the frame was rendered with (the camera's real FOV, lens
    /// opening included) and the one to show it with (the FOV without the opening, times the
    /// fisheye's zoom), so the centre keeps a steady scale whatever the fisheye does.
    /// </summary>
    private Vector4 LensGlobals(Camera camera, bool playing)
    {
        float fov = Mathf.Clamp(camera.fieldOfView, 1f, 179f);

        // Out of Play nothing opens the lens, so the nominal FOV is the rig's own.
        float nominal = playing
            ? fov - LensFovOffset
            : cameraController != null && cameraController.Config != null ? cameraController.Config.WalkFov : fov;
        nominal = Mathf.Clamp(nominal, 1f, 179f);

        float renderedFocal = 1f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        float shownFocal = 1f / Mathf.Tan(nominal * 0.5f * Mathf.Deg2Rad) * Mathf.Lerp(1f, config.FisheyeZoom, fisheye);
        float projection = Mathf.Lerp(1f, config.FisheyeProjection, fisheye);

        return new Vector4(renderedFocal, shownFocal, projection, fisheye);
    }

    // ── Text ────────────────────────────────────────────────────────────────────────────

    private void UpdateText(bool playing)
    {
        float now = playing ? Time.unscaledTime : Time.realtimeSinceStartup;

        if (!playing)
        {
            // The preview shows the settled lines, no typing.
            labelLine.Set(config.Label, false, now);
            readoutLine.Set(PreviewReadout, false, now);
            readoutTargetLength = PreviewReadout.Length;
        }

        if (textDirty)
        {
            labelLine.Invalidate();
            readoutLine.Invalidate();
        }
        if (labelLine.Draw(Text, now, config.EraseCharsPerSecond, config.TypeCharsPerSecond)) textDirty = true;
        if (readoutLine.Draw(Text, now, config.EraseCharsPerSecond, config.TypeCharsPerSecond)) textDirty = true;

        if (textDirty || writtenBanner != config.BannerText)
        {
            writtenBanner = config.BannerText;
            CameraFeedFont.WriteLine(Text, BannerLine * LineCapacity, LineCapacity, writtenBanner);
            textDirty = true;
        }

        string bootTitle = PlayerCameraBoot.IsReboot ? config.RebootTitle : config.BootTitle;
        if (textDirty || writtenBootTitle != bootTitle)
        {
            writtenBootTitle = bootTitle;
            bootTitleLength = CameraFeedFont.WriteLine(Text, BootTitleLine * LineCapacity, LineCapacity, writtenBootTitle);
            textDirty = true;
        }

        int statusPercent = Mathf.FloorToInt(Mathf.Clamp01(lensShown) * 100f);
        if (textDirty || status != writtenStatus || (status == Status.Calibrating && statusPercent != writtenStatusPercent))
        {
            writtenStatus = status;
            writtenStatusPercent = statusPercent;
            string line = status == Status.Calibrating ? CalibratingLine(statusPercent)
                        : status == Status.Calibrated ? config.CalibratedText
                        : null;
            statusLength = CameraFeedFont.WriteLine(Text, StatusLine * LineCapacity, LineCapacity, line);
            textDirty = true;
        }

        int bootPercent = boot > 0f ? Mathf.FloorToInt(bar * 100f) : -1;
        if (textDirty || bootPercent != writtenBootPercent)
        {
            writtenBootPercent = bootPercent;
            bootPercentLength = CameraFeedFont.WriteLine(Text, BootPercentLine * LineCapacity, LineCapacity,
                                                         bootPercent >= 0 ? bootPercent + "%" : null);
            textDirty = true;
        }

        if (!textDirty) return;
        textDirty = false;
        Shader.SetGlobalFloatArray(TextId, Text);
    }

    /// <summary>The calibrating line. Runs inside the render: a format typed wrong in the config
    /// shows as written instead of throwing every frame.</summary>
    private string CalibratingLine(int percent)
    {
        try
        {
            return string.Format(config.CalibratingFormat, percent);
        }
        catch (System.FormatException)
        {
            return config.CalibratingFormat;
        }
    }

    // ── Label and readout ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The label (the area the player is in, <see cref="CameraAreaZone"/>) and the readout. Both
    /// type in when the recording overlay comes on screen, and retype when what they say changes;
    /// the readout's time ticking just rewrites it. Also what a module starting plays: the bomb
    /// banner, the readout typed in the middle and counting up, its travel to the corner.
    /// </summary>
    private void TickReadouts(float now)
    {
        if (config == null) return;

        bool online = overlay > 0f;
        if (online && !overlayOnline)
        {
            // The camera is (back) on the air: both lines type in from nothing.
            labelLine.Clear();
            readoutLine.Clear();
            readoutIdentity = int.MinValue;
        }
        overlayOnline = online;

        string area = CameraAreaZone.CurrentLabel;
        string label = string.IsNullOrEmpty(area) ? config.Label : area;
        if (label != labelLine.Target) labelLine.Set(label, true, now);

        int identity;
        string text;
        bool showsModule = TryModuleReadout(now, out identity, out text);

        // The banner owns the screen until it has swept out; the readout types in after it.
        if (intro == Intro.Banner && now - introStart >= config.BannerSeconds) intro = Intro.Middle;
        if (showsModule && intro == Intro.Banner) showsModule = false;

        if (!showsModule)
        {
            identity = IdleIdentity;
            text = string.Empty;
            readoutKey = int.MinValue;
        }

        // What the line says changed (a module started, disarmed, failed): retype. The time ticking:
        // rewrite.
        bool animate = identity != readoutIdentity;
        if (animate || text != readoutLine.Target) readoutLine.Set(text, animate, now);
        readoutIdentity = identity;

        // The start's count-up waits for the line to finish typing in at 00:00 in the middle.
        if (countUpPending && intro == Intro.Middle && readoutLine.IsSettled(now))
        {
            countUpPending = false;
            countUpStart = now;
        }

        // Once it has counted up, the readout leaves the middle for its corner.
        if (intro == Intro.Middle && !countUpPending && countUpStart < 0f)
        {
            intro = Intro.Travel;
            introStart = now;
        }

        float travelled = Mathf.Clamp01((now - introStart) / config.TravelSeconds);
        if (intro == Intro.Travel && travelled >= 1f) intro = Intro.Docked;

        readoutTravel = intro == Intro.Docked ? 1f
                      : intro == Intro.Travel ? travelled * travelled * (3f - 2f * travelled)
                      : 0f;
        readoutTargetLength = readoutLine.Target.Length;

        // The readout's color as the module runs out (amber, then red), and the blink of its time:
        // only in the corner, with the module running and from amber on, faster the less time is
        // left. Steady (white) before that.
        readoutStage = showsModule ? config.ModuleTimerStage(focusModule) : 0;
        FindTime(readoutLine.Target, out readoutTimeStart, out readoutTimeLength);
        bool counting = showsModule && focusModule.Status == ModuleStatus.Active && readoutStage >= 1 &&
                        (intro == Intro.Travel || intro == Intro.Docked);

        // The blink's speed changes with the time left, so its phase is accumulated here: a phase
        // worked out from the clock would jump every time the speed moved. It starts lit.
        if (!counting)
            readoutBlinkPhase = -1f;
        else
            readoutBlinkPhase = Mathf.Repeat(Mathf.Max(readoutBlinkPhase, 0f) +
                                             config.ReadoutBlinkHz(focusModule, readoutStage) * Time.unscaledDeltaTime, 1f);

        bannerLength = intro == Intro.Banner ? Mathf.Min(config.BannerText.Length, LineCapacity) : 0;
        if (bannerLength > 0) bannerSweep = config.BannerSweep(now - introStart);
    }

    /// <summary>
    /// The readout for the device: the running module, or with none running the last one that ran.
    /// A module starting is the moment it appears — typed in at 00:00, its time counting up to the
    /// module's, then counting down. Rebuilt only when what it shows changes.
    /// </summary>
    /// <returns>False before any module has started: the readout stays empty.</returns>
    private bool TryModuleReadout(float now, out int identity, out string text)
    {
        identity = IdleIdentity;
        text = null;
        if (!ModuleManager.Exists) return false;

        ModuleManager manager = ModuleManager.Instance;
        IReadOnlyList<ModuleRuntime> modules = manager.GetAllModules();
        if (modules == null || modules.Count == 0) return false;

        // A new session rebuilt the modules: back to idle.
        if (focusModule != null && IndexOf(modules, focusModule) < 0) focusModule = lastActive = null;

        ModuleRuntime active = manager.GetActiveModule();
        if (active != null && active != lastActive)
        {
            focusModule = active;
            countUpPending = true;
            countUpStart = -1f;
            intro = Intro.Banner;
            introStart = now;
        }
        lastActive = active;

        if (focusModule == null) return false;

        int index = IndexOf(modules, focusModule);
        float seconds = SO_PlayerCameraFeedConfig.ModuleSeconds(focusModule);

        // The start: 00:00 while it types in, then up to the module's time (the live one, so the
        // count-up lands exactly where the countdown already is).
        if (countUpPending)
        {
            seconds = 0f;
        }
        else if (countUpStart >= 0f)
        {
            float u = config.ActivationCountUpSeconds > 0f
                ? Mathf.Clamp01((now - countUpStart) / config.ActivationCountUpSeconds)
                : 1f;
            if (u >= 1f) countUpStart = -1f;
            else seconds *= 1f - (1f - u) * (1f - u) * (1f - u);
        }

        identity = index * 8 + (int)focusModule.Status;
        int key = identity * 100000 + Mathf.Clamp(Mathf.FloorToInt(seconds), 0, 99999);
        if (key != readoutKey || readoutLine.Target.Length == 0)
        {
            readoutKey = key;
            text = config.ModuleReadout(focusModule, index, seconds);
        }
        else
        {
            text = readoutLine.Target;
        }
        return true;
    }

    /// <summary>Where the last "MM:SS" in <paramref name="text"/> is: the readout's time. Nothing
    /// (length 0) when there is none.</summary>
    private static void FindTime(string text, out int start, out int length)
    {
        start = 0;
        length = 0;
        for (int i = text.Length - 5; i >= 0; i--)
        {
            if (char.IsDigit(text[i]) && char.IsDigit(text[i + 1]) && text[i + 2] == ':' &&
                char.IsDigit(text[i + 3]) && char.IsDigit(text[i + 4]))
            {
                start = i;
                length = 5;
                return;
            }
        }
    }

    private static int IndexOf(IReadOnlyList<ModuleRuntime> modules, ModuleRuntime module)
    {
        for (int i = 0; i < modules.Count; i++)
            if (modules[i] == module) return i;
        return -1;
    }

    /// <summary>
    /// A line of the overlay that retypes itself: on an animated change what is on screen erases
    /// behind a cursor and the new text types in after it; a plain change (a timer ticking) just
    /// replaces the text, mid-typing included. Written into the shared buffer only when what it
    /// shows changes.
    /// </summary>
    private sealed class TypedLine
    {
        private readonly int start;

        // Erasing: a prefix of `from`, shrinking from fromCount. Typing: a prefix of `to`, growing.
        private string from = string.Empty;
        private int fromCount;
        private string to = string.Empty;
        private float changedAt;
        private bool changing;

        private string drawnText;
        private int drawnCount = -1;
        private bool drawnCursor;

        // The speeds of the last Draw: Set needs them to know what is on screen at the moment.
        private float eraseSpeed = 60f;
        private float typeSpeed = 30f;

        public TypedLine(int start) => this.start = start;

        /// <summary>The text the line shows, or is heading to.</summary>
        public string Target => to;

        /// <summary>Glyphs on screen, cursor included.</summary>
        public int Length { get; private set; }

        public void Set(string text, bool animate, float now)
        {
            text ??= string.Empty;
            if (animate)
            {
                // What is on screen right now becomes what erases.
                Visible(now, out string shown, out int count, out _);
                from = shown;
                fromCount = count;
                changedAt = now;
                changing = true;
            }
            to = text;
        }

        /// <summary>Nothing on screen: the next animated change types in without erasing.</summary>
        public void Clear()
        {
            from = to = string.Empty;
            fromCount = 0;
            changing = false;
        }

        public void Invalidate() => drawnCount = -1;

        /// <summary>The last animated change has finished erasing and typing.</summary>
        public bool IsSettled(float now) =>
            !changing || now - changedAt >= fromCount / eraseSpeed + to.Length / typeSpeed;

        /// <returns>True when the buffer changed.</returns>
        public bool Draw(float[] buffer, float now, float erase, float type)
        {
            eraseSpeed = Mathf.Max(erase, 1f);
            typeSpeed = Mathf.Max(type, 1f);
            Visible(now, out string text, out int count, out bool cursor);
            if (count == drawnCount && cursor == drawnCursor && ReferenceEquals(text, drawnText)) return false;

            drawnText = text;
            drawnCount = count;
            drawnCursor = cursor;
            Length = CameraFeedFont.WriteTyped(buffer, start, LineCapacity, text, count, cursor);
            return true;
        }

        private void Visible(float now, out string text, out int count, out bool cursor)
        {
            if (!changing)
            {
                text = to;
                count = to.Length;
                cursor = false;
                return;
            }

            float t = now - changedAt;
            float eraseSeconds = fromCount / eraseSpeed;
            if (t < eraseSeconds)
            {
                text = from;
                count = fromCount - Mathf.FloorToInt(t * eraseSpeed);
                cursor = true;
                return;
            }

            int typed = Mathf.FloorToInt((t - eraseSeconds) * typeSpeed);
            if (typed >= to.Length)
            {
                changing = false;
                text = to;
                count = to.Length;
                cursor = false;
                return;
            }

            text = to;
            count = typed;
            cursor = true;
        }
    }
}
