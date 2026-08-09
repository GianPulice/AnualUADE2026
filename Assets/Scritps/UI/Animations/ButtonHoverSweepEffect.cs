using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// "Sweep" effect for button hover, Resident Evil menu style: a child image
/// (selection bar or icon) slides from left to right towards its rest position when hover
/// starts, and snaps back on exit.
///
/// Generic and reusable: add it to ANY UI GameObject (typically the same one that has the
/// uGUI Button/Selectable) and it only needs a reference to the image that sweeps.
///
/// It responds to both mouse (IPointerEnter/Exit) and gamepad/keyboard navigation
/// (ISelect/IDeselect), so it does not depend on any specific Input system.
///
/// Inspector setup:
///   - sweepTarget: the child image/bar that slides. Leave it positioned in the editor
///     AT ITS FINAL (visible rest) POSITION; Awake captures that X as the target and starts
///     the object at the hidden position.
///   - If the bar is not inside a RectMask2D/Mask, use useExplicitHiddenX or a large
///     hiddenOffsetX so it really starts outside the button.
/// </summary>
[AddComponentMenu("WIRED/UI Animations/Button Hover Sweep Effect")]
public class ButtonHoverSweepEffect : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler
{
    [Header("Target")]
    [Tooltip("Child image that performs the sweep (selection bar/icon). Requires a RectTransform.")]
    [SerializeField] private RectTransform sweepTarget;

    [Header("Hidden position (anchoredPosition.x)")]
    [Tooltip("If enabled, uses 'hiddenX' as an absolute value. Otherwise, hidden = rest position + hiddenOffsetX.")]
    [SerializeField] private bool useExplicitHiddenX = false;
    [SerializeField] private float hiddenX = -60f;
    [Tooltip("Offset relative to the rest position. Negative = starts on the left and sweeps right.")]
    [SerializeField] private float hiddenOffsetX = -60f;

    [Header("Timing / Ease")]
    [SerializeField] private float inDuration  = UITweenDefaults.HoverInDuration;
    [SerializeField] private float outDuration = UITweenDefaults.HoverOutDuration;
    [SerializeField] private LeanTweenType inEase  = UITweenDefaults.HoverInEase;
    [SerializeField] private LeanTweenType outEase = UITweenDefaults.HoverOutEase;

    [Header("Options")]
    [Tooltip("Tick if this button lives in a menu that runs with Time.timeScale = 0 (pause/inventory).")]
    [SerializeField] private bool ignoreTimeScale = true;
    [Tooltip("Optional global override. If assigned, it overrides local durations/eases in Awake.")]
    [SerializeField] private UIAnimationSettingsSO settings;

    private float shownX;         // authored rest position, captured in Awake
    private float restingHiddenX; // computed hidden position
    private bool initialized;

    private void Awake()
    {
        if (sweepTarget == null)
        {
            Debug.LogWarning($"[ButtonHoverSweepEffect] '{name}': sweepTarget is not assigned.", this);
            enabled = false;
            return;
        }

        ApplySettings();

        shownX = sweepTarget.anchoredPosition.x;
        restingHiddenX = useExplicitHiddenX ? hiddenX : shownX + hiddenOffsetX;

        SetX(restingHiddenX); // starts hidden
        initialized = true;
    }

    private void ApplySettings()
    {
        if (settings == null) return;
        inDuration  = settings.hoverInDuration;
        outDuration = settings.hoverOutDuration;
        inEase      = settings.hoverInEase;
        outEase     = settings.hoverOutEase;
    }

    // ── Pointer and navigation (gamepad/keyboard) events ──────────────────────

    public void OnPointerEnter(PointerEventData eventData) => PlaySweep(show: true);
    public void OnPointerExit(PointerEventData eventData)  => PlaySweep(show: false);
    public void OnSelect(BaseEventData eventData)          => PlaySweep(show: true);
    public void OnDeselect(BaseEventData eventData)        => PlaySweep(show: false);

    // ── Core ──────────────────────────────────────────────────────────────────

    private void PlaySweep(bool show)
    {
        if (!initialized) return;

        // Cancel any sweep in progress: fast hover/unhover does not stack tweens.
        LeanTween.cancel(sweepTarget.gameObject);

        float to           = show ? shownX : restingHiddenX;
        float dur          = show ? inDuration : outDuration;
        LeanTweenType ease = show ? inEase : outEase;

        // Reversible tween on anchoredPosition.x (same tween, different target).
        LeanTween.value(sweepTarget.gameObject, sweepTarget.anchoredPosition.x, to, dur)
            .setOnUpdate(SetX)
            .setEase(ease)
            .setIgnoreTimeScale(ignoreTimeScale);
    }

    private void SetX(float x)
    {
        if (sweepTarget == null) return;
        Vector2 p = sweepTarget.anchoredPosition;
        p.x = x;
        sweepTarget.anchoredPosition = p;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void OnDisable()
    {
        if (sweepTarget == null) return;
        LeanTween.cancel(sweepTarget.gameObject);
        if (initialized) SetX(restingHiddenX); // leave it in a consistent hidden state
    }

    private void OnDestroy()
    {
        if (sweepTarget != null) LeanTween.cancel(sweepTarget.gameObject);
    }
}
