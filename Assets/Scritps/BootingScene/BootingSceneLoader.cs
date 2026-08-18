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
    [Tooltip("The group loaded by default in a Build, or when no previous scene is detected.")]
    [SerializeField] private string defaultStartGroup = "Menu";

#if UNITY_EDITOR
    private const string LastSceneKey = "LAST_PLAY_MODE_SCENE_PATH";
#endif

    private void Start()
    {
        // Fire the async boot sequence and "forget" it
        BootGameSequenceAsync().Forget();
    }

    private async UniTask BootGameSequenceAsync()
    {
        Debug.Log("<color=yellow>[Bootstrapper] 1. Starting the engine and loading Data...</color>");

        List<UniTask> persistentLoads = new List<UniTask>();
        foreach (string sceneName in sceneDatabase.persistentSceneNames)
        {
            persistentLoads.Add(sceneLoader.LoadSceneAdditiveAsync(sceneName));
        }

        await UniTask.WhenAll(persistentLoads);
        Debug.Log("<color=green>[Bootstrapper] 2. Data loaded successfully.</color>");

        // 2. Work out which screen we have to go to
        string nextGroupToLoad = GetNextGroupToLoad();

        Debug.Log($"<color=orange>[Bootstrapper] 3. Delegating the load to the ScreenManager. Group: {nextGroupToLoad}</color>");
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

            Debug.LogWarning($"[Bootstrapper] Scene '{sceneName}' is not in any group of the SO. Going to the menu.");
        }
#endif
        return defaultStartGroup;
    }
}
