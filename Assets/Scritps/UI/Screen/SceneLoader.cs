using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

public class SceneLoader : MonoBehaviour
{
    /// <summary>
    /// Carga una escena de forma aditiva y reporta el progreso.
    /// </summary>
    /// <param name="sceneName">Nombre de la escena a cargar.</param>
    /// <param name="progress">Objeto que escuchará el progreso (0.0f a 1.0f). Puede ser null.</param>
    public async UniTask LoadSceneAdditiveAsync(string sceneName, IProgress<float> progress = null)
    {
        // Iniciamos la carga asíncrona de Unity
        AsyncOperation asyncOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

        if (asyncOperation == null)
        {
            Debug.LogError($"[SceneLoader] No se pudo cargar la escena '{sceneName}'. ¿Está en los Build Settings?");
            return;
        }

        // Magia de UniTask: Convierte el AsyncOperation en una Task y le pasamos el reporte de progreso
        await asyncOperation.ToUniTask(progress: progress);
    }

    /// <summary>
    /// Descarga una escena aditiva de la memoria.
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
