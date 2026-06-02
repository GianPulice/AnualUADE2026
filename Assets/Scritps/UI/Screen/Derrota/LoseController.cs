using Cysharp.Threading.Tasks;
using UnityEngine;

public class LoseController : BaseScreenController<LoseView, GameResultModel>
{
    [Header("Event Channels")]
    [SerializeField] private ScreenEventChannel _screenChannel;

    [Header("Navigation Groups (Labels)")]
    [SerializeField] private string _currentLevelGroup = "Level1_Group";
    [SerializeField] private string _mainMenuGroup = "MainMenu_Group";

    private bool _isTransitioning;

    private void Awake()
    {
        if (view == null)
        {
            Debug.LogError($"[{nameof(LoseController)}] view no asignada en el Inspector.");
            return;
        }

        if (model == null)
        {
            model = new GameResultModel();
            model.Initialize();
        }

        view.gameObject.SetActive(false);

        view.OnRetryClicked   += HandleRetry;
        view.OnOptionsClicked += HandleOptions;
        view.OnExitClicked    += HandleExit;
        GameResultManager.OnGameResult += HandleGameResult;
    }

    private void OnDestroy()
    {
        if (view == null) return;

        view.OnRetryClicked   -= HandleRetry;
        view.OnOptionsClicked -= HandleOptions;
        view.OnExitClicked    -= HandleExit;
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
        if (incomingModel.GameState != GameState.Lose) return;
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

    private void HandleRetry()
    {
        Time.timeScale = 1f;
        _screenChannel.RaisePopScreen();
        _screenChannel.RaisePushScreen(_currentLevelGroup);
    }

    private void HandleOptions()
    {
        Debug.Log("Settings no implementado aún.");
    }

    private void HandleExit()
    {
        Time.timeScale = 1f;
        _screenChannel.RaiseClearAll();
        _screenChannel.RaisePushScreen(_mainMenuGroup);
    }
}
