using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

public class SceneLoader : MonoBehaviour
{
    /// <summary>
    /// Loads a scene additively and reports progress.
    /// </summary>
    /// <param name="sceneName">Name of the scene to load.</param>
    /// <param name="progress">Object that will listen to the progress (0.0f to 1.0f). Can be null.</param>
    public async UniTask LoadSceneAdditiveAsync(string sceneName, IProgress<float> progress = null)
    {
        // Start Unity's async load
        AsyncOperation asyncOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

        if (asyncOperation == null)
        {
            Debug.LogError($"[SceneLoader] Could not load scene '{sceneName}'. Is it in the Build Settings?");
            return;
        }

        // UniTask magic: converts the AsyncOperation into a Task and forwards the progress report
        await asyncOperation.ToUniTask(progress: progress);
    }

    /// <summary>
    /// Unloads an additive scene from memory.
    /// </summary>
    public async UniTask UnloadSceneAsync(string sceneName)
    {
        AsyncOperation asyncOperation = SceneManager.UnloadSceneAsync(sceneName);

        if (asyncOperation != null)
        {
            await asyncOperation.ToUniTask();
        }
    }
}
