using System;
using UnityEngine;

/// <summary>
/// Global answer to "is the loading screen up right now?".
///
/// Static on purpose, so anything can ask without holding a reference: the loading screen covers
/// the moment one set of scenes is unloaded and the next one loaded, which is exactly when
/// references into those scenes are least trustworthy. A future update manager is meant to read
/// <see cref="IsLoading"/> and skip every gameplay Update while it is true.
///
/// Only <see cref="ScreenManager"/> drives it. It turns true once the screen is fully black and
/// the loading visuals are showing, and false the moment the fade back into the new scene starts
/// — so the new scene is already running while it is being revealed.
/// </summary>
public static class LoadingScreen
{
    /// <summary>
    /// The loading screen stays up at least this long, in real seconds. If unloading and loading
    /// took less, the rest is waited out on the loading screen; if it took longer, nothing extra.
    /// </summary>
    public const float MinimumDuration = 6f;

    /// <summary>True while the loading screen is covering a scene change.</summary>
    public static bool IsLoading { get; private set; }

    /// <summary>Fired when <see cref="IsLoading"/> turns true.</summary>
    public static event Action OnLoadingStarted;

    /// <summary>Fired when <see cref="IsLoading"/> turns false.</summary>
    public static event Action OnLoadingFinished;

    /// <summary>Only for <see cref="ScreenManager"/>. Firing twice in a row is a no-op.</summary>
    internal static void SetLoading(bool loading)
    {
        if (IsLoading == loading) return;

        IsLoading = loading;
        if (loading) OnLoadingStarted?.Invoke();
        else OnLoadingFinished?.Invoke();
    }

    // With domain reload turned off in the editor, statics survive between Play sessions: a run
    // stopped mid-load would start the next one already "loading", with the previous run's
    // listeners still subscribed.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsLoading = false;
        OnLoadingStarted = null;
        OnLoadingFinished = null;
    }
}
