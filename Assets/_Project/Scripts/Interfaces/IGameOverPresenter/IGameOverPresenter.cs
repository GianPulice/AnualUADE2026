using System;

/// <summary>
/// Something that plays between the module explosion that ends the run and the GameOver screen
/// (today: <see cref="ModuleExplosionSequence"/>, the defeat cinematic).
///
/// Registered on <see cref="GameResultManager.GameOverPresenter"/>. When one is registered,
/// ReportGameOver hands it the module that caused the defeat and a commit callback instead of
/// raising the result straight away; the presenter MUST call the callback exactly once when it is
/// done, or the run never ends.
/// </summary>
public interface IGameOverPresenter
{
    /// <param name="cause">The module whose explosion ended the run. Can be null.</param>
    /// <param name="commit">Raises the GameOver result. Call it once, when the presentation ends.</param>
    void PresentGameOver(ModuleRuntime cause, Action commit);
}
