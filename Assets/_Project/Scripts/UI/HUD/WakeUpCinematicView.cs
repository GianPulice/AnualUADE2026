using UnityEngine;

/// <summary>
/// The opening cinematic, driven by ARC_01a (<see cref="ArchitectVoiceController"/>):
///
///   1. The level starts on black. Camera look input is locked (<see cref="WakeUpCinematicEvents"/>).
///   2. ARC_01a starts: its first subtitle page is read on the black screen.
///   3. When the second page starts, the eyes open (two lids + a dim that clears), the player's
///      stand-up trigger fires and the camera pan starts on the player's right (<see cref="WakeUpCameraPan"/>).
///   4. The pan is timed to reach the default framing on the frame ARC_01a ends, which is also the
///      frame the controller gives movement back.
///   5. The input hint (<see cref="InputHintView"/>) shows after ARC_01a or after ARC_01b (<see cref="hintMoment"/>).
///   6. At any point until control comes back, the skip key (<see cref="SO_WakeUpCinematicConfig"/>)
///      jumps to the end and cuts both lines. <see cref="WakeUpSkipPromptView"/> tells the player.
///
/// Everything is keyed to the line's own timing (pages are spread over its speaking time, see
/// <see cref="ArchitectLinePages"/>), so recording a voice clip or retiming the bank re-syncs the
/// whole cinematic without touching this component. The page break goes in the bank text:
/// "There's a device on your body. | Three modules. ...".
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

    [Header("Player")]
    [Tooltip("Animator trigger fired on the player when the eyes open. Skipped if the controller " +
             "has no trigger with this name.")]
    [SerializeField] private string standUpTrigger = "standUp";

    [Header("Input hint")]
    [SerializeField] private HintMoment hintMoment = HintMoment.AfterWakeUp1;
    [SerializeField] private bool hintEnabled = true;
    [Tooltip("Shown through InputHintView, once per run like any other hint.")]
    [SerializeField] private InputHint hint = new InputHint { keys = "WASD", action = "move", dismissOnMove = true };

    private enum State { Off, Covering, Playing, WaitingForHint }

    private State state = State.Off;
    private float lineStartTime;
    private float lineDuration;
    private float openStart;
    private bool eyesOpening;

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

        // Never leave the camera locked behind a HUD that went away.
        if (state == State.Covering || state == State.Playing) WakeUpCinematicEvents.Finish();
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
    }

    private void Update()
    {
        if ((state == State.Covering || state == State.Playing) && SkipPressed())
        {
            Skip();
            return;
        }

        switch (state)
        {
            case State.Covering:
                // The wake-up was skipped (no text in the bank): do not leave the player on black.
                ArchitectVoiceController voice = ArchitectVoiceController.Instance;
                if (voice == null || voice.IsWakeUpDone) EndCinematic(showHint: false);
                break;

            case State.Playing:
                TickEyes();
                break;

            case State.WaitingForHint:
                ArchitectVoiceController v = ArchitectVoiceController.Instance;
                if (v == null || (v.IsWakeUpDone && !v.IsSpeaking)) ShowHint();
                break;
        }
    }

    private void HandleLineStarted(ArchitectLinePlayback playback)
    {
        if (state != State.Covering || playback.Id != ArchitectLineID.WakeUpMoment1) return;

        string[] pages = ArchitectLinePages.Split(playback.Text);
        float[] starts = ArchitectLinePages.StartTimes(playback, pages);

        lineStartTime = Time.unscaledTime;
        lineDuration = playback.Duration;

        // The eyes open with the second page, so the first one is read on black. Capped so there is
        // always some pan left, however the bank is timed.
        openStart = pages.Length > 1 ? starts[1] : fallbackBlackHold;
        openStart = Mathf.Min(openStart, lineDuration * 0.8f);

        eyesOpening = false;
        state = State.Playing;
    }

    private void HandleLineEnded(bool interrupted)
    {
        if (state != State.Playing) return;
        EndCinematic(showHint: !interrupted);
    }

    private void TickEyes()
    {
        float elapsed = Time.unscaledTime - lineStartTime;
        if (elapsed < openStart) return;

        if (!eyesOpening)
        {
            eyesOpening = true;
            FireStandUp();

            // Measured from now and not from openStart: if this frame came late, the pan still has
            // to land on the line's last frame, not after it.
            WakeUpCinematicEvents.StartPan(lineDuration - elapsed);
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

        return Input.GetKeyDown(config != null ? config.SkipKey : KeyCode.F);
    }

    /// <summary>
    /// Jumps to the end: the camera lands on its end framing, control comes back and neither wake-up
    /// line keeps playing. The hint still shows, since the player has not moved yet.
    /// </summary>
    private void Skip()
    {
        // Skipped before the eyes opened: the player is still lying down.
        if (!eyesOpening) FireStandUp();

        // The cinematic ends first, so the LineEnded(interrupted) the skip raises finds it already Off.
        EndCinematic(showHint: true);

        ArchitectVoiceController voice = ArchitectVoiceController.Instance;
        if (voice != null) voice.SkipWakeUp();
    }

    private void EndCinematic(bool showHint)
    {
        SetVisible(false);
        WakeUpCinematicEvents.Finish();

        if (!showHint)
        {
            state = State.Off;
            return;
        }

        if (hintMoment == HintMoment.AfterWakeUp1) ShowHint();
        else state = State.WaitingForHint;
    }

    private void ShowHint()
    {
        state = State.Off;
        if (hintEnabled) InputHintEvents.Show(hint);
    }

    private void FireStandUp()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        Animator animator = player != null ? player.AnimController : null;
        if (animator == null || string.IsNullOrEmpty(standUpTrigger)) return;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == standUpTrigger && parameter.type == AnimatorControllerParameterType.Trigger)
            {
                animator.SetTrigger(standUpTrigger);
                return;
            }
        }
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
