using System;

public static class GameResultManager
{
    ///<summary>
    ///Fired when the game ends or wins. WinController and LoseController subscribe to this event to show the corresponding screen.
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

    /// <summary>Llamar al cargar la escena de gameplay para habilitar un nuevo reporte.</summary>
    public static void ResetSession()
    {
        _resultReported = false;
        _model = new GameResultModel();
        _model.Initialize();
    }

}
