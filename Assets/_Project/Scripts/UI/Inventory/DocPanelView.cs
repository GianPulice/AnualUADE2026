using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VIEW of the note panel that shows the selected Text item's document inside the inventory.
///
/// One job: show a note laid out as a message and get out of the way. It owns the title bar, the
/// message header (subject and summary, above the body), the body, the scroll and its own close (X)
/// button, and delegates the show/hide animation to the
/// <see cref="InventoryTabPanelAnimator"/> on the same GameObject — the panel grows in and
/// collapses through the reverse animation.
///
/// It does NOT decide when to show or hide. <see cref="ItemDetailView"/> drives it from the
/// selection: a Text item calls <see cref="Show"/>, anything else (or no selection) calls
/// <see cref="Hide"/>. There is no open/close state of its own to unwind — the X only raises
/// <see cref="OnCloseRequested"/>, and the owner turns that into "clear the selection", exactly
/// what ESC does, so the panel can never disagree with what is selected.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[AddComponentMenu("WIRED/UI/Doc Panel View")]
public class DocPanelView : MonoBehaviour
{
    [Header("Content")]
    [SerializeField] private TextMeshProUGUI titleText;
    [Tooltip("Message header: the subject line, above the body.")]
    [SerializeField] private TextMeshProUGUI subjectText;
    [Tooltip("Message header: the item description, under the subject. Hidden when empty.")]
    [SerializeField] private TextMeshProUGUI summaryText;
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private ScrollRect scrollRect;
    [Tooltip("The ScrollRect's viewport. Used to decide whether the text overflows.")]
    [SerializeField] private RectTransform viewport;

    [Header("Close")]
    [Tooltip("The X inside the panel. It only raises OnCloseRequested — it does not hide by itself. " +
             "Optional: deactivate the button's GameObject to have no X at all.")]
    [SerializeField] private Button closeButton;

    [Header("Animation")]
    [Tooltip("If left empty, the animator on this GameObject is used.")]
    [SerializeField] private InventoryTabPanelAnimator animator;

    /// <summary>Raised by the X button. The owner decides what closing means.</summary>
    public event Action OnCloseRequested;

    /// <summary>
    /// True from Show() to Hide(). The animation is deliberately not part of the answer: a
    /// following Show() must know at once whether to animate in or just swap the text.
    /// </summary>
    public bool IsOpen { get; private set; }

    private bool wired;

    private void Awake() => EnsureWired();

    private void OnDestroy()
    {
        if (closeButton != null) closeButton.onClick.RemoveListener(RaiseCloseRequested);
    }

    /// <summary>
    /// Idempotent, and deliberately not only in Awake: the panel starts inactive, so Awake does
    /// not run until the first Show() activates the GameObject — but the content is written
    /// before that.
    /// </summary>
    private void EnsureWired()
    {
        if (wired) return;
        wired = true;

        if (animator == null) animator = GetComponent<InventoryTabPanelAnimator>();
        if (closeButton != null) closeButton.onClick.AddListener(RaiseCloseRequested);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the document. Hidden, the panel animates in; already showing another document
    /// (the selection moved from one Text item to another), only the text is swapped and the
    /// scroll goes back to the top — no close/open flicker.
    /// </summary>
    public void Show(string title, string subject, string summary, string body)
    {
        EnsureWired();

        if (titleText != null) titleText.text = title;
        if (subjectText != null) subjectText.text = subject;
        if (summaryText != null)
        {
            summaryText.text = summary;
            summaryText.gameObject.SetActive(!string.IsNullOrEmpty(summary));
        }
        if (bodyText != null) bodyText.text = body;

        if (!IsOpen)
        {
            IsOpen = true;

            if (animator != null) animator.Open();
            else gameObject.SetActive(true);
        }

        ResetScroll().Forget();
    }

    /// <summary>Folds the panel through the reverse animation. No-op when it is already hidden.</summary>
    public void Hide()
    {
        EnsureWired();
        IsOpen = false;

        if (animator != null) animator.Close();
        else gameObject.SetActive(false);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private void RaiseCloseRequested() => OnCloseRequested?.Invoke();

    /// <summary>
    /// Back to the top on every Show(), and scrolling enabled only when the text actually
    /// overflows — a scroll on a three-line note reads as broken. Waits a frame because the
    /// ContentSizeFitter has not laid the text out yet when Show() returns.
    /// </summary>
    private async UniTaskVoid ResetScroll()
    {
        if (scrollRect == null) return;
        scrollRect.verticalNormalizedPosition = 1f;

        await UniTask.WaitForEndOfFrame(this);

        if (scrollRect == null || viewport == null) return;

        RectTransform content = scrollRect.content;
        if (content == null) return;

        // rect.height, not sizeDelta.y: with a ContentSizeFitter driving the height, sizeDelta
        // is an offset against the anchors and stays at 0.
        scrollRect.vertical = content.rect.height > viewport.rect.height;
        scrollRect.verticalNormalizedPosition = 1f;
    }
}
