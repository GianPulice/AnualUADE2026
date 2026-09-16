using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VIEW of the note reader: a 650x850 sheet in the middle of the screen over a dimmed world.
///
/// The sheet is the inventory's Doc Box built at page size — same surface material, same bevel
/// frame, same header bar and scroll well — so a note read on pickup and the same note reopened
/// from the inventory look like the same object. <see cref="DocPanelView"/> stays exactly as it
/// was; this is a second, larger instance of the look, not a replacement.
///
/// It owns no policy: the X, the dim and ESC all end up raising <see cref="OnCloseRequested"/>,
/// and <see cref="DocumentReaderController"/> decides what closing means. One close path is what
/// keeps the UIStateManager stack honest.
/// </summary>
public class DocumentReaderView : BaseScreenView
{
    [Header("Sheet")]
    [Tooltip("Animator on the sheet. Pops it open from the centre; leave empty to just fade.")]
    [SerializeField] private InventoryTabPanelAnimator sheetAnimator;

    [Header("Content")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI contentText;
    [Tooltip("Optional illustration above the body. Hidden when the document has no sprite.")]
    [SerializeField] private Image documentImage;
    [SerializeField] private ScrollRect scrollRect;
    [Tooltip("The ScrollRect's viewport. Used to decide whether the text overflows.")]
    [SerializeField] private RectTransform viewport;

    [Header("Close")]
    [Tooltip("The X in the header. Only raises OnCloseRequested.")]
    [SerializeField] private Button closeButton;
    [Tooltip("The dimmed background. Clicking outside the sheet closes it too.")]
    [SerializeField] private Button dimButton;

    [Header("Footer")]
    [SerializeField] private TextMeshProUGUI hintText;
    [SerializeField] private string hintLabel = "[ ESC ]  CLOSE";

    /// <summary>Raised by the X, by a click on the dim, and by anything else that means "close".</summary>
    public event Action OnCloseRequested;

    private bool wired;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake() => EnsureWired();

    private void OnDestroy()
    {
        if (closeButton != null) closeButton.onClick.RemoveListener(RaiseCloseRequested);
        if (dimButton != null)   dimButton.onClick.RemoveListener(RaiseCloseRequested);
    }

    /// <summary>
    /// Idempotent, and not only in Awake: the reader starts inactive, so Awake does not run until
    /// the first open — but Populate() is called before that, while the controller is still
    /// preparing the document.
    /// </summary>
    private void EnsureWired()
    {
        if (wired) return;
        wired = true;

        if (closeButton != null) closeButton.onClick.AddListener(RaiseCloseRequested);
        if (dimButton != null)   dimButton.onClick.AddListener(RaiseCloseRequested);
        if (hintText != null)    hintText.text = hintLabel;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void Populate(string title, string body, Sprite image)
    {
        EnsureWired();

        if (titleText != null)   titleText.text = string.IsNullOrWhiteSpace(title) ? "NOTE" : title;
        if (contentText != null) contentText.text = body ?? string.Empty;

        if (documentImage != null)
        {
            bool hasImage = image != null;
            documentImage.gameObject.SetActive(hasImage);
            if (hasImage) documentImage.sprite = image;
        }
    }

    // ── Show / Hide ───────────────────────────────────────────────────────────

    /// <summary>
    /// Fades the layer in (base) while the sheet itself pops open. The animator is started after
    /// the root is active so its own Awake has run and LeanTween is tweening a live object.
    /// </summary>
    public override async UniTask ShowAsync()
    {
        EnsureWired();

        gameObject.SetActive(true);
        sheetAnimator?.Open();
        ResetScroll().Forget();

        await base.ShowAsync();
    }

    public override async UniTask HideAsync()
    {
        sheetAnimator?.Close();
        await base.HideAsync();
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private void RaiseCloseRequested() => OnCloseRequested?.Invoke();

    /// <summary>
    /// Back to the top on every open, and scrolling enabled only when the text actually overflows —
    /// a scrollbar on a three-line note reads as broken. Waits a frame because the
    /// ContentSizeFitter has not laid the text out yet when the sheet opens.
    /// </summary>
    private async UniTaskVoid ResetScroll()
    {
        if (scrollRect == null) return;
        scrollRect.verticalNormalizedPosition = 1f;

        await UniTask.WaitForEndOfFrame(this);

        if (scrollRect == null || viewport == null) return;

        RectTransform content = scrollRect.content;
        if (content == null) return;

        // rect.height, not sizeDelta.y: with a ContentSizeFitter driving the height, sizeDelta is
        // an offset against the anchors and stays at 0.
        scrollRect.vertical = content.rect.height > viewport.rect.height;
        scrollRect.verticalNormalizedPosition = 1f;
    }
}
