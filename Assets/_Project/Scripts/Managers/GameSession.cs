using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates the "new run" reset across every persistent manager.
///
/// The gameplay singletons (ModuleManager, InventoryManager, PuzzleStateManager, …) live in
/// the Data scene with DontDestroyOnLoad, so they survive returning to the Main Menu. Without
/// an explicit reset step, pressing New Game reuses the leftover state from the previous run
/// (exploded modules, active timers, picked-up items, completed puzzles).
///
/// Two integration paths, both dispatched by <see cref="BeginNewSession"/>:
///  • MonoBehaviour managers implement <see cref="ISessionResettable"/> and call
///    <see cref="Register"/> / <see cref="Unregister"/> in Awake / OnDestroy.
///  • Static managers (e.g. <see cref="GameResultManager"/>) subscribe to
///    <see cref="OnNewSessionStarting"/> from a [RuntimeInitializeOnLoadMethod] hook.
///
/// Call sites: <c>MainMenuController.EnterGameplay</c> (New Game / empty slot) and
/// <c>ResultScreenController.HandleRetry</c>.
/// </summary>
public static class GameSession
{
    private static readonly List<ISessionResettable> resettables = new List<ISessionResettable>();

    /// <summary>
    /// Raised by <see cref="BeginNewSession"/> after every registered <see cref="ISessionResettable"/>
    /// has run. Static classes that cannot implement the interface subscribe here — typically from
    /// a <see cref="RuntimeInitializeOnLoadMethod"/> so the hook survives domain-reload-disabled
    /// enters into Play mode.
    /// </summary>
    public static event Action OnNewSessionStarting;

    public static void Register(ISessionResettable resettable)
    {
        if (resettable == null) return;
        if (resettables.Contains(resettable)) return;
        resettables.Add(resettable);
    }

    public static void Unregister(ISessionResettable resettable)
    {
        if (resettable == null) return;
        resettables.Remove(resettable);
    }

    /// <summary>
    /// Clears every registered manager and fires <see cref="OnNewSessionStarting"/>.
    /// Safe to call even before any manager has registered — it is a no-op in that case.
    /// </summary>
    public static void BeginNewSession()
    {
        Debug.Log($"[GameSession] BeginNewSession — resetting {resettables.Count} manager(s) " +
                  $"and notifying {(OnNewSessionStarting?.GetInvocationList().Length ?? 0)} static hook(s).");

        // Iterate a copy so a resettable that unregisters during its reset (or an implementation
        // that spawns/destroys other resettables) does not corrupt the list.
        ISessionResettable[] snapshot = resettables.ToArray();
        for (int i = 0; i < snapshot.Length; i++)
        {
            try
            {
                snapshot[i].ResetForNewSession();
            }
            catch (Exception e)
            {
                // One misbehaving manager must not skip the others.
                Debug.LogException(e);
            }
        }

        try
        {
            OnNewSessionStarting?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    /// <summary>
    /// Static state survives leaving Play mode when domain reload is disabled, which would leave
    /// entries pointing at destroyed managers from the previous run. Clearing both here makes a
    /// fresh Play start with an empty registry — the managers re-register on their own Awake.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        resettables.Clear();
        OnNewSessionStarting = null;
    }
}
