using System;
using UnityEngine;

public static class GameResultManager
{
    /// <summary>
    /// Wire the static reset into <see cref="GameSession.BeginNewSession"/> so a New Game / Retry
    /// clears the reported flag alongside every instance manager. Runs on each Play so the hook
    /// survives domain-reload-disabled enters into Play mode.
    ///
    /// AfterAssembliesLoaded, NOT SubsystemRegistration: GameSession clears OnNewSessionStarting in
    /// SubsystemRegistration, and Unity does not order methods of the same load type. When the clear
    /// ran second the hook was wiped, the reported flag never reset, and every run after the first
    /// result ignored its WinTrigger (WIR-035).
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void HookSessionReset()
    {
        GameSession.OnNewSessionStarting -= ResetSession;
        GameSession.OnNewSessionStarting += ResetSession;
    }

    ///<summary>
    ///Fired when the game ends or wins. ResultScreenController (Lose/GameOver) and WinController
    ///subscribe to this event and each one filters by the GameState it handles.
    ///</summary>

    //--Event ----------------------
    public static event Action<GameResultModel> OnGameResult;

    // -- Internal Model -----------
    private static GameResultModel _model;
    private static bool            _resultReported;

    static GameResultManager()
    {
        _model = new GameResultModel();
        _model.Initialize();
    }

    /// <summary>
    /// Optional win presentation (the escape's last shot, the gate slamming shut). When set,
    /// ReportWin lets it play first and only raises the result when it calls back. Null =
    /// immediate, as before. Same contract as <see cref="GameOverPresenter"/>.
    /// </summary>
    public static IWinPresenter WinPresenter { get; set; }

    public static void ReportWin(float time, int completedModules)
    {
        if (_resultReported) return;

        // Flagged before the presentation, as ReportGameOver does: a module running out while the
        // shot plays must not end the run a second time.
        _resultReported = true;

        if (WinPresenter != null)
        {
            WinPresenter.PresentWin(() => CommitWin(time, completedModules));
            return;
        }

        CommitWin(time, completedModules);
    }

    private static void CommitWin(float time, int completedModules)
    {
        _model.SetResult(GameState.Win, time, completedModules);
        OnGameResult?.Invoke(_model);
    }

    public static void ReportLoss(float time, int completedModules)
    {
        if (_resultReported) return;
        _resultReported = true;

        _model.SetResult(GameState.Lose, time, completedModules);
        OnGameResult?.Invoke(_model);
    }

    /// <summary>
    /// Every module has exploded. Raised before calling this method:
    /// - OnSaveDeleteRequested so the SaveManager deletes the active slot.
    /// </summary>
    public static event Action OnSaveDeleteRequested;

    /// <summary>
    /// Optional defeat presentation (the explosion cinematic). When set, ReportGameOver lets it
    /// play first and only raises the result when it calls back. Null = immediate, as before.
    /// </summary>
    public static IGameOverPresenter GameOverPresenter { get; set; }

    /// <param name="cause">
    /// The module whose explosion ended the run, so the presenter knows what to frame. Null when
    /// the defeat does not come from a single module.
    /// </param>
    public static void ReportGameOver(float time, int resolvedModules, ModuleRuntime cause = null)
    {
        if (_resultReported) return;

        // Flagged BEFORE the presentation, not after: while the cinematic plays, a capture or the
        // all-exploded path could report again and the run would end twice.
        _resultReported = true;

        if (GameOverPresenter != null)
        {
            GameOverPresenter.PresentGameOver(cause, () => CommitGameOver(time, resolvedModules));
            return;
        }

        CommitGameOver(time, resolvedModules);
    }

    private static void CommitGameOver(float time, int resolvedModules)
    {
        OnSaveDeleteRequested?.Invoke();

        _model.SetResult(GameState.GameOver, time, resolvedModules);
        OnGameResult?.Invoke(_model);
    }

    /// <summary>
    /// Whether the explosion that was just raised ends the run, per the SO_GameOverRules on
    /// ModuleManager (which reports the GameOver itself). Valid inside an
    /// <see cref="ModuleEvents.OnExploded"/> handler: ModuleManager marks the module Exploded
    /// before raising the event, so the count already includes it.
    /// </summary>
    public static bool ExplosionEndsRun(ModuleRuntime exploded) =>
        ModuleManager.Exists && ModuleManager.Instance.ExplosionEndsRun(exploded);

    /// <summary>Call when loading the gameplay scene to allow a new result to be reported.</summary>
    public static void ResetSession()
    {
        // GameOverPresenter is NOT cleared here: the presenter lives in the scene and registers
        // and unregisters itself in OnEnable/OnDisable.
        _resultReported = false;
        _model = new GameResultModel();
        _model.Initialize();
    }

}
