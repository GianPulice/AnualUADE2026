using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Open/close animator for a "browser tab" style panel (Chrome tab):
/// the panel grows on one axis (width by default) from 0 to its final size, anchored to the
/// point where the tab "is born", while the inner content fades in so it does not look
/// deformed while growing. On close the animation is reversed (fade-out + collapse) and the
/// GameObject is deactivated at the end.
///
/// Generic and reusable: add it to the RectTransform of the panel that grows. Meant to be
/// hooked from the inventory Controller/Model (InventoryManagerUI), but it does NOT know
/// about it: it only exposes Open()/Close() + callbacks. See the integration note at the end.
///
/// ── About the growth origin (where the tab "is born") ────────────────────────────────────
/// The axis is controlled with <see cref="growAxis"/>; the anchoring edge comes from the
/// RectTransform's PIVOT:
///   - pivot.x = 0   -> grows to the right (born from the left edge)
///   - pivot.x = 1   -> grows to the left
///   - pivot.x = 0.5 -> grows in both directions
/// (analogous with pivot.y for vertical growth). The pivot is the origin in both Scale
/// and SizeDelta mode.
///
/// Useful note: on a rect with stretch anchors and sizeDelta (0,0) — like the inventory's
/// LAYOUT panel — changing the pivot does NOT alter the layout (it still fills the parent);
/// it only moves the scaling origin. It is safe to touch it to choose where the tab is born.
///
/// For a panel that should look like it comes out of a specific widget rather than out of its own
/// pivot — the note pop-up unfolding from its button, the way a window restores from a taskbar —
/// assign <see cref="originRect"/> and the panel travels as it grows.
///
/// See <see cref="GrowMode"/>: with stretch anchors you must use Scale, because sizeDelta
/// there is an offset against the parent's edges and not the real width.
///
/// ── Adapting it to a notebook-page "flip" (RE2 Remake) ───────────────────────────────────
/// Instead of animating sizeDelta, replace ApplyGrow() with a Y rotation around a side pivot:
/// LeanTween.value(host, 90f, 0f, dur).setOnUpdate(a => panelRect.localEulerAngles =
/// new Vector3(0, a, 0)) combined with the fade. The rest of the structure (reentrancy,
/// ignoreTimeScale, callbacks) is reused as is.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[AddComponentMenu("WIRED/UI Animations/Inventory Tab Panel Animator")]
public class InventoryTabPanelAnimator : MonoBehaviour
{
    public enum GrowAxis { Horizontal, Vertical, Both }

    /// <summary>
    /// How the panel grows:
    ///   Scale     — animates localScale. ALWAYS works, even with stretch anchors.
    ///               The content is squeezed while growing (the fade covers it). Safe default.
    ///   SizeDelta — animates sizeDelta (real width). Gives a more faithful "tab" (the content
    ///               is not deformed), but REQUIRES non-stretch anchors on the growing axis.
    /// </summary>
    public enum GrowMode { Scale, SizeDelta }

    [Header("References")]
    [Tooltip("Panel that grows. If left empty, this GameObject's RectTransform is used.")]
    [SerializeField] private RectTransform panelRect;
    [Tooltip("CanvasGroup of the inner CONTENT (not the panel), for fade-in/out while growing.")]
    [SerializeField] private CanvasGroup contentGroup;

    [Header("Growth")]
    [Tooltip("Scale = safe with stretch anchors. SizeDelta = real width, requires non-stretch anchors.")]
    [SerializeField] private GrowMode growMode = GrowMode.Scale;
    [SerializeField] private GrowAxis growAxis = GrowAxis.Horizontal;
    [Tooltip("Minimum scale while collapsed (Scale mode). 0 = fully closed tab.")]
    [SerializeField] private float collapsedScale = 0f;
    [Tooltip("Minimum axis size while collapsed (SizeDelta mode).")]
    [SerializeField] private float collapsedSize = 0f;

    [Header("Growth origin")]
    [Tooltip("Optional. If assigned, the panel also TRAVELS from this rect's centre to its authored " +
             "position while it grows, so it reads as coming out of that widget — the way a window " +
             "restores from its taskbar button. Leave empty to grow in place from the pivot.")]
    [SerializeField] private RectTransform originRect;

    [Header("Timing / Ease")]
    [SerializeField] private float openDuration  = UITweenDefaults.PanelOpenDuration;
    [SerializeField] private float closeDuration = UITweenDefaults.PanelCloseDuration;
    [SerializeField] private float fadeDuration  = UITweenDefaults.PanelFadeDuration;
    [SerializeField] private LeanTweenType openEase  = UITweenDefaults.PanelOpenEase;
    [SerializeField] private LeanTweenType closeEase = UITweenDefaults.PanelCloseEase;

    [Header("Options")]
    [Tooltip("Dejalo en true. El inventario ya no congela el juego, pero el menú de pausa sí " +
             "puede abrirse encima suyo, y una animación en tiempo escalado se queda clavada a " +
             "mitad de camino cuando eso pasa.")]
    [SerializeField] private bool ignoreTimeScale = true;
    [Tooltip("Collapse and hide (SetActive false) the panel in Awake so it starts closed.")]
    [SerializeField] private bool startHidden = true;
    [Tooltip("Optional global override. If assigned, it overrides local durations/eases in Awake.")]
    [SerializeField] private UIAnimationSettingsSO settings;

    [Header("Events (for the Controller: block input, etc.)")]
    public UnityEvent onOpenStarted;
    public UnityEvent onOpened;
    public UnityEvent onCloseStarted;
    public UnityEvent onClosed;

    /// <summary>Code callbacks for the Controller (in addition to the Inspector UnityEvents).</summary>
    public event Action OnOpened;
    public event Action OnClosed;

    /// <summary>true between the start of Open() and the end of Close() (animation included).</summary>
    public bool IsOpen { get; private set; }
    /// <summary>true while an open or close animation is running.</summary>
    public bool IsAnimating { get; private set; }

    private Vector2 expandedSize;   // authored final sizeDelta, captured in Awake (SizeDelta mode)
    private Vector3 baseScale;      // authored final localScale, captured in Awake (Scale mode)
    private Vector2 basePosition;   // authored final anchoredPosition, captured in Awake
    private Vector2 originPosition; // originRect's centre, in this panel's own anchoredPosition space
    private bool hasOrigin;         // false when originRect is unset or unresolvable
    private float growT;            // current growth progress (0 = collapsed, 1 = full)
    private bool initialized;

    private void Awake() => EnsureInitialized();

    /// <summary>
    /// Captures the authored final size and, if startHidden, leaves the panel collapsed (invisible).
    /// Idempotent. It deliberately does NOT deactivate the GameObject: if the panel starts inactive
    /// in the scene, Awake only runs on Open()'s SetActive(true); deactivating here would cut the
    /// tween mid-flight. The panel must be authored at FULL size in the prefab/scene.
    /// </summary>
    private void EnsureInitialized()
    {
        if (initialized) return;
        if (panelRect == null) panelRect = GetComponent<RectTransform>();
        ApplySettings();

        expandedSize = panelRect.sizeDelta;
        baseScale = panelRect.localScale;
        basePosition = panelRect.anchoredPosition;
        initialized = true;

        WarnIfStretchedWithSizeDelta();

        if (startHidden && !IsOpen)
        {
            growT = 0f;
            ApplyGrow(0f);
            if (contentGroup != null) contentGroup.alpha = 0f;
            SetContentInteractable(false);
        }
        else
        {
            growT = 1f;
        }
    }

    private void ApplySettings()
    {
        if (settings == null) return;
        openDuration  = settings.panelOpenDuration;
        closeDuration = settings.panelCloseDuration;
        fadeDuration  = settings.panelFadeDuration;
        openEase      = settings.panelOpenEase;
        closeEase     = settings.panelCloseEase;
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>Opens the tab: grows the width from the current state + content fade-in.</summary>
    public void Open()
    {
        gameObject.SetActive(true); // in case it was deactivated (startHidden or a previous close)
        EnsureInitialized();
        KillTweens();

        // Resolved per open, not cached: the origin widget moves with the layout and with the
        // aspect ratio, and a stale origin sends the panel flying in from the wrong corner.
        CaptureOrigin();

        IsOpen = true;
        IsAnimating = true;
        SetContentInteractable(false); // input blocked during the animation
        onOpenStarted?.Invoke();

        // Growth from the current progress (reentrancy: if it was closing, it starts from there).
        LeanTween.value(panelRect.gameObject, growT, 1f, openDuration)
            .setOnUpdate(ApplyGrow)
            .setEase(openEase)
            .setIgnoreTimeScale(ignoreTimeScale)
            .setOnComplete(HandleOpenComplete);

        // Content fade-in (shorter: it appears once there is already some width, without deforming).
        if (contentGroup != null)
        {
            LeanTween.alphaCanvas(contentGroup, 1f, fadeDuration)
                .setIgnoreTimeScale(ignoreTimeScale);
        }
    }

    /// <summary>Closes the tab: content fade-out + width collapse, then deactivates.</summary>
    public void Close()
    {
        if (!gameObject.activeSelf) return; // already closed
        KillTweens();

        IsAnimating = true;
        SetContentInteractable(false);
        onCloseStarted?.Invoke();

        // Content fade-out (leads: the width collapses in parallel but the content leaves first).
        if (contentGroup != null)
        {
            LeanTween.alphaCanvas(contentGroup, 0f, fadeDuration)
                .setIgnoreTimeScale(ignoreTimeScale);
        }

        // Width collapse from the current progress.
        // NOTE: for "fade-out FIRST and then collapse" (more sequential), add
        //       .setDelay(fadeDuration) to this tween.
        LeanTween.value(panelRect.gameObject, growT, 0f, closeDuration)
            .setOnUpdate(ApplyGrow)
            .setEase(closeEase)
            .setIgnoreTimeScale(ignoreTimeScale)
            .setOnComplete(HandleCloseComplete);
    }

    // ── Completion callbacks ─────────────────────────────────────────────────────

    private void HandleOpenComplete()
    {
        IsAnimating = false;
        SetContentInteractable(true);
        onOpened?.Invoke();
        OnOpened?.Invoke();
    }

    private void HandleCloseComplete()
    {
        IsAnimating = false;
        IsOpen = false;
        gameObject.SetActive(false);
        onClosed?.Invoke();
        OnClosed?.Invoke();
    }

    // ── Core ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Expresses <see cref="originRect"/>'s centre as an anchoredPosition in the panel's own
    /// parent, so ApplyGrow can simply Lerp between the two. Silently does nothing when there is
    /// no origin or no RectTransform parent — the panel then grows in place, as before.
    /// </summary>
    private void CaptureOrigin()
    {
        hasOrigin = false;

        if (originRect == null) return;
        if (!(panelRect.parent is RectTransform parent)) return;

        Vector2 local = parent.InverseTransformPoint(originRect.TransformPoint(originRect.rect.center));

        // The point our anchoredPosition is measured from. Using the midpoint of the anchors keeps
        // this correct for stretched rects too, where min and max differ.
        Rect pr = parent.rect;
        Vector2 anchorReference = new Vector2(
            Mathf.Lerp(pr.xMin, pr.xMax, (panelRect.anchorMin.x + panelRect.anchorMax.x) * 0.5f),
            Mathf.Lerp(pr.yMin, pr.yMax, (panelRect.anchorMin.y + panelRect.anchorMax.y) * 0.5f));

        // anchoredPosition positions the PIVOT, not the centre, so shift by the gap between them.
        Rect r = panelRect.rect;
        Vector2 pivotOffset = new Vector2(
            (panelRect.pivot.x - 0.5f) * r.width,
            (panelRect.pivot.y - 0.5f) * r.height);

        originPosition = local - anchorReference + pivotOffset;
        hasOrigin = true;
    }

    private void ApplyGrow(float t)
    {
        if (panelRect == null) return;
        growT = t;

        if (hasOrigin)
            panelRect.anchoredPosition = Vector2.Lerp(originPosition, basePosition, t);

        bool horizontal = growAxis == GrowAxis.Horizontal || growAxis == GrowAxis.Both;
        bool vertical   = growAxis == GrowAxis.Vertical   || growAxis == GrowAxis.Both;

        if (growMode == GrowMode.Scale)
        {
            Vector3 s = baseScale;
            if (horizontal) s.x = Mathf.Lerp(collapsedScale, baseScale.x, t);
            if (vertical)   s.y = Mathf.Lerp(collapsedScale, baseScale.y, t);
            panelRect.localScale = s;
        }
        else
        {
            Vector2 s = expandedSize;
            if (horizontal) s.x = Mathf.Lerp(collapsedSize, expandedSize.x, t);
            if (vertical)   s.y = Mathf.Lerp(collapsedSize, expandedSize.y, t);
            panelRect.sizeDelta = s;
        }
    }

    /// <summary>
    /// In SizeDelta mode with stretch anchors, sizeDelta is NOT the width but an offset against
    /// the parent's edges: the panel would end up with a "final size of 0" and the animation
    /// would not be visible. This warning exists because that is exactly the case of the
    /// inventory's LAYOUT panel.
    /// </summary>
    private void WarnIfStretchedWithSizeDelta()
    {
        if (growMode != GrowMode.SizeDelta) return;

        bool stretchX = !Mathf.Approximately(panelRect.anchorMin.x, panelRect.anchorMax.x);
        bool stretchY = !Mathf.Approximately(panelRect.anchorMin.y, panelRect.anchorMax.y);

        bool conflict = ((growAxis == GrowAxis.Horizontal || growAxis == GrowAxis.Both) && stretchX)
                     || ((growAxis == GrowAxis.Vertical   || growAxis == GrowAxis.Both) && stretchY);

        if (conflict)
        {
            Debug.LogWarning(
                $"[InventoryTabPanelAnimator] '{name}': growMode = SizeDelta but the anchors are " +
                $"set to stretch on the {growAxis} axis. sizeDelta does not represent the real size " +
                $"and the animation will not be visible. Use growMode = Scale, or remove the stretch " +
                $"from that axis.", this);
        }
    }

    private void SetContentInteractable(bool value)
    {
        if (contentGroup == null) return;
        contentGroup.interactable = value;
        contentGroup.blocksRaycasts = value;
    }

    private void KillTweens()
    {
        if (panelRect != null) LeanTween.cancel(panelRect.gameObject);
        if (contentGroup != null) LeanTween.cancel(contentGroup.gameObject);
    }

    private void OnDisable() => KillTweens();
    private void OnDestroy() => KillTweens();
}
