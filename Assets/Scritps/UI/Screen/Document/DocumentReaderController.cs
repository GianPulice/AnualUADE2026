using Cysharp.Threading.Tasks;
using UnityEngine;

public class DocumentReaderController : BaseScreenController<DocumentReaderView, DocumentReaderModel>, IModalUI
{
    public static DocumentReaderController Instance { get; private set; }

    [Header("Input")]
    [SerializeField] private KeyCode closeKey = KeyCode.Escape;

    private bool isOpen;
    private bool isTransitioning;

    public bool IsOpen => isOpen;

    // -- IModalUI --
    public string ModalId => "DocumentReader";
    public bool ConsumesEscape => true;
    public bool BlocksPause   => false;   // Pausa puede abrirse encima del documento
    public void RequestClose() => CloseSafe().Forget();

    private void Awake()
    {
        Instance = this;

        model = new DocumentReaderModel();
        model.Initialize();

        if (view != null) view.gameObject.SetActive(false);
    }

    // El cierre por ESC ahora lo gobierna UIStateManager via UI/Exit -> RequestClose.

    public void Open(SO_DocumentData data)
    {
        if (data == null || isOpen || isTransitioning) return;

        model.SetDocument(data);
        view.Populate(data);
        OpenSafe().Forget();
    }

    protected override void OnBeforeOpen()
    {
        isOpen = true;
        if (UIStateManager.Exists) UIStateManager.Instance.Push(this);
    }

    protected override void OnBeforeClose()
    {
        isOpen = false;
        if (UIStateManager.Exists) UIStateManager.Instance.Pop(this);
    }

    private async UniTaskVoid OpenSafe()
    {
        isTransitioning = true;
        await Open();
        isTransitioning = false;
    }

    private async UniTaskVoid CloseSafe()
    {
        isTransitioning = true;
        await Close();
        isTransitioning = false;
    }
}
