using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Single end-of-run screen. Replaces LoseController and GameOverController, which were the
/// same code except for which buttons were visible and whether there was a title and stats.
///
/// Listens to <see cref="GameResultManager.OnGameResult"/> and only opens if the received
/// state has a <see cref="ResultPresentation"/> configured. States without a preset are
/// ignored, so WinController can keep living alongside it.
///
/// It does NOT implement IModalUI: it is a final screen, not a modal overlay. That is why it
/// handles Time.timeScale by hand instead of delegating to the UIStateManager.
///
/// Presets expected in the Inspector:
///   - Lose     -> no title, no stats, with Retry.
///   - GameOver -> "GAME OVER" in red, with stats, no Retry (the save is already deleted).
/// </summary>
public class ResultScreenController : BaseScreenController<ResultView, GameResultModel>
{
    [Header("Event Channels")]
    [SerializeField] private ScreenEventChannel _screenChannel;

    [Header("Navigation")]
    [SerializeField] private string _mainMenuGroup = "Menu";

    [Header("Presentation per result")]
    [Tooltip("One preset per GameState this screen should show. No preset = it does not open.")]
    [SerializeField] private ResultPresentation[] _presentations;

    private bool _isTransitioning;

    private void Awake()
    {
        if (view == null)
        {
            Debug.LogError($"[{nameof(ResultScreenController)}] view not assigned in the Inspector.");
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
        if (preset == null) return;   // A state this screen does not handle (e.g. Win).

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

    // ── Buttons ──────────────────────────────────────────────────

    /// <summary>
    /// Retry = reload the current scene group.
    ///
    /// The label comes from the ScreenManager and not from a serialized field: it used to be
    /// hardcoded as "Level1_Group", which does not exist in the SO_SceneList, so Retry only
    /// logged an error. Reading it from the manager works with any level.
    /// </summary>
    private void HandleRetry()
    {
        Time.timeScale = 1f;

        if (!ScreenManager.Exists)
        {
            Debug.LogError($"[{nameof(ResultScreenController)}] There is no ScreenManager to reload the level.");
            return;
        }

        // Without this, GameResultManager's _resultReported guard stays true and the next
        // death would not open any screen.
        GameResultManager.ResetSession();

        ScreenManager.Instance.ReloadCurrentGroup().Forget();
    }

    private void HandleMenu()
    {
        Time.timeScale = 1f;

        if (_screenChannel == null)
        {
            Debug.LogError($"[{nameof(ResultScreenController)}] The ScreenEventChannel is not assigned.");
            return;
        }

        _screenChannel.RaiseClearAll();
        _screenChannel.RaisePushScreen(_mainMenuGroup);
    }
}
