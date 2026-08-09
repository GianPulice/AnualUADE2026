using System;

public static class GameResultManager
{
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

    public static void ReportWin(float time, int completedModules)
    {
        if (_resultReported) return;
        _resultReported = true;

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

    public static void ReportGameOver(float time, int resolvedModules)
    {
        if (_resultReported) return;
        _resultReported = true;

        OnSaveDeleteRequested?.Invoke();

        _model.SetResult(GameState.GameOver, time, resolvedModules);
        OnGameResult?.Invoke(_model);
    }

    /// <summary>Call when loading the gameplay scene to allow a new result to be reported.</summary>
    public static void ResetSession()
    {
        _resultReported = false;
        _model = new GameResultModel();
        _model.Initialize();
    }

}
