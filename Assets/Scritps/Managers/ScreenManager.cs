using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class ScreenManager : Singleton<ScreenManager>
{
    [SerializeField] private List<SceneUIBinding> sceneUIBindings = new List<SceneUIBinding>();

    private List<string> currentUIScenes = new();


    void Awake()
    {
        CreateSingleton(true);
        SuscribeToOnSceneLoadedEvent();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        foreach (SceneUIBinding binding in sceneUIBindings)
        {
            binding.UpdateNames();
        }
    }
#endif


    // No necesita desuscripcion porque es singleton
    private void SuscribeToOnSceneLoadedEvent()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        HandleSceneUIAsync(scene.name).Forget();
    }

    private async UniTaskVoid HandleSceneUIAsync(string loadedSceneName)
    {
        List<string> nextUIScenes = GetUIScenesForScene(loadedSceneName);

        if (nextUIScenes == null || nextUIScenes.Count == 0)
        {
            await UnloadCurrentUIAsync();
            return;
        }

        await UnloadCurrentUIAsync();

        foreach (string uiScene in nextUIScenes)
        {
            if (!SceneManager.GetSceneByName(uiScene).isLoaded)
            {
                await LoadSceneAdditiveAsync(uiScene);
            }

            currentUIScenes.Add(uiScene);
            Debug.Log($"[ScreenManager] UI cargada: {uiScene}");
        }
    }

    private async UniTask UnloadCurrentUIAsync()
    {
        if (currentUIScenes.Count == 0)
            return;

        foreach (string uiScene in currentUIScenes)
        {
            if (SceneManager.GetSceneByName(uiScene).isLoaded)
            {
                await UnloadSceneAsync(uiScene);
                Debug.Log($"[ScreenManager] UI descargada: {uiScene}");
            }
        }

        currentUIScenes.Clear();
    }

    private List<string> GetUIScenesForScene(string sceneName)
    {
        foreach (SceneUIBinding binding in sceneUIBindings)
        {
            if (binding.SceneNames.Contains(sceneName))
            {
                return new List<string>(binding.UISceneNames);
            }
        }

        return null;
    }

    /// <summary>
    /// Carga una escena de forma aditiva y reporta el progreso.
    /// </summary>
    /// <param name="sceneName">Nombre de la escena a cargar.</param>
    private async UniTask LoadSceneAdditiveAsync(string sceneName)
    {
        AsyncOperation asyncOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

        if (asyncOperation == null)
        {
            Debug.LogError($"No se pudo cargar la escena '{sceneName}'");
            return;
        }

        await asyncOperation.ToUniTask();
    }

    /// <summary>
    /// Descarga una escena aditiva de la memoria.
    /// </summary>
    private async UniTask UnloadSceneAsync(string sceneName)
    {
        AsyncOperation asyncOperation = SceneManager.UnloadSceneAsync(sceneName);

        if (asyncOperation != null)
        {
            await asyncOperation.ToUniTask();
        }
    }
}

[System.Serializable]
public class SceneUIBinding
{
#if UNITY_EDITOR
    [SerializeField] private List<SceneAsset> gameplayScenes = new();
    [SerializeField] private List<SceneAsset> UIScenes = new();
#endif

    [HideInInspector][SerializeField] private List<string> sceneNames = new();
    [HideInInspector][SerializeField] private List<string> uiSceneNames = new();

    public IReadOnlyList<string> SceneNames => sceneNames;
    public IReadOnlyList<string> UISceneNames => uiSceneNames;

    public void UpdateNames()
    {
#if UNITY_EDITOR
        sceneNames.Clear();
        uiSceneNames.Clear();

        foreach (SceneAsset scene in gameplayScenes)
        {
            if (scene != null)
                sceneNames.Add(scene.name);
        }

        foreach (SceneAsset scene in UIScenes)
        {
            if (scene != null)
                uiSceneNames.Add(scene.name);
        }
#endif
    }
}