using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Which edge a UI element slides in from / out towards.</summary>
public enum SlideDirection { FromLeft, FromRight, FromTop, FromBottom }

/// <summary>
/// Generic, reusable slide transition for interaction prompts, item pickup notifications,
/// subtitles, tooltips, etc. A single component serves different cases depending on the
/// <see cref="SlideDirection"/> passed to it.
///
/// API:
///   void SlideIn(SlideDirection direction);
///   void SlideOut(SlideDirection direction);
///
/// RE look:
///   - "Serious"/persistent elements (the "Press E to..." prompt): easeOutQuad, no overshoot.
///   - Quick feedback (item notification): easeOutBack with low overshoot (feedbackStyle = true).
///
/// The element is placed in the editor AT ITS FINAL VISIBLE POSITION; Awake captures that as
/// its rest position. The hidden position is computed per direction: by default it is
/// auto-computed from the RectTransform size so it goes fully off-screen (or set slideDistance
/// to a value > 0).
///
/// Input-agnostic: driven by public methods, it does not read Input directly.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[AddComponentMenu("WIRED/UI Animations/UI Slide Transition")]
public class UISlideTransition : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Optional CanvasGroup to accompany the slide with a fade. If null, it only moves.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Hidden distance")]
    [Tooltip("Distance in px off-screen. If <= 0, it is auto-computed from the RectTransform size.")]
    [SerializeField] private float slideDistance = -1f;

    [Header("Style")]
    [Tooltip("true = quick feedback with overshoot (notifications). false = serious, no overshoot (prompts).")]
    [SerializeField] private bool feedbackStyle = false;

    [Header("Timing / Ease")]
    [SerializeField] private float inDuration  = UITweenDefaults.SlideInDuration;
    [SerializeField] private float outDuration = UITweenDefaults.SlideOutDuration;
    [Tooltip("Entry ease for serious mode (no overshoot).")]
    [SerializeField] private LeanTweenType seriousInEase = UITweenDefaults.SlideSeriousEase;
    [Tooltip("Entry ease for feedback mode (with overshoot).")]
    [SerializeField] private LeanTweenType feedbackInEase = UITweenDefaults.SlideFeedbackEase;
    [SerializeField] private float feedbackOvershoot = UITweenDefaults.FeedbackOvershoot;
    [Tooltip("Exit ease (retraction). Sharp, no overshoot.")]
    [SerializeField] private LeanTweenType outEase = LeanTweenType.easeInQuad;

    [Header("Auto-hide")]
    [Tooltip("If enabled, after SlideIn it retracts on its own after 'visibleDuration' seconds.")]
    [SerializeField] private bool autoHide = false;
    [SerializeField] private float visibleDuration = UITweenDefaults.DefaultVisibleDuration;

    [Header("Options")]
    [Tooltip("Hide the element in Awake (off-screen + alpha 0) so it starts invisible.")]
    [SerializeField] private bool startHidden = true;
    [Tooltip("Direction used only for the INITIAL hidden state (before the first SlideIn).")]
    [SerializeField] private SlideDirection initialHiddenDirection = SlideDirection.FromBottom;
    [Tooltip("Tick if it lives in a menu/HUD that can run with Time.timeScale = 0.")]
    [SerializeField] private bool ignoreTimeScale = true;
    [Tooltip("Accompany the slide with a CanvasGroup fade (requires canvasGroup assigned).")]
    [SerializeField] private bool fadeWithSlide = true;
    [Tooltip("Deactivate the GameObject when SlideOut finishes.")]
    [SerializeField] private bool deactivateOnHidden = false;
    [Tooltip("Optional global override. If assigned, it overrides local durations/eases in Awake.")]
    [SerializeField] private UIAnimationSettingsSO settings;

    [Header("Events")]
    public UnityEvent onShown;
    public UnityEvent onHidden;
    public event Action OnShown;
    public event Action OnHidden;

    private RectTransform rect;
    private Vector2 shownPos;              // authored visible position, captured in Awake
    private SlideDirection lastDirection;  // so auto-hide/SlideOut without a direction retracts the way it came in

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        ApplySettings();
        shownPos = rect.anchoredPosition; // the element is authored at its VISIBLE rest position
        lastDirection = initialHiddenDirection;

        if (startHidden)
        {
            rect.anchoredPosition = HiddenPositionFor(initialHiddenDirection);
            if (canvasGroup != null) canvasGroup.alpha = 0f;
        }
    }

    private void ApplySettings()
    {
        if (settings == null) return;
        inDuration        = settings.slideInDuration;
        outDuration       = settings.slideOutDuration;
        seriousInEase     = settings.slideSeriousEase;
        feedbackInEase    = settings.slideFeedbackEase;
        feedbackOvershoot = settings.feedbackOvershoot;
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>Slides in from the given edge towards its rest position.</summary>
    public void SlideIn(SlideDirection direction)
    {
        gameObject.SetActive(true);
        KillTweens();

        lastDirection = direction;

        // Place it at the corresponding hidden position and (optionally) start from alpha 0.
        rect.anchoredPosition = HiddenPositionFor(direction);
        if (fadeWithSlide && canvasGroup != null) canvasGroup.alpha = 0f;

        LeanTweenType ease = feedbackStyle ? feedbackInEase : seriousInEase;

        LTDescr move = LeanTween.value(rect.gameObject, rect.anchoredPosition, shownPos, inDuration)
            .setOnUpdate((Vector2 p) => rect.anchoredPosition = p)
            .setEase(ease)
            .setIgnoreTimeScale(ignoreTimeScale)
            .setOnComplete(HandleShown);

        // Overshoot only has a real effect with *Back eases.
        if (feedbackStyle) move.setOvershoot(feedbackOvershoot);

        if (fadeWithSlide && canvasGroup != null)
            LeanTween.alphaCanvas(canvasGroup, 1f, inDuration).setIgnoreTimeScale(ignoreTimeScale);

        if (autoHide)
            LeanTween.delayedCall(rect.gameObject, inDuration + visibleDuration, () => SlideOut(direction))
                .setIgnoreTimeScale(ignoreTimeScale);
    }

    /// <summary>Slides out towards the given edge. Cancels any pending auto-hide.</summary>
    public void SlideOut(SlideDirection direction)
    {
        if (rect == null) return;
        KillTweens();

        lastDirection = direction;
        Vector2 target = HiddenPositionFor(direction);

        LeanTween.value(rect.gameObject, rect.anchoredPosition, target, outDuration)
            .setOnUpdate((Vector2 p) => rect.anchoredPosition = p)
            .setEase(outEase)
            .setIgnoreTimeScale(ignoreTimeScale)
            .setOnComplete(HandleHidden);

        if (fadeWithSlide && canvasGroup != null)
            LeanTween.alphaCanvas(canvasGroup, 0f, outDuration).setIgnoreTimeScale(ignoreTimeScale);
    }

    /// <summary>Retracts the way it came in (handy for closing without tracking the direction outside).</summary>
    public void SlideOut() => SlideOut(lastDirection);

    // ── Callbacks ────────────────────────────────────────────────────────────────

    private void HandleShown()
    {
        onShown?.Invoke();
        OnShown?.Invoke();
    }

    private void HandleHidden()
    {
        if (deactivateOnHidden) gameObject.SetActive(false);
        onHidden?.Invoke();
        OnHidden?.Invoke();
    }

    // ── Core ──────────────────────────────────────────────────────────────────────

    private Vector2 HiddenPositionFor(SlideDirection direction)
    {
        // Effective distance: auto from the rect size if slideDistance <= 0.
        float horizontal = slideDistance > 0f ? slideDistance : rect.rect.width;
        float vertical   = slideDistance > 0f ? slideDistance : rect.rect.height;

        return direction switch
        {
            SlideDirection.FromLeft   => shownPos + Vector2.left  * horizontal,
            SlideDirection.FromRight  => shownPos + Vector2.right * horizontal,
            SlideDirection.FromTop    => shownPos + Vector2.up    * vertical,
            SlideDirection.FromBottom => shownPos + Vector2.down  * vertical,
            _                         => shownPos
        };
    }

    private void KillTweens()
    {
        if (rect != null) LeanTween.cancel(rect.gameObject); // cancels slide + auto-hide (same host)
    }

    private void OnDisable() => KillTweens();
    private void OnDestroy() => KillTweens();
}
