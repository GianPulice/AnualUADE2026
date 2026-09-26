using UnityEngine;

/// <summary>
/// The opening cinematic, driven by ARC_01a (<see cref="ArchitectVoiceController"/>):
///
///   1. The level starts on black. Camera look input is locked (<see cref="WakeUpCinematicEvents"/>).
///      <see cref="standUpLeadSeconds"/> before ARC_01a, the stand-up clip starts, at normal speed.
///   2. ARC_01a starts: its first subtitle page is read on the black screen.
///   3. When the second page starts, the eyes open (two lids + a dim that clears) and the camera pan
///      starts on the player's right (<see cref="WakeUpCameraPan"/>).
///   4. The pan is timed to reach the default framing on the stand-up's last frame, a couple of
///      seconds after ARC_01a ends (ARC_01b is already playing by then). Control comes back there.
///   5. The input hint (<see cref="InputHintView"/>) shows after ARC_01a or after ARC_01b (<see cref="hintMoment"/>).
///   6. At any point until control comes back, the skip key (<see cref="SO_WakeUpCinematicConfig"/>)
///      jumps to the end and cuts both lines. <see cref="WakeUpSkipPromptView"/> tells the player.
///
/// Everything is keyed to the line's own timing (pages are spread over its speaking time, see
/// <see cref="ArchitectLinePages"/>), so recording a voice clip or retiming the bank re-syncs the
/// whole cinematic without touching this component. The page break goes in the bank text:
/// "There's a device on your body. | Three modules. ...".
///
/// With <see cref="OpeningStyle.CameraBoot"/> (the default) the eyes are the player's camera
/// (<see cref="PlayerCameraFeed"/>) and steps 1–3 change:
///   1. The camera is off (black) until a moment before ARC_01a, when it boots
///      (<see cref="PlayerCameraBoot"/>): the picture comes in through static — uncalibrated, an
///      extreme fisheye — under a boot screen with a bar, and the player starts getting up and the
///      camera pan starts with it.
///   2. The boot screen stays up the whole cinematic, ARC_01a's subtitles under it, while the lens
///      calibrates back to normal in jumps as the player gets up.
///   3. On the stand-up's last frame the boot ends and the camera starts recording (its overlay),
///      where control comes back as before.
/// Without a feed on the player's camera it falls back to the eyes.
///
/// Must sit in the HUD above the gameplay overlays but BELOW ArchitectSubtitle, so the first
/// sentence is readable on the black screen.
///
/// SETUP: Tools ▸ Architect ▸ Setup Wake-Up Cinematic.
/// </summary>
public class WakeUpCinematicView : MonoBehaviour
{
    public enum HintMoment
    {
        AfterWakeUp1,   // When ARC_01a ends, the moment control comes back.
        AfterWakeUp2,   // When ARC_01b ends, the whole wake-up dialogue said.
    }

    public enum OpeningStyle
    {
        EyeLids,        // The eyes open over the black on ARC_01a's second page.
        CameraBoot,     // The player's camera boots through the whole cinematic, ARC_01a under it.
    }

    [Header("Opening")]
    [Tooltip("CameraBoot: the picture comes in a moment before ARC_01a, an extreme fisheye under a " +
             "boot screen that stays up the whole cinematic, and the lens calibrates as the player " +
             "gets up (PlayerCameraFeed; timing and look on SO_PlayerCameraFeed). EyeLids: the eyes " +
             "open on ARC_01a's second page. CameraBoot falls back to EyeLids when the player's " +
             "camera has no feed.")]
    [SerializeField] private OpeningStyle opening = OpeningStyle.CameraBoot;

    [Header("References")]
    [SerializeField] private CanvasGroup overlayGroup;
    [SerializeField] private RectTransform topLid;
    [SerializeField] private RectTransform bottomLid;
    [Tooltip("Full-screen black over the part the lids already uncovered: the eyes adjusting.")]
    [SerializeField] private CanvasGroup dimGroup;

    [Header("Eyes opening")]
    [SerializeField, Min(0.1f)] private float eyeOpenDuration = 1.8f;

    [Tooltip("How open the eyes are (0 closed, 1 fully open) over the opening, normalized time. " +
             "The default opens a crack, blinks, and opens.")]
    [SerializeField] private AnimationCurve openCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.28f, 0.22f), new Keyframe(0.42f, 0.04f),
        new Keyframe(0.65f, 0.55f), new Keyframe(1f, 1f));

    [Tooltip("Opacity of the dim when the eyes start opening. It clears over the opening.")]
    [SerializeField, Range(0f, 1f)] private float startDim = 0.85f;

    [Tooltip("Used only when ARC_01a has no page break: seconds on black before the eyes open.")]
    [SerializeField, Min(0f)] private float fallbackBlackHold = 2.5f;

    [Header("Stand-up")]
    [Tooltip("EyeLids only. Seconds BEFORE ARC_01a starts that the stand-up clip starts, at normal " +
             "speed, still on black. The clip (11.4s) then outlasts the line by a couple of seconds, " +
             "and the whole cinematic — camera pan, control, input hint — stretches to its end. " +
             "Capped by the controller's Wake Up Delay: the clip cannot start before the countdown " +
             "does. With the camera boot the player starts getting up when the picture comes in, at " +
             "the start of the boot.")]
    [SerializeField, Min(0f)] private float standUpLeadSeconds = 2f;

    [Header("Input hint")]
    [SerializeField] private HintMoment hintMoment = HintMoment.AfterWakeUp1;
    [SerializeField] private bool hintEnabled = true;
    [Tooltip("Shown through InputHintView, once per run like any other hint.")]
    [SerializeField] private InputHint hint = new InputHint { keys = "WASD", action = "move", dismissOnMove = true };

    // WaitingForStandUp: ARC_01a is over but the player is still getting up; the camera stays
    // locked and keeps panning until the clip ends.
    private enum State { Off, Covering, Playing, WaitingForStandUp, WaitingForHint }

    private State state = State.Off;
    private float lineStartTime;
    private float lineDuration;
    private float openStart;
    private bool eyesOpening;
    private bool standUpFired;

    // Camera boot: latched when the picture comes in, so the opening never changes style halfway.
    private bool cameraBooted;
    private bool panPending;
    private bool lensFollowsTime;
    private float lensStartProgress;
    private float pictureStartTime;

    // Seconds the pan waits for the Animator to report the stand-up's length (a frame or two after
    // the clip starts) before settling for a guess.
    private const float PanLengthWait = 0.3f;

    // Without a stand-up to follow, the lens calibrates and the pan lands over this long, or over the
    // boot plus ARC_01a once the line has started.
    private const float LensFallbackSeconds = 10f;

    /// <summary>The camera boots this time: asked for, and the player's camera has a feed.</summary>
    private bool UsesCameraBoot => opening == OpeningStyle.CameraBoot && PlayerCameraFeed.IsAvailable;

    private void Awake()
    {
        ArchitectEvents.OnLineStarted += HandleLineStarted;
        ArchitectEvents.OnLineEnded += HandleLineEnded;
        SetVisible(false);
    }

    private void OnDestroy()
    {
        ArchitectEvents.OnLineStarted -= HandleLineStarted;
        ArchitectEvents.OnLineEnded -= HandleLineEnded;

        // Never leave the camera locked — or switched off — behind a HUD that went away.
        if (state == State.Covering || state == State.Playing || state == State.WaitingForStandUp)
        {
            WakeUpCinematicEvents.Finish();
            PlayerCameraBoot.End(cut: true);
        }
    }

    // Start, not Awake: the controller registers its Instance in its own Awake.
    private void Start()
    {
        ArchitectVoiceController voice = ArchitectVoiceController.Instance;
        if (voice == null || !voice.PlaysWakeUpOnStart || voice.IsWakeUpDone) return;

        state = State.Covering;
        SetVisible(true);
        SetOpen(0f, 1f);
        WakeUpCinematicEvents.LockCamera();

        // The camera starts off. The lids stay up until a frame confirms the feed is there to draw
        // the black — the player's scene may load after this one.
        if (opening == OpeningStyle.CameraBoot) PlayerCameraBoot.SetOff();
    }

    private void Update()
    {
        if ((state == State.Covering || state == State.Playing) && SkipPressed())
        {
            Skip();
            return;
        }

        // ARC_01a ends before the stand-up does: the skip still cuts what is left of it, and the
        // camera and the hint follow on the next frame.
        if ((state == State.WaitingForStandUp || state == State.WaitingForHint) && IsPlayerWakingUp() &&
            SkipPressed())
        {
            PlayerRegistry.Current.SkipStandUp();
            PlayerCameraBoot.End(cut: true);
            return;
        }

        switch (state)
        {
            case State.Covering:
                // The wake-up was skipped (no text in the bank): do not leave the player on black.
                ArchitectVoiceController voice = ArchitectVoiceController.Instance;
                if (voice == null || voice.IsWakeUpDone)
                {
                    EndCinematic(showHint: false, cut: true);
                    break;
                }

                // The clip starts standUpLeadSeconds before ARC_01a, while the countdown runs. With
                // the camera boot it starts with the picture instead (TickBoot).
                float untilLine = voice.WakeUpSecondsUntilLine;
                if (!standUpFired && !UsesCameraBoot && !LoadingScreen.IsLoading &&
                    untilLine >= 0f && untilLine <= standUpLeadSeconds)
                    FireStandUp();

                if (opening == OpeningStyle.CameraBoot) TickBoot(untilLine);
                break;

            case State.Playing:
                if (cameraBooted) TickLens();
                else TickEyes();
                break;

            case State.WaitingForStandUp:
                if (cameraBooted) TickLens();

                // On its feet: the pan has landed on the nape with it, control comes back.
                if (!IsPlayerWakingUp()) EndCinematic(showHint: true);
                break;

            case State.WaitingForHint:
                if (IsHintMomentReached() && PlayerCanMove()) ShowHint();
                break;
        }
    }

    private void HandleLineStarted(ArchitectLinePlayback playback)
    {
        if (state != State.Covering || playback.Id != ArchitectLineID.WakeUpMoment1) return;

        lineStartTime = Time.unscaledTime;
        lineDuration = playback.Duration;
        state = State.Playing;

        // The boot goes on under the line (its subtitles sit below the boot screen) until the
        // player is on their feet. If the countdown was too short for the picture to be in
        // already, it comes in right now.
        if (UsesCameraBoot)
        {
            if (!cameraBooted) ComeOnAir();
            return;
        }

        // The countdown was shorter than the lead (or skipped past it): get up now at the latest.
        if (!standUpFired) FireStandUp();

        // The eyes (also the camera boot's fallback): the camera was never going to draw.
        PlayerCameraBoot.End(cut: false);

        string[] pages = ArchitectLinePages.Split(playback.Text);
        float[] starts = ArchitectLinePages.StartTimes(playback, pages);

        // The eyes open with the second page, so the first one is read on black. Capped so there is
        // always some pan left, however the bank is timed.
        openStart = pages.Length > 1 ? starts[1] : fallbackBlackHold;
        openStart = Mathf.Min(openStart, lineDuration * 0.8f);

        eyesOpening = false;
    }

    private void HandleLineEnded(bool interrupted)
    {
        if (state != State.Playing) return;

        if (interrupted)
        {
            EndCinematic(showHint: false, cut: true);
            return;
        }

        // The stand-up outlasts the line: the eyes are long open, but the camera keeps panning
        // and control waits for the clip's last frame.
        SetVisible(false);
        if (IsPlayerWakingUp()) state = State.WaitingForStandUp;
        else EndCinematic(showHint: true);
    }

    // ── Camera boot ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// While the countdown to ARC_01a runs: the camera is off until
    /// <see cref="PlayerCameraFeed.PictureLeadSeconds"/> before the line, when the picture comes in
    /// — uncalibrated, under the boot screen — and the player starts getting up with it. The boot
    /// then runs the whole cinematic (<see cref="TickLens"/>). The lids cover only while no feed is
    /// there to draw any of it.
    /// </summary>
    private void TickBoot(float untilLine)
    {
        bool feed = PlayerCameraFeed.IsAvailable;
        SetVisible(!feed && !cameraBooted);
        if (!feed) return;

        if (!cameraBooted)
        {
            // Past the countdown with ARC_01a held back (a menu open) counts as time to come in.
            bool due = !LoadingScreen.IsLoading &&
                       (untilLine < 0f || untilLine <= PlayerCameraFeed.PictureLeadSeconds);
            if (!due)
            {
                PlayerCameraBoot.SetOff();
                return;
            }
            ComeOnAir();
        }

        TickLens();
    }

    /// <summary>
    /// The picture comes in: the player starts getting up and the camera pan starts, both landing
    /// on the stand-up's last frame, where the lens is set.
    /// </summary>
    private void ComeOnAir()
    {
        cameraBooted = true;
        pictureStartTime = Time.unscaledTime;
        SetVisible(false);

        if (!standUpFired) FireStandUp();

        // The lens is set when the player is on their feet, so it follows the stand-up from here.
        // Without one running, the clock instead.
        PlayerStateManager player = PlayerRegistry.Current;
        lensFollowsTime = player == null || !player.IsWakeUpStandingUp;
        lensStartProgress = lensFollowsTime ? 0f : player.StandUpProgress;

        panPending = true;
        TryStartPan();
    }

    /// <summary>
    /// How far the wake-up is, picture in to on their feet: the boot's bar and the lens both follow
    /// it, so the boot ends — and the camera starts recording — with the stand-up.
    /// </summary>
    private void TickLens()
    {
        TryStartPan();

        PlayerStateManager player = PlayerRegistry.Current;
        float progress;
        if (player != null && player.IsWakeUpStandingUp)
            progress = Mathf.InverseLerp(lensStartProgress, 1f, player.StandUpProgress);
        else if (lensFollowsTime)
            progress = (Time.unscaledTime - pictureStartTime) / LensSecondsWithoutStandUp();
        else
            progress = 1f;   // On their feet.

        PlayerCameraBoot.SetBooting(progress);
        PlayerCameraBoot.SetWakeProgress(progress);
    }

    /// <summary>Without a stand-up: the boot plus ARC_01a once the line is known, a guess before.</summary>
    private float LensSecondsWithoutStandUp() =>
        lineDuration > 0f ? Mathf.Max(lineStartTime - pictureStartTime + lineDuration, 0.1f) : LensFallbackSeconds;

    /// <summary>
    /// The pan lands on the nape when the player is on their feet — the stand-up's end. Its length is
    /// only known once the Animator reports the clip, so it waits for that a moment; without a
    /// stand-up, it lands with the lens.
    /// </summary>
    private void TryStartPan()
    {
        if (!panPending) return;

        PlayerStateManager player = PlayerRegistry.Current;
        bool standingUp = player != null && player.IsWakeUpStandingUp;
        float elapsed = Time.unscaledTime - pictureStartTime;
        if (standingUp && player.StandUpSecondsLeft <= 0f && elapsed < PanLengthWait) return;

        panPending = false;
        WakeUpCinematicEvents.StartPan(standingUp && player.StandUpSecondsLeft > 0f
            ? player.StandUpSecondsLeft
            : LensSecondsWithoutStandUp() - elapsed);
    }

    private void TickEyes()
    {
        float elapsed = Time.unscaledTime - lineStartTime;
        if (elapsed < openStart) return;

        if (!eyesOpening)
        {
            eyesOpening = true;

            // The pan lands on the nape when the player is on its feet — the stand-up's end, a
            // couple of seconds after the line. Without a stand-up running, on the line's end.
            // Measured from now, so a late frame does not push the landing past either.
            PlayerStateManager player = PlayerRegistry.Current;
            float panSeconds = player != null && player.IsWakeUpStandingUp && player.StandUpSecondsLeft > 0f
                ? player.StandUpSecondsLeft
                : lineDuration - elapsed;
            WakeUpCinematicEvents.StartPan(panSeconds);
        }

        float progress = Mathf.Clamp01((elapsed - openStart) / eyeOpenDuration);
        SetOpen(Mathf.Clamp01(openCurve.Evaluate(progress)), Mathf.Lerp(startDim, 0f, progress));

        if (progress >= 1f) SetVisible(false);
    }

    // Legacy input, like WakeUpCameraPan: the project runs both input backends.
    private static bool SkipPressed()
    {
        ArchitectVoiceController voice = ArchitectVoiceController.Instance;
        SO_WakeUpCinematicConfig config = voice != null ? voice.WakeUpConfig : null;
        if (config != null && !config.Skippable) return false;
        if (PauseManager.IsGameplayInputBlocked) return false;

        // The cinematic is already set up behind the loading screen, but a skip there would throw
        // away a cinematic the player never got to see.
        if (LoadingScreen.IsLoading) return false;

        return Input.GetKeyDown(config != null ? config.SkipKey : KeyCode.F);
    }

    /// <summary>
    /// Jumps to the end: the camera lands on its end framing, control comes back and neither wake-up
    /// line keeps playing. The hint still shows, since the player has not moved yet.
    /// </summary>
    private void Skip()
    {
        // Lying down or halfway up: straight to the end of the stand-up, control comes back now.
        PlayerStateManager player = PlayerRegistry.Current;
        if (player != null) player.SkipStandUp();

        // The cinematic ends first, so the LineEnded(interrupted) the skip raises finds it already Off.
        EndCinematic(showHint: true, cut: true);

        ArchitectVoiceController voice = ArchitectVoiceController.Instance;
        if (voice != null) voice.SkipWakeUp();
    }

    /// <param name="cut">The cinematic did not play out: the camera jumps to its normal picture
    /// (under a burst of static) instead of having calibrated.</param>
    private void EndCinematic(bool showHint, bool cut = false)
    {
        SetVisible(false);
        WakeUpCinematicEvents.Finish();
        PlayerCameraBoot.End(cut);
        panPending = false;

        if (!showHint)
        {
            state = State.Off;
            return;
        }

        // Not shown here even for AfterWakeUp1: ARC_01a can end while the player is still getting
        // up, and "move" on screen while it cannot is a lie. WaitingForHint holds it until the
        // player is actually free — right away after a skip, which drops the stand-up.
        state = State.WaitingForHint;
    }

    private bool IsHintMomentReached()
    {
        if (hintMoment == HintMoment.AfterWakeUp1) return true;

        ArchitectVoiceController voice = ArchitectVoiceController.Instance;
        return voice == null || (voice.IsWakeUpDone && !voice.IsSpeaking);
    }

    /// <summary>The player is still in the level-start stand-up.</summary>
    public static bool IsPlayerWakingUp()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        return player != null && player.IsWakeUpStandingUp;
    }

    /// <summary>Standing, not locked by the wake-up, not mid stand-up: the player can move.</summary>
    private static bool PlayerCanMove()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        return player == null || !player.IsImmobilized;
    }

    private void ShowHint()
    {
        state = State.Off;
        if (hintEnabled) InputHintEvents.Show(hint);
    }

    /// <summary>
    /// The player has been lying on the floor since the camera was locked (it picks that up on its
    /// own) and starts getting up now, at the clip's normal speed, still behind the black.
    /// </summary>
    private void FireStandUp()
    {
        standUpFired = true;
        PlayerStateManager player = PlayerRegistry.Current;
        if (player != null) player.PlayStandUp(PlayerStateManager.EStandUp.Init);
    }

    /// <param name="open">0 = lids meet in the middle, 1 = lids off screen.</param>
    private void SetOpen(float open, float dim)
    {
        if (topLid != null)
        {
            topLid.anchorMin = new Vector2(0f, 0.5f + 0.5f * open);
            topLid.anchorMax = Vector2.one;
        }

        if (bottomLid != null)
        {
            bottomLid.anchorMin = Vector2.zero;
            bottomLid.anchorMax = new Vector2(1f, 0.5f - 0.5f * open);
        }

        if (dimGroup != null) dimGroup.alpha = dim;
    }

    private void SetVisible(bool visible)
    {
        if (overlayGroup == null) return;
        overlayGroup.alpha = visible ? 1f : 0f;
        overlayGroup.interactable = false;
        overlayGroup.blocksRaycasts = false;
    }
}
