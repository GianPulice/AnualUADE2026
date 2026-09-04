using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;

public class ScreenManager : Singleton<ScreenManager>
{
    [Header("Dependencies")]
    [SerializeField] private SceneLoader sceneLoader;
    [SerializeField] private ScreenEventChannel screenChannel;
    [SerializeField] private SO_SceneList sceneDatabase;

    private Stack<string> activeScreens = new Stack<string>();

    /// <summary>Label of the active scene group, or null if none is loaded.</summary>
    public string CurrentGroupLabel => activeScreens.Count > 0 ? activeScreens.Peek() : null;

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

        string label = activeScreens.Pop();

        var groupEntry = sceneDatabase.GetGroup(label);
        if (groupEntry == null || groupEntry.sceneNames.Count == 0)
        {
            Debug.LogError($"[ScreenManager] Group '{label}' was not found in the SO_SceneList.");
            return;
        }

        await UnloadGroupAsync(label);

        activeScreens.Push(label);
        await LoadGroupAsync(groupEntry);

        Debug.Log($"<color=green>[ScreenManager] Group '{label}' reloaded.</color>");
    }

    private void Awake()
    {
        CreateSingleton(true);
        if (sceneDatabase == null) Debug.LogError("[ScreenManager] The SO_SceneList has not been assigned!");
        if (screenChannel == null) Debug.LogError("[ScreenManager] The Event Channel has not been assigned!");
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

        // Unload the previous screen
        if (activeScreens.Count > 0)
        {
            string previous = activeScreens.Pop();
            await UnloadGroupAsync(previous);
        }

        // Load the new scene group
        activeScreens.Push(screenLabel);
        await LoadGroupAsync(groupEntry);

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

    // ── Helpers ──

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
