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

    /// <summary>Label del grupo de escenas activo, o null si no hay ninguno cargado.</summary>
    public string CurrentGroupLabel => activeScreens.Count > 0 ? activeScreens.Peek() : null;

    /// <summary>
    /// Descarga y vuelve a cargar el grupo activo. Lo usa el Retry de la pantalla de derrota.
    ///
    /// No se puede armar con Pop + Push desde el canal de eventos: los dos handlers son
    /// UniTask fire-and-forget, asi que la carga arrancaria antes de que termine la descarga.
    /// Tampoco alcanza con un Push solo, porque HandlePushScreenAsync corta temprano cuando
    /// el label ya es la pantalla activa.
    /// </summary>
    public async UniTask ReloadCurrentGroup()
    {
        if (activeScreens.Count == 0)
        {
            Debug.LogWarning("[ScreenManager] ReloadCurrentGroup sin ningun grupo activo.");
            return;
        }

        string label = activeScreens.Pop();

        var groupEntry = sceneDatabase.GetGroup(label);
        if (groupEntry == null || groupEntry.sceneNames.Count == 0)
        {
            Debug.LogError($"[ScreenManager] No se encontró el grupo '{label}' en el SO_SceneList.");
            return;
        }

        await UnloadGroupAsync(label);

        activeScreens.Push(label);
        await LoadGroupAsync(groupEntry);

        Debug.Log($"<color=green>[ScreenManager] Grupo '{label}' recargado.</color>");
    }

    private void Awake()
    {
        CreateSingleton(true);
        if (sceneDatabase == null) Debug.LogError("[ScreenManager] Faltan asignar el SO_SceneList!");
        if (screenChannel == null) Debug.LogError("[ScreenManager] Faltan asignar el Event Channel!");
    }

    private void OnEnable()
    {
        Debug.Log("<color=cyan>[ScreenManager] Habilitado y escuchando eventos</color>");
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

    // ── Wrappers (Reciben los eventos de tu MainMenuController) ──

    private void OnPushScreenRequestedWrapper(string screenLabel)
    {
        Debug.Log($"<color=cyan>[ScreenManager] PUSH recibido: {screenLabel}</color>");
        HandlePushScreenAsync(screenLabel).Forget();
    }

    private void OnPopScreenRequestedWrapper()
    {
        Debug.Log("<color=cyan>[ScreenManager] POP recibido</color>");
        HandlePopScreenAsync().Forget();
    }

    private void OnClearAllRequestedWrapper()
    {
        Debug.Log("<color=cyan>[ScreenManager] CLEAR ALL recibido</color>");
        HandleClearAllAsync().Forget();
    }

    // ── Handlers (Manejan la lógica de carga usando tu SceneLoader) ──

    private async UniTask HandlePushScreenAsync(string screenLabel)
    {
        if (activeScreens.Count > 0 && activeScreens.Peek() == screenLabel)
        {
            Debug.LogWarning($"[ScreenManager] '{screenLabel}' ya es la pantalla activa.");
            return;
        }

        var groupEntry = sceneDatabase.GetGroup(screenLabel);
        if (groupEntry == null || groupEntry.sceneNames.Count == 0)
        {
            Debug.LogError($"[ScreenManager] No se encontró el grupo '{screenLabel}' en el SO_SceneList.");
            return;
        }

        // Descargamos la pantalla anterior
        if (activeScreens.Count > 0)
        {
            string previous = activeScreens.Pop();
            await UnloadGroupAsync(previous);
        }

        // Cargamos el nuevo grupo de escenas
        activeScreens.Push(screenLabel);
        await LoadGroupAsync(groupEntry);

        Debug.Log($"<color=green>[ScreenManager] Grupo '{screenLabel}' cargado exitosamente.</color>");
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
            // Protegemos las escenas persistentes (como Data) para que no se borren
            if (sceneDatabase.persistentSceneNames.Contains(sceneName)) continue;

            tasks.Add(sceneLoader.UnloadSceneAsync(sceneName));
        }
        await UniTask.WhenAll(tasks);
    }
}