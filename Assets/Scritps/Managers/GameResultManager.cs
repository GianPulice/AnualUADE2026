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

    static GameResultManager()
    {
        _model = new GameResultModel();
        _model.Initialize();
    }
    public static void ReportWin(float time, int completedModules)
    {
        _model.SetResult(GameState.Win, time, completedModules);
        OnGameResult?.Invoke(_model);
    }

    public static void ReportLoss(float time, int completedModules)
    {
        _model.SetResult(GameState.Lose, time, completedModules);
        OnGameResult?.Invoke(_model);
    }

}
