using System;

// GameResultModel.cs
// Shared model between WinController and ResultScreenController (Lose / GameOver).
// Holds result data: score, time, etc.

public enum GameState
{
    Win,
    Lose,
    GameOver,
    None
}

public class GameResultModel : BaseScreenModel
{
    // Public Variables
    public GameState GameState { get; private set; }
    public float Time { get; private set; }
    public int CompletedModules { get; private set; }

    public event Action<GameResultModel> OnResultSet;

    public override void Initialize()
    {
        GameState = GameState.None;
        Time = 0f;
        CompletedModules = 0;
    }

    /// <summary>
    /// Call this from GameResultManager when the round ends.
    /// Both WinController and ResultScreenController will read from this.
    /// </summary>
    public void SetResult(GameState result, float time, int completedModules)
    {
        GameState = result;
        Time = time;
        CompletedModules = completedModules;
        OnResultSet?.Invoke(this);
    }
}
