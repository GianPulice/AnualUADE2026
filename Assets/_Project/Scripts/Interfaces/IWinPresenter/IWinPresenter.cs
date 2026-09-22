using System;

/// <summary>
/// Something that plays between the player crossing the level's WinTrigger and the Win screen
/// (today: the last shot of the escape, <see cref="EscapeSequenceDirector"/> — the gate slamming
/// shut in the Nemesis's face). The win-side twin of <see cref="IGameOverPresenter"/>.
///
/// Registered on <see cref="GameResultManager.WinPresenter"/>. When one is registered, ReportWin
/// hands it a commit callback instead of raising the result straight away; the presenter MUST call
/// the callback exactly once when it is done, or the run never ends.
/// </summary>
public interface IWinPresenter
{
    /// <param name="commit">Raises the Win result. Call it once, when the presentation ends.</param>
    void PresentWin(Action commit);
}
