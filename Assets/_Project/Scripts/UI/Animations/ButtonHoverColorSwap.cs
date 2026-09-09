using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Swaps a Graphic's colour on hover, and back on exit.
///
/// It exists next to <see cref="ButtonHoverSweepEffect"/> rather than inside it: that one slides a
/// bar, this one recolours a graphic, and a button can want either, both, or neither. Pairing them
/// on the same button is how an inverting button is built — the sweep brings the new background in,
/// this flips the label so it stays readable on top of it.
///
/// Works on any Graphic, so it covers Image and TextMeshProUGUI alike (TMP_Text derives from
/// MaskableGraphic). Responds to mouse and to gamepad/keyboard navigation, same as the sweep.
/// </summary>
[AddComponentMenu("WIRED/UI Animations/Button Hover Color Swap")]
public class ButtonHoverColorSwap : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler
{
    [Header("Target")]
    [Tooltip("Graphic to recolour. If left empty, the Graphic on this GameObject is used.")]
    [SerializeField] private Graphic target;

    [Header("Colors")]
    [Tooltip("Leave 'capture from target' on to use whatever colour the graphic is authored with.")]
    [SerializeField] private bool captureNormalFromTarget = true;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color hoverColor = Color.black;

    [Header("Timing")]
    [Tooltip("0 = instant swap. A short fade reads softer under a sweeping bar.")]
    [SerializeField] private float duration = UITweenDefaults.HoverInDuration;
    [Tooltip("Tick if this button lives in a menu that runs with Time.timeScale = 0.")]
    [SerializeField] private bool ignoreTimeScale = true;

    private bool initialized;

    private void Awake()
    {
        if (target == null) target = GetComponent<Graphic>();

        if (target == null)
        {
            Debug.LogWarning($"[ButtonHoverColorSwap] '{name}': no target Graphic.", this);
            enabled = false;
            return;
        }

        if (captureNormalFromTarget) normalColor = target.color;
        target.color = normalColor;
        initialized = true;
    }

    // ── Pointer and navigation (gamepad/keyboard) events ──────────────────────

    public void OnPointerEnter(PointerEventData eventData) => Swap(hovered: true);
    public void OnPointerExit(PointerEventData eventData) => Swap(hovered: false);
    public void OnSelect(BaseEventData eventData) => Swap(hovered: true);
    public void OnDeselect(BaseEventData eventData) => Swap(hovered: false);

    // ── Core ──────────────────────────────────────────────────────────────────

    private void Swap(bool hovered)
    {
        if (!initialized) return;

        // Same reason as the sweep: fast hover/unhover must not stack tweens on one property.
        LeanTween.cancel(gameObject);

        Color to = hovered ? hoverColor : normalColor;

        if (duration <= 0f)
        {
            target.color = to;
            return;
        }

        Color from = target.color;
        LeanTween.value(gameObject, 0f, 1f, duration)
            .setOnUpdate((float t) => { if (target != null) target.color = Color.Lerp(from, to, t); })
            .setIgnoreTimeScale(ignoreTimeScale);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private void OnDisable()
    {
        LeanTween.cancel(gameObject);
        if (initialized && target != null) target.color = normalColor;
    }

    private void OnDestroy() => LeanTween.cancel(gameObject);
}
