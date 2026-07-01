using Cysharp.Threading.Tasks;
using UnityEngine;

// Controlador de la pantalla de Game Over por módulos.
// NO implementa IModalUI — es una pantalla final, no un overlay modal.
// Gestiona Time.timeScale directamente como LoseController (mismo patrón).
// La vista tiene título GAME OVER rojo + vignette roja + stats.
// El save del slot activo ya fue borrado por GameResultManager.OnSaveDeleteRequested.
public class GameOverController : BaseScreenController<GameOverView, GameResultModel>
{
    [Header("Event Channels")]
    [SerializeField] private ScreenEventChannel _screenChannel;

    [Header("Navigation")]
    [SerializeField] private string _mainMenuGroup = "Menu";

    private bool _isTransitioning;

    private void Awake()
    {
        if (view == null)
        {
            Debug.LogError($"[{nameof(GameOverController)}] view no asignada en el Inspector.");
            return;
        }

        if (model == null)
        {
            model = new GameResultModel();
            model.Initialize();
        }

        view.gameObject.SetActive(false);
        view.OnMenuClicked += HandleMenu;
        GameResultManager.OnGameResult += HandleGameResult;
    }

    private void OnDestroy()
    {
        if (view != null) view.OnMenuClicked -= HandleMenu;
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

    private void HandleGameResult(GameResultModel incoming)
    {
        if (incoming.GameState != GameState.GameOver) return;
        if (_isTransitioning) return;

        InjectDependencies(incoming);
        OpenSafe().Forget();
    }

    private async UniTaskVoid OpenSafe()
    {
        _isTransitioning = true;
        await Open();
        _isTransitioning = false;
    }

    private void HandleMenu()
    {
        Time.timeScale = 1f;
        _screenChannel.RaiseClearAll();
        _screenChannel.RaisePushScreen(_mainMenuGroup);
    }
}
