/// <summary>
/// A persistent manager whose state must be cleared when a new run starts (New Game, empty
/// save slot, or Retry after GameOver). Implementers register with <see cref="GameSession"/>
/// on Awake / OnEnable and unregister on OnDestroy; <see cref="GameSession.BeginNewSession"/>
/// invokes <see cref="ResetForNewSession"/> on every registered instance.
///
/// Adding a new stateful manager only requires implementing this interface and the two
/// Register/Unregister calls — no changes to MainMenuController or ResultScreenController.
/// </summary>
public interface ISessionResettable
{
    /// <summary>
    /// Clear every field that must not carry over from the previous run. This runs while the
    /// gameplay scene is unloaded, so implementers do not need to notify UI listeners.
    /// </summary>
    void ResetForNewSession();
}
