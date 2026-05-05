using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;

public class GameBootstrapper : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private SceneLoader sceneLoader;
    [SerializeField] private ScreenEventChannel screenChannel;
    [SerializeField] private SO_SceneList sceneDatabase;

    private void Start()
    {
        // Disparamos la secuencia de booteo asíncrona y la "olvidamos"
        BootGameSequenceAsync().Forget();
    }

    private async UniTask BootGameSequenceAsync()
    {
        Debug.Log("<color=yellow>[Bootstrapper] Iniciando secuencia de arranque del motor...</color>");

        // 1. Crear una lista de tareas para cargar todas las escenas persistentes
        List<UniTask> persistentLoads = new List<UniTask>();

        foreach (string sceneName in sceneDatabase.persistentSceneNames)
        {
            Debug.Log($"[Bootstrapper] Cargando infraestructura persistente: {sceneName}");
            // Usamos el SceneLoader que ya tenés para mantener todo centralizado
            persistentLoads.Add(sceneLoader.LoadSceneAdditiveAsync(sceneName));
        }

        // 2. Esperar a que TODAS las escenas persistentes terminen de cargar
        if (persistentLoads.Count > 0)
        {
            await UniTask.WhenAll(persistentLoads);
            Debug.Log("<color=green>[Bootstrapper] Toda la data persistente está lista y cargada en memoria.</color>");
        }

        // 3. Disparar la UI inicial
        // Según tu SO_SceneList, el label del menú principal es "Menu"
        Debug.Log("[Bootstrapper] Transfiriendo control al Menú Principal...");
        screenChannel.RaisePushScreen("Menu");
    }
}