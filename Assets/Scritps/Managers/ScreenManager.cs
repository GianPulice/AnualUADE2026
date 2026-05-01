using UnityEngine;
using System.Linq;
using Cysharp.Threading.Tasks;

public class ScreenManager : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private SceneLoader sceneLoader;
    [SerializeField] private ScreenEventChannel screenChannel;
    [SerializeField] private SO_SceneList sceneDatabase;

    private string currentActiveScene;
    private void Awake()
    {
        if (sceneDatabase == null) Debug.LogError("[ScreenManager] Faltan asignar el SO_SceneList!");
        if (screenChannel == null) Debug.LogError("[ScreenManager] Faltan asignar el Event Channel!");
    }
    private void OnEnable()
    {
        Debug.Log($"<color=cyan>[ScreenManager] estoy habilitado");
        if (screenChannel != null)
            screenChannel.OnPushScreenRequested += OnPushScreenRequestedWrapper; 
    }

    private void OnDisable()
    {
        if (screenChannel != null)
            screenChannel.OnPushScreenRequested -= OnPushScreenRequestedWrapper; 
    }
    private void OnPushScreenRequestedWrapper(string screenLabel)
    {
        Debug.Log($"<color=cyan>[ScreenManager] ¡WRAPPER EJECUTADO! Recibí la señal para: {screenLabel}</color>");
        HandlePushScreenAsync(screenLabel).Forget();
    }
    private async UniTask HandlePushScreenAsync(string screenLabel)
    {
        Debug.Log($"[ScreenManager] Intentando buscar el label: '{screenLabel}'"); // Verifica espacios o tildes

        var entry = sceneDatabase.scenes.FirstOrDefault(s => s.label == screenLabel);

        if (entry != null)
        {
            Debug.Log($"[ScreenManager] Cargando: {entry.sceneName} desde el label: {screenLabel}");
            await sceneLoader.LoadSceneAdditiveAsync(entry.sceneName);
        }
        else
        {
            Debug.LogError($"[ScreenManager] No se encontró ninguna escena con el label '{screenLabel}' en el SO.");
        }
    }
}
