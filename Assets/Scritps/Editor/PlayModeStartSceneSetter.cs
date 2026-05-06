using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class PlayModeStartSceneSetter
{
    private const string BootingScenePath = "Assets/Scenes/Bootstrapper/Bootstrap.unity";
    private const string LastSceneKey = "LAST_PLAY_MODE_SCENE_PATH";


    static PlayModeStartSceneSetter()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        SceneAsset bootingScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootingScenePath);

        if (bootingScene != null)
        {
            EditorSceneManager.playModeStartScene = bootingScene;
        }
        else
        {
            Debug.LogWarning($"No se encontró BootingScene en: {BootingScenePath}");
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode)
            return;

        Scene currentScene = SceneManager.GetActiveScene();

        if (currentScene.path != BootingScenePath)
        {
            EditorPrefs.SetString(LastSceneKey, currentScene.path);
        }
        else
        {
            EditorPrefs.DeleteKey(LastSceneKey);
        }
    }
}
