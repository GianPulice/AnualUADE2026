using UnityEngine;
using Cysharp.Threading.Tasks;

// Del Win te manda a Main Menu y Main Menu tiene el boton de Exit Game.

public class WinController : BaseScreenController<WinView,GameResultModel>
{
    [Header("Event Channels")]
    [Tooltip("Canal para comunicar los cambios de pantalla al ScreenManager.")]
    [SerializeField] private ScreenEventChannel _screenChannel;

    [Header("Navigation Groups (Labels)")]
    [SerializeField] private string _mainMenuGroup = "Menu";

    private bool _isTransitioning;

    // ── Lifecycle ─────────────────────────────────────────────
    private void Awake()
    {
        if (model == null)
        {
            model = new GameResultModel();
            model.Initialize();
        }

        // Ahora el único botón que nos importa al ganar es volver al menú
        view.OnMainMenuClicked += HandleMainMenu;
    }

    private void OnEnable() => GameResultManager.OnGameResult += HandleGameResult;
    private void OnDisable() => GameResultManager.OnGameResult -= HandleGameResult;

    private void OnDestroy()
    {
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

    // ── Buttons ───────────────────────────────────────────────
    private void HandleMainMenu()
    {
        Time.timeScale = 1f;
        _screenChannel.RaiseClearAll(); // Limpiamos la pantalla de victoria y el nivel de fondo
        _screenChannel.RaisePushScreen(_mainMenuGroup); // Cargamos el menú principal
    }
}
