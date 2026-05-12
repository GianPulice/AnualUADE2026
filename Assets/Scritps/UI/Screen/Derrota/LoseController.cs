using UnityEngine;
using Cysharp.Threading.Tasks;
public class LoseController : BaseScreenController<LoseView,GameResultModel>
{
    [Header("Event Channels")]
    [SerializeField] private ScreenEventChannel _screenChannel;

    [Header("Navigation Groups (Labels)")]
    [SerializeField] private string _currentLevelGroup = "Level1_Group"; // Necesario para reintentar
    [SerializeField] private string _mainMenuGroup = "MainMenu_Group";

    private bool _isTransitioning;

    // ── Lifecycle ─────────────────────────────────────────────
    private void Awake()
    {
        if (model == null)
        {
            model = new GameResultModel();
            model.Initialize();
        }

        view.OnRetryClicked += HandleRetry;
        view.OnMainMenuClicked += HandleMainMenu;
    }

    private void OnEnable() => GameResultManager.OnGameResult += HandleGameResult;
    private void OnDisable() => GameResultManager.OnGameResult -= HandleGameResult;

    private void OnDestroy()
    {
        view.OnRetryClicked -= HandleRetry;
        view.OnMainMenuClicked -= HandleMainMenu;
    }

    // ── BaseScreenController hooks ────────────────────────────
    protected override void OnBeforeOpen()
    {
        view.SetData(model);
    }

    // ── Cross-scene event ─────────────────────────────────────
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

    // ── Buttons ───────────────────────────────────────────────
    private void HandleRetry()
    {
        Time.timeScale = 1f;
        _screenChannel.RaisePopScreen(); // Descargamos la pantalla de derrota y el nivel actual
        _screenChannel.RaisePushScreen(_currentLevelGroup); // Volvemos a cargar el nivel limpio
    }

    private void HandleMainMenu()
    {
        Time.timeScale = 1f;
        _screenChannel.RaiseClearAll();
        _screenChannel.RaisePushScreen(_mainMenuGroup);
    }
}
