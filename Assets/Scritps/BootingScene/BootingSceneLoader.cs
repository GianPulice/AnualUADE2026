using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class BootingSceneLoader : MonoBehaviour
{
    [Header("Solamente funciona si se ejecuta en modo build")]
    [SerializeField] private string defaultSceneName = "MainMenu";

#if UNITY_EDITOR
    private const string LastSceneKey = "LAST_PLAY_MODE_SCENE_PATH";
#endif

    private static bool alreadyLoaded;

    private async void Start()
    {
        if (alreadyLoaded) return;

        alreadyLoaded = true;

        await UniTask.Yield();

        string sceneToLoad = GetSceneToLoad();

        Debug.Log($"[BootingSceneLoader] Cargando escena: {sceneToLoad}");

        await SceneManager.LoadSceneAsync(sceneToLoad, LoadSceneMode.Single);
    }

    private string GetSceneToLoad()
    {
#if UNITY_EDITOR
        string scenePath = EditorPrefs.GetString(LastSceneKey, "");

        if (!string.IsNullOrEmpty(scenePath))
        {
            return System.IO.Path.GetFileNameWithoutExtension(scenePath);
        }
#endif

        return defaultSceneName;
    }
}
