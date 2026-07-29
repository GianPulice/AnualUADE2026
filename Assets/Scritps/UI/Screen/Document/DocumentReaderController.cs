using Cysharp.Threading.Tasks;
using UnityEngine;

public class DocumentReaderController : BaseScreenController<DocumentReaderView, DocumentReaderModel>, IModalUI
{
    public static DocumentReaderController Instance { get; private set; }

    private bool isOpen;
    private bool isTransitioning;
    private IInteractable openingTarget;

    public bool IsOpen => isOpen;

    // ── IModalUI ─────────────────────────────────────────────────────────────
    public string ModalId        => "DocumentReader";
    public bool   ConsumesEscape => true;
    public bool   BlocksPause    => false;
    public bool   PausesGame     => false;
    public void   RequestClose() => CloseSafe().Forget();

    private void Awake()
    {
        Instance = this;

        model = new DocumentReaderModel();
        model.Initialize();

        if (view != null) view.gameObject.SetActive(false);

        InteractionEvents.OnTargetChanged += HandleTargetChanged;
    }

    private void OnDestroy()
    {
        InteractionEvents.OnTargetChanged -= HandleTargetChanged;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void Open(SO_DocumentData data)
    {
        if (data == null || isOpen || isTransitioning) return;

        model.SetDocument(data);
        view.Populate(data);

        // We store which interactable opened this document so the auto-close knows
        // when we have really "left" its range.
        openingTarget = InteractionManager.Exists
            ? InteractionManager.Instance.CurrentInteractable
            : null;

        OpenSafe().Forget();
    }

    // ── BaseScreenController hooks ───────────────────────────────────────────

    protected override void OnBeforeOpen()
    {
        isOpen = true;
        if (UIStateManager.Exists) UIStateManager.Instance.Push(this);
    }

    protected override void OnBeforeClose()
    {
        isOpen = false;
        openingTarget = null;
        if (UIStateManager.Exists) UIStateManager.Instance.Pop(this);
    }

    // ── Auto-close on target change ──────────────────────────────────────────

    private void HandleTargetChanged(IInteractable newTarget)
    {
        if (!isOpen) return;

        // If the InteractionManager stopped pointing at the note that opened this document
        // (either because the player walked away, or because they are now looking at
        // something else), we close it.
        if (!ReferenceEquals(newTarget, openingTarget))
            CloseSafe().Forget();
    }

    // ── Async helpers ────────────────────────────────────────────────────────

    private async UniTaskVoid OpenSafe()
    {
        isTransitioning = true;
        await Open();
        isTransitioning = false;
    }

    private async UniTaskVoid CloseSafe()
    {
        if (isTransitioning) return;
        isTransitioning = true;
        await Close();
        isTransitioning = false;
    }
}
