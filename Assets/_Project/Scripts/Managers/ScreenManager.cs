using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;

public class ScreenManager : Singleton<ScreenManager>
{
    [Header("Dependencies")]
    [SerializeField] private SceneLoader sceneLoader;
    [SerializeField] private ScreenEventChannel screenChannel;
    [SerializeField] private SO_SceneList sceneDatabase;

    [Header("Loading screen")]
    [Tooltip("Covers every scene change after the first one: fade to black, loading screen for at " +
             "least LoadingScreen.MinimumDuration seconds, fade back in. Without it scene changes " +
             "still work, just uncovered.")]
    [SerializeField] private LoadingScreenView loadingScreenPrefab;

    private Stack<string> activeScreens = new Stack<string>();

    private LoadingScreenView loadingScreen;

    // False until the first group has loaded. That first load is the boot into the menu (or, in
    // the editor, into whichever level Play was pressed from) and deliberately gets no loading
    // screen: there is nothing on screen yet to cover.
    private bool hasLoadedAnyGroup;

    // One scene change at a time. The requests arrive as fire-and-forget UniTasks, so without this
    // a double click, or a Retry pressed during a Main Menu, would start a second unload/load
    // while the first one is still halfway through.
    private bool isTransitioning;

    /// <summary>Label of the active scene group, or null if none is loaded.</summary>
    public string CurrentGroupLabel => activeScreens.Count > 0 ? activeScreens.Peek() : null;

    /// <summary>True while a scene change (or the quit sequence) is running.</summary>
    public bool IsTransitioning => isTransitioning;

    /// <summary>
    /// Unloads and reloads the active group. Used by the Retry button on the defeat screen.
    ///
    /// It cannot be built out of Pop + Push through the event channel: both handlers are
    /// fire-and-forget UniTasks, so the load would start before the unload finished.
    /// A Push alone is not enough either, because HandlePushScreenAsync bails out early when
    /// the label is already the active screen.
    /// </summary>
    public async UniTask ReloadCurrentGroup()
    {
        if (activeScreens.Count == 0)
        {
            Debug.LogWarning("[ScreenManager] ReloadCurrentGroup with no active group.");
            return;
        }

        string label = activeScreens.Peek();

        var groupEntry = sceneDatabase.GetGroup(label);
        if (groupEntry == null || groupEntry.sceneNames.Count == 0)
        {
            Debug.LogError($"[ScreenManager] Group '{label}' was not found in the SO_SceneList.");
            return;
        }

        if (!TryBeginTransition($"reload '{label}'")) return;
        try
        {
            await RunBehindLoadingScreenAsync(() => SwapToGroupAsync(label, groupEntry), revealAfter: true);
        }
        finally
        {
            isTransitioning = false;
        }

        Debug.Log($"<color=green>[ScreenManager] Group '{label}' reloaded.</color>");
    }

    /// <summary>
    /// Quits the game behind the loading screen: fade to black, unload the active scenes, hold the
    /// loading screen for the minimum time, quit. Every Exit button goes through here.
    ///
    /// Static so a button can call it without first checking the manager exists — with no
    /// ScreenManager it just quits.
    /// </summary>
    public static void RequestQuit()
    {
        if (Exists) instance.QuitAsync().Forget();
        else QuitApplication();
    }

    private async UniTask QuitAsync()
    {
        if (!TryBeginTransition("quit")) return;

        try
        {
            await RunBehindLoadingScreenAsync(UnloadAllGroupsAsync, revealAfter: false);
        }
        finally
        {
            // Left false even though the game is about to close: in the editor, stopping Play is
            // not instant, and a manager stuck "transitioning" would refuse the next request.
            isTransitioning = false;
        }

        QuitApplication();
    }

    private static void QuitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void Awake()
    {
        CreateSingleton(true);

        // CreateSingleton destroys a duplicate, but the rest of its Awake still runs — without
        // this it would spawn a second loading screen that nothing ever drives.
        if (instance != this) return;

        if (sceneDatabase == null) Debug.LogError("[ScreenManager] The SO_SceneList has not been assigned!");
        if (screenChannel == null) Debug.LogError("[ScreenManager] The Event Channel has not been assigned!");

        if (loadingScreenPrefab != null)
        {
            // A child of this DontDestroyOnLoad object, so it survives the scenes it covers.
            loadingScreen = Instantiate(loadingScreenPrefab, transform);
            loadingScreen.HideImmediate();
        }
        else
        {
            Debug.LogWarning("[ScreenManager] No loading screen prefab assigned. Scene changes " +
                             "will happen without the fade and the loading screen.", this);
        }
    }

    private void OnEnable()
    {
        Debug.Log("<color=cyan>[ScreenManager] Enabled and listening for events</color>");
        if (screenChannel != null)
        {
            screenChannel.OnPushScreenRequested += OnPushScreenRequestedWrapper;
            screenChannel.OnPopScreenRequested += OnPopScreenRequestedWrapper;
            screenChannel.OnClearAllScreensRequested += OnClearAllRequestedWrapper;
        }
    }

    private void OnDisable()
    {
        if (screenChannel != null)
        {
            screenChannel.OnPushScreenRequested -= OnPushScreenRequestedWrapper;
            screenChannel.OnPopScreenRequested -= OnPopScreenRequestedWrapper;
            screenChannel.OnClearAllScreensRequested -= OnClearAllRequestedWrapper;
        }
    }

    // ── Wrappers (receive the events from MainMenuController) ──

    private void OnPushScreenRequestedWrapper(string screenLabel)
    {
        Debug.Log($"<color=cyan>[ScreenManager] PUSH received: {screenLabel}</color>");
        HandlePushScreenAsync(screenLabel).Forget();
    }

    private void OnPopScreenRequestedWrapper()
    {
        Debug.Log("<color=cyan>[ScreenManager] POP received</color>");
        HandlePopScreenAsync().Forget();
    }

    private void OnClearAllRequestedWrapper()
    {
        Debug.Log("<color=cyan>[ScreenManager] CLEAR ALL received</color>");
        HandleClearAllAsync().Forget();
    }

    // ── Handlers (handle the load logic using the SceneLoader) ──

    private async UniTask HandlePushScreenAsync(string screenLabel)
    {
        if (activeScreens.Count > 0 && activeScreens.Peek() == screenLabel)
        {
            Debug.LogWarning($"[ScreenManager] '{screenLabel}' is already the active screen.");
            return;
        }

        var groupEntry = sceneDatabase.GetGroup(screenLabel);
        if (groupEntry == null || groupEntry.sceneNames.Count == 0)
        {
            Debug.LogError($"[ScreenManager] Group '{screenLabel}' was not found in the SO_SceneList.");
            return;
        }

        if (!TryBeginTransition($"push '{screenLabel}'")) return;
        try
        {
            if (hasLoadedAnyGroup)
                await RunBehindLoadingScreenAsync(() => SwapToGroupAsync(screenLabel, groupEntry), revealAfter: true);
            else
                await SwapToGroupAsync(screenLabel, groupEntry);

            hasLoadedAnyGroup = true;
        }
        finally
        {
            isTransitioning = false;
        }

        Debug.Log($"<color=green>[ScreenManager] Group '{screenLabel}' loaded successfully.</color>");
    }

    private async UniTask HandlePopScreenAsync()
    {
        if (activeScreens.Count == 0) return;

        string screenLabelToClose = activeScreens.Pop();
        await UnloadGroupAsync(screenLabelToClose);
    }

    private async UniTask HandleClearAllAsync()
    {
        List<UniTask> unloadTasks = new List<UniTask>();
        while (activeScreens.Count > 0)
        {
            string label = activeScreens.Pop();
            unloadTasks.Add(UnloadGroupAsync(label));
        }
        await UniTask.WhenAll(unloadTasks);
    }

    // ── Loading screen ──

    private bool TryBeginTransition(string what)
    {
        if (isTransitioning)
        {
            Debug.LogWarning($"[ScreenManager] Ignored {what}: a scene change is already running.");
            return false;
        }

        isTransitioning = true;
        return true;
    }

    /// <summary>
    /// Fade to black, run <paramref name="work"/> behind the loading screen, keep the loading
    /// screen up until at least <see cref="LoadingScreen.MinimumDuration"/> seconds have passed
    /// since it appeared, then fade back in (unless <paramref name="revealAfter"/> is false, for
    /// quitting). Runs <paramref name="work"/> uncovered when there is no loading screen.
    ///
    /// A failure inside <paramref name="work"/> is logged and the sequence still finishes, so an
    /// exception can never leave the game behind a black screen with IsLoading stuck on.
    /// </summary>
    private async UniTask RunBehindLoadingScreenAsync(Func<UniTask> work, bool revealAfter)
    {
        if (loadingScreen == null)
        {
            await work();
            return;
        }

        // This object is DontDestroyOnLoad, so this only fires when the game itself shuts down.
        CancellationToken token = this.GetCancellationTokenOnDestroy();

        await loadingScreen.FadeToBlackAsync(token);

        loadingScreen.SetProgress(0f);
        loadingScreen.SetContentVisible(true);
        LoadingScreen.SetLoading(true);

        float startTime = Time.realtimeSinceStartup;

        // Preserve: the task is polled every frame below and awaited once at the end.
        UniTask workTask = RunSafely(work).Preserve();

        // Wait for BOTH the work and the minimum time. Real time, not scaled: the pause and
        // result screens can hand over with Time.timeScale still at 0.
        while (true)
        {
            float elapsed = Time.realtimeSinceStartup - startTime;
            bool workDone = workTask.Status != UniTaskStatus.Pending;

            if (workDone && elapsed >= LoadingScreen.MinimumDuration) break;

            // The bar tracks the minimum time. While the scenes are still loading it stops just
            // short of full, so a load that runs past the minimum reads as "almost there" rather
            // than as a full bar that is stuck.
            float progress = elapsed / LoadingScreen.MinimumDuration;
            loadingScreen.SetProgress(workDone ? progress : Mathf.Min(progress, 0.95f));

            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        await workTask;
        loadingScreen.SetProgress(1f);

        if (!revealAfter) return;

        LoadingScreen.SetLoading(false);
        await loadingScreen.FadeFromBlackAsync(token);
    }

    private static async UniTask RunSafely(Func<UniTask> work)
    {
        try
        {
            await work();
        }
        catch (Exception e) when (!(e is OperationCanceledException))
        {
            Debug.LogException(e);
        }
    }

    // ── Helpers ──

    /// <summary>
    /// Unloads every active group and loads <paramref name="group"/> as the only one. Everything
    /// and not just the top of the stack, so a stray group left behind by a Clear All or a Pop
    /// cannot survive into the next scene.
    /// </summary>
    private async UniTask SwapToGroupAsync(string label, SceneGroupEntry group)
    {
        await UnloadAllGroupsAsync();

        activeScreens.Push(label);
        await LoadGroupAsync(group);
    }

    private async UniTask UnloadAllGroupsAsync()
    {
        while (activeScreens.Count > 0)
        {
            await UnloadGroupAsync(activeScreens.Pop());
        }
    }

    private async UniTask LoadGroupAsync(SceneGroupEntry group)
    {
        List<UniTask> tasks = new List<UniTask>();
        foreach (string sceneName in group.sceneNames)
        {
            tasks.Add(sceneLoader.LoadSceneAdditiveAsync(sceneName));
        }
        await UniTask.WhenAll(tasks);
    }

    private async UniTask UnloadGroupAsync(string label)
    {
        var group = sceneDatabase.GetGroup(label);
        if (group == null) return;

        List<UniTask> tasks = new List<UniTask>();
        foreach (string sceneName in group.sceneNames)
        {
            // Protect the persistent scenes (such as Data) so they are not unloaded
            if (sceneDatabase.persistentSceneNames.Contains(sceneName)) continue;

            tasks.Add(sceneLoader.UnloadSceneAsync(sceneName));
        }
        await UniTask.WhenAll(tasks);
    }
}
