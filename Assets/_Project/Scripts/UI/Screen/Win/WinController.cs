using Cysharp.Threading.Tasks;
using UnityEngine;

public class WinController : BaseScreenController<WinView, GameResultModel>, IModalUI
{
    // -- IModalUI -------------------
    // Same reason as ResultScreenController: without a modal on the stack PlayerCameraController
    // re-locks the cursor every frame, so the win screen came up with no mouse to click its
    // buttons, and ESC opened the pause menu over it. Time.timeScale stays hand-managed here.
    public string ModalId => "Win";
    public bool ConsumesEscape => false;
    public bool BlocksPause   => true;
    public bool PausesGame    => false;
    public void RequestClose() { }

    [Header("Event Channels")]
    [SerializeField] private ScreenEventChannel _screenChannel;

    [Header("Navigation Groups (Labels)")]
    [SerializeField] private string _mainMenuGroup = "Menu";

    private bool _isTransitioning;

    private void Awake()
    {
        if (view == null)
        {
            Debug.LogError($"[{nameof(WinController)}] view not assigned in the Inspector.");
            return;
        }

        if (model == null)
        {
            model = new GameResultModel();
            model.Initialize();
        }

        view.gameObject.SetActive(false);

        view.OnMainMenuClicked += HandleMainMenu;
        view.OnExitClicked     += HandleExit;
        GameResultManager.OnGameResult += HandleGameResult;
    }

    private void OnDestroy()
    {
        if (view == null) return;

        view.OnMainMenuClicked -= HandleMainMenu;
        view.OnExitClicked     -= HandleExit;
        GameResultManager.OnGameResult -= HandleGameResult;
    }

    protected override void OnBeforeOpen()
    {
        Time.timeScale = 0f;

        // Frees the cursor and stops PlayerCameraController from re-locking it.
        if (UIStateManager.Exists) UIStateManager.Instance.Push(this);

        view.SetData(model);
    }

    protected override void OnBeforeClose()
    {
        Time.timeScale = 1f;
        if (UIStateManager.Exists) UIStateManager.Instance.Pop(this);
    }

    private void HandleGameResult(GameResultModel incomingModel)
    {
        if (incomingModel.GameState != GameState.Win) return;
        if (_isTransitioning) return;

        InjectDependencies(incomingModel);
        OpenSafe().Forget();
    }

    private async UniTaskVoid OpenSafe()
    {
        _isTransitioning = true;
        await Open();
        _isTransitioning = false;
    }

    private void HandleMainMenu()
    {
        Time.timeScale = 1f;
        if (UIStateManager.Exists) UIStateManager.Instance.Pop(this);

        // Push alone, no Clear All first: the push already unloads the level, behind the loading
        // screen. A Clear All would unload it straight away, in view, before the fade even starts.
        _screenChannel.RaisePushScreen(_mainMenuGroup);
    }

    private void HandleExit() => ScreenManager.RequestQuit();
}
