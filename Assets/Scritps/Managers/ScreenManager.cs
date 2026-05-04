using UnityEngine;
using System.Linq;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine.InputSystem;

//Cuando se agregue la escena Data sacar el Singleton y el dont destroy.
public class ScreenManager : Singleton<ScreenManager>
{
    [Header("Dependencies")]
    [SerializeField] private SceneLoader sceneLoader;
    [SerializeField] private ScreenEventChannel screenChannel;
    [SerializeField] private SO_SceneList sceneDatabase;

    //[Header("Player Input")]
    //[SerializeField] private PlayerInput playerInput;

    private Stack<string> activeScreens = new Stack<string>();
    private void Awake()
    {
        CreateSingleton(true);
        if (sceneDatabase == null) Debug.LogError("[ScreenManager] Faltan asignar el SO_SceneList!");
        if (screenChannel == null) Debug.LogError("[ScreenManager] Faltan asignar el Event Channel!");
    }
    private void OnEnable()
    {
        Debug.Log("<color=cyan>[ScreenManager] habilitado</color>");
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
    // ── Wrappers ─────────────────────────────────────────────────────────────

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

    // ── Handlers ─────────────────────────────────────────────────────────────

    private async UniTask HandlePushScreenAsync(string screenLabel)
    {
        // Evitamos pushear la misma pantalla que ya está activa
        if (activeScreens.Count > 0 && activeScreens.Peek() == screenLabel)
        {
            Debug.LogWarning($"[ScreenManager] '{screenLabel}' ya es la pantalla activa.");
            return;
        }

        var groupEntry = sceneDatabase.GetGroup(screenLabel);
        if (groupEntry == null || groupEntry.sceneNames.Count == 0)
        {
            Debug.LogError($"[ScreenManager] No se encontró el grupo '{screenLabel}' o está vacío en el SO.");
            return;
        }

        // Descargamos la pantalla anterior antes de cargar la nueva
        if (activeScreens.Count > 0)
        {
            string previous = activeScreens.Pop();
            await UnloadGroupAsync(previous);
        }

        // Cargamos todas las escenas del nuevo grupo en paralelo
        activeScreens.Push(screenLabel);
        await LoadGroupAsync(groupEntry);

        Debug.Log($"<color=green>[ScreenManager] Grupo '{screenLabel}' cargado.</color>");
    }

    private async UniTask HandlePopScreenAsync()
    {
        if (activeScreens.Count == 0)
        {
            Debug.LogWarning("[ScreenManager] Stack vacío, nada que popear.");
            return;
        }

        string screenLabelToClose = activeScreens.Pop();
        await UnloadGroupAsync(screenLabelToClose);

        Debug.Log($"<color=yellow>[ScreenManager] Grupo '{screenLabelToClose}' descargado.</color>");
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
        Debug.Log("<color=yellow>[ScreenManager] Todas las pantallas descargadas.</color>");
    }
    // ── Helpers ───────────────────────────────────────────────────────────────

    private async UniTask LoadGroupAsync(SceneGroupEntry group)
    {
        List<UniTask> tasks = new List<UniTask>();
        foreach (string sceneName in group.sceneNames)
        {
            Debug.Log($"[ScreenManager] Cargando escena: '{sceneName}'");
            tasks.Add(sceneLoader.LoadSceneAdditiveAsync(sceneName));
        }
        await UniTask.WhenAll(tasks);
    }

    private async UniTask UnloadGroupAsync(string label)
    {
        var group = sceneDatabase.GetGroup(label);
        if (group == null || group.sceneNames.Count == 0)
        {
            Debug.LogWarning($"[ScreenManager] No se encontró el grupo '{label}' para descargar.");
            return;
        }

        List<UniTask> tasks = new List<UniTask>();
        foreach (string sceneName in group.sceneNames)
        {
            // Si es persistente, la saltamos
            if (sceneDatabase.persistentSceneNames.Contains(sceneName))
            {
                Debug.Log($"[ScreenManager] '{sceneName}' es persistente, no se descarga.");
                continue;
            }
            tasks.Add(sceneLoader.UnloadSceneAsync(sceneName));
        }
        await UniTask.WhenAll(tasks);
    }
    private void SetInputMap(string mapName)
    {
        //if (playerInput != null && playerInput.currentActionMap.name != mapName)
        //{
        //    playerInput.SwitchCurrentActionMap(mapName);
        //}
    }
}
