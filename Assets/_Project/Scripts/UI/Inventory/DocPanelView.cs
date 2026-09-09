using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VIEW of the note pop-up that opens on top of the inventory's detail panel.
///
/// One job: show a block of read-only text and get out of the way. It owns the title, the body,
/// the scroll and its own close (X) button, and delegates the open/close animation to the
/// <see cref="InventoryTabPanelAnimator"/> on the same GameObject — the panel grows from its
/// centre and collapses through the reverse animation.
///
/// It does NOT decide when to open or close. <see cref="ItemDetailView"/> drives it, and the X
/// only raises <see cref="OnCloseRequested"/> so that closing always travels the same path as
/// ESC does (InventoryManagerUI.CloseDocument -> ItemDetailView.HideDoc). One close path means
/// the ESC layer stack and the toggle button's label can never disagree with what is on screen.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[AddComponentMenu("WIRED/UI/Doc Panel View")]
public class DocPanelView : MonoBehaviour
{
    [Header("Content")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private ScrollRect scrollRect;
    [Tooltip("The ScrollRect's viewport. Used to decide whether the text overflows.")]
    [SerializeField] private RectTransform viewport;

    [Header("Close")]
    [Tooltip("The X inside the panel. It only raises OnCloseRequested — it does not close by itself.")]
    [SerializeField] private Button closeButton;

    [Header("Animation")]
    [Tooltip("If left empty, the animator on this GameObject is used.")]
    [SerializeField] private InventoryTabPanelAnimator animator;

    /// <summary>Raised by the X button. The owner decides what closing means.</summary>
    public event Action OnCloseRequested;

    /// <summary>
    /// True from Open() to Close(). The animation is deliberately not part of the answer: ESC
    /// must be able to unwind the layer on the frame it is pressed, not 0.18s later.
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
    /// not run until the first Open() activates the GameObject — but SetContent() is called
    /// before that, while the item is merely selected.
    /// </summary>
    private void EnsureWired()
    {
        if (wired) return;
        wired = true;

        if (animator == null) animator = GetComponent<InventoryTabPanelAnimator>();
        if (closeButton != null) closeButton.onClick.AddListener(RaiseCloseRequested);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Loads the text without showing anything. Called when the item is selected.</summary>
    public void SetContent(string title, string body)
    {
        EnsureWired();

        if (titleText != null) titleText.text = title;
        if (bodyText != null) bodyText.text = body;
    }

    public void Open()
    {
        EnsureWired();
        IsOpen = true;

        if (animator != null) animator.Open();
        else gameObject.SetActive(true);

        ResetScroll().Forget();
    }

    public void Close()
    {
        EnsureWired();
        IsOpen = false;

        if (animator != null) animator.Close();
        else gameObject.SetActive(false);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private void RaiseCloseRequested() => OnCloseRequested?.Invoke();

    /// <summary>
    /// Back to the top on every open, and scrolling enabled only when the text actually
    /// overflows — a scroll on a three-line note reads as broken. Waits a frame because the
    /// ContentSizeFitter has not laid the text out yet when Open() returns.
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
