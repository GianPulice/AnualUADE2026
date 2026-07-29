using Cysharp.Threading.Tasks;
using UnityEngine;

public class WinController : BaseScreenController<WinView, GameResultModel>
{
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
        view.SetData(model);
    }

    protected override void OnBeforeClose()
    {
        Time.timeScale = 1f;
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
        _screenChannel.RaiseClearAll();
        _screenChannel.RaisePushScreen(_mainMenuGroup);
    }

    private void HandleExit()
    {
        Application.Quit();
    }
}
