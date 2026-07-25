using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Pantalla unica de fin de partida. Reemplaza a LoseController y GameOverController,
/// que eran el mismo codigo salvo por que botones se veian y si habia titulo y stats.
///
/// Escucha <see cref="GameResultManager.OnGameResult"/> y se abre solo si el estado
/// recibido tiene un <see cref="ResultPresentation"/> configurado. Los estados sin preset
/// los ignora, asi que WinController puede seguir viviendo en paralelo.
///
/// NO implementa IModalUI: es una pantalla final, no un overlay modal. Por eso maneja
/// Time.timeScale a mano en vez de delegarlo en UIStateManager.
///
/// Presets esperados en el Inspector:
///   - Lose     -> sin titulo, sin stats, con Retry.
///   - GameOver -> "GAME OVER" rojo, con stats, sin Retry (el save ya fue borrado).
/// </summary>
public class ResultScreenController : BaseScreenController<ResultView, GameResultModel>
{
    [Header("Event Channels")]
    [SerializeField] private ScreenEventChannel _screenChannel;

    [Header("Navigation")]
    [SerializeField] private string _mainMenuGroup = "Menu";

    [Header("Presentación por resultado")]
    [Tooltip("Un preset por GameState que esta pantalla deba mostrar. Sin preset = no se abre.")]
    [SerializeField] private ResultPresentation[] _presentations;

    private bool _isTransitioning;

    private void Awake()
    {
        if (view == null)
        {
            Debug.LogError($"[{nameof(ResultScreenController)}] view no asignada en el Inspector.");
            return;
        }

        if (model == null)
        {
            model = new GameResultModel();
            model.Initialize();
        }

        view.gameObject.SetActive(false);

        view.OnRetryClicked    += HandleRetry;
        view.OnMainMenuClicked += HandleMenu;
        GameResultManager.OnGameResult += HandleGameResult;
    }

    private void OnDestroy()
    {
        GameResultManager.OnGameResult -= HandleGameResult;

        if (view == null) return;
        view.OnRetryClicked    -= HandleRetry;
        view.OnMainMenuClicked -= HandleMenu;
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
        if (_isTransitioning) return;

        ResultPresentation preset = FindPresentation(incoming.GameState);
        if (preset == null) return;   // Estado que esta pantalla no maneja (ej: Win).

        InjectDependencies(incoming);
        view.ApplyPresentation(preset);
        OpenSafe().Forget();
    }

    private ResultPresentation FindPresentation(GameState state)
    {
        if (_presentations == null) return null;

        foreach (ResultPresentation preset in _presentations)
            if (preset != null && preset.State == state) return preset;

        return null;
    }

    private async UniTaskVoid OpenSafe()
    {
        _isTransitioning = true;
        await Open();
        _isTransitioning = false;
    }

    // ── Botones ──────────────────────────────────────────────────

    /// <summary>
    /// Reintentar = recargar el grupo de escenas actual.
    ///
    /// El label sale de ScreenManager y no de un campo serializado: antes estaba
    /// hardcodeado como "Level1_Group", que no existe en el SO_SceneList, asi que el
    /// Retry solo logueaba un error. Leyendolo del manager funciona con cualquier nivel.
    /// </summary>
    private void HandleRetry()
    {
        Time.timeScale = 1f;

        if (!ScreenManager.Exists)
        {
            Debug.LogError($"[{nameof(ResultScreenController)}] No hay ScreenManager para recargar el nivel.");
            return;
        }

        // Sin esto el guard _resultReported de GameResultManager sigue en true y la
        // siguiente muerte no volveria a abrir ninguna pantalla.
        GameResultManager.ResetSession();

        ScreenManager.Instance.ReloadCurrentGroup().Forget();
    }

    private void HandleMenu()
    {
        Time.timeScale = 1f;

        if (_screenChannel == null)
        {
            Debug.LogError($"[{nameof(ResultScreenController)}] Falta asignar el ScreenEventChannel.");
            return;
        }

        _screenChannel.RaiseClearAll();
        _screenChannel.RaisePushScreen(_mainMenuGroup);
    }
}
