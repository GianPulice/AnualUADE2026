using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class BootingSceneLoader : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private SceneLoader sceneLoader;
    [SerializeField] private ScreenEventChannel screenChannel;
    [SerializeField] private SO_SceneList sceneDatabase;

    [Header("Settings")]
    [Tooltip("El grupo que se cargará por defecto en una Build o si no se detecta escena previa.")]
    [SerializeField] private string defaultStartGroup = "Menu";

#if UNITY_EDITOR
    private const string LastSceneKey = "LAST_PLAY_MODE_SCENE_PATH";
#endif

    private void Start()
    {
        // Disparamos la secuencia de booteo asíncrona y la "olvidamos"
        BootGameSequenceAsync().Forget();
    }

    private async UniTask BootGameSequenceAsync()
    {
        Debug.Log("<color=yellow>[Bootstrapper] 1. Iniciando motor y cargando Data...</color>");

        List<UniTask> persistentLoads = new List<UniTask>();
        foreach (string sceneName in sceneDatabase.persistentSceneNames)
        {
            persistentLoads.Add(sceneLoader.LoadSceneAdditiveAsync(sceneName));
        }

        await UniTask.WhenAll(persistentLoads);
        Debug.Log("<color=green>[Bootstrapper] 2. Data cargada exitosamente.</color>");

        // 2. Averiguar a qué pantalla tenemos que ir
        string nextGroupToLoad = GetNextGroupToLoad();

        Debug.Log($"<color=orange>[Bootstrapper] 3. Delegando carga al ScreenManager. Grupo: {nextGroupToLoad}</color>");
        screenChannel.RaisePushScreen(nextGroupToLoad);
        await UniTask.Yield();
        await sceneLoader.UnloadSceneAsync("Bootstrap");
    }

    private string GetNextGroupToLoad()
    {
#if UNITY_EDITOR
        string lastScenePath = EditorPrefs.GetString(LastSceneKey, "");

        if (!string.IsNullOrEmpty(lastScenePath))
        {
            string sceneName = System.IO.Path.GetFileNameWithoutExtension(lastScenePath);

            if (sceneDatabase.persistentSceneNames.Contains(sceneName) || sceneName == "Bootstrap")
            {
                return defaultStartGroup;
            }

            foreach (var group in sceneDatabase.sceneGroups)
            {
                if (group.sceneNames.Contains(sceneName))
                {
                    return group.label;
                }
            }

            Debug.LogWarning($"[Bootstrapper] La escena '{sceneName}' no está en ningún grupo del SO. Yendo al menú.");
        }
#endif
        return defaultStartGroup;
    }
}
