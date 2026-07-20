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

    // ── API pública ──────────────────────────────────────────────────────────

    public void Open(SO_DocumentData data)
    {
        if (data == null || isOpen || isTransitioning) return;

        model.SetDocument(data);
        view.Populate(data);

        // Guardamos qué interactable abrió este documento para que el auto-close
        // sepa cuándo realmente "salimos" de su rango.
        openingTarget = InteractionManager.Exists
            ? InteractionManager.Instance.CurrentInteractable
            : null;

        OpenSafe().Forget();
    }

    // ── Hooks BaseScreenController ───────────────────────────────────────────

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

    // ── Auto-close por cambio de target ──────────────────────────────────────

    private void HandleTargetChanged(IInteractable newTarget)
    {
        if (!isOpen) return;

        // Si el InteractionManager dejo de apuntar al note que abrió este documento,
        // (sea porque el player se alejó, sea porque ahora mira otra cosa), cerramos.
        if (!ReferenceEquals(newTarget, openingTarget))
            CloseSafe().Forget();
    }

    // ── Helpers async ────────────────────────────────────────────────────────

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
