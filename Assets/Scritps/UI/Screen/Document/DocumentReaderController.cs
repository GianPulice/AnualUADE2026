using Cysharp.Threading.Tasks;
using UnityEngine;

public class DocumentReaderController : BaseScreenController<DocumentReaderView, DocumentReaderModel>
{
    public static DocumentReaderController Instance { get; private set; }

    [Header("Input")]
    [SerializeField] private KeyCode closeKey = KeyCode.Escape;

    private bool isOpen;
    private bool isTransitioning;

    public bool IsOpen => isOpen;

    private void Awake()
    {
        Instance = this;

        model = new DocumentReaderModel();
        model.Initialize();

        if (view != null) view.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (isOpen && Input.GetKeyDown(closeKey))
            CloseSafe().Forget();
    }

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
        Time.timeScale = 0f;
    }

    protected override void OnBeforeClose()
    {
        isOpen = false;

        if (PauseManager.Instance == null || !PauseManager.Instance.IsPaused)
            Time.timeScale = 1f;
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
