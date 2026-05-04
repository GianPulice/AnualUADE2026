using Cysharp.Threading.Tasks;
using UnityEngine;

public class MainMenuController : BaseScreenController<MainMenuView,EmptyScreenModel>
{
    [Header("Event Channels")]
    [SerializeField] private ScreenEventChannel screenChannel;

    [Header("Data Reference")]
    [SerializeField] private SO_SceneList sceneDatabase;

    //Para desuscribir lambdas
    private System.Action _onNewGame;
    private System.Action _onLoadGame;
    private System.Action _onSettings;

    private void Start()
    {
        Debug.Log("<color=yellow>[MainMenuController] Forzando el inicio de la pantalla cero...</color>");
        Open().Forget();
    }

    protected override void OnBeforeOpen()
    {
        Debug.Log("<color=green>Controller: estoy haciendo cosas</color>");

        _onNewGame = () => HandleNewGame().Forget();
        _onLoadGame = () => HandleLoadGame().Forget();
        _onSettings = () => HandleSettings().Forget();

        view.OnNewGameClicked += _onNewGame;
        view.OnLoadGameClicked += _onLoadGame;
        view.OnSettingsClicked += _onSettings;
        view.OnExitClicked += HandleExit;
    }

    protected override void OnBeforeClose()
    {
        view.OnNewGameClicked -= _onNewGame;
        view.OnLoadGameClicked -= _onLoadGame;
        view.OnSettingsClicked -= _onSettings;
        view.OnExitClicked -= HandleExit;
    }

    private async UniTask HandleNewGame()
    {
        if (!ValidateSceneGroup("TestIñaki")) return;     
        await Close();
        screenChannel.RaisePushScreen("TestIñaki");
    }

    private async UniTask HandleLoadGame()
    {
        if (!ValidateSceneGroup("UI_SaveSlots")) return;
        await Close();
        screenChannel.RaisePushScreen("UI_SaveSlots");
    }

    private async UniTask HandleSettings()
    {
        if (!ValidateSceneGroup("UI_Settings")) return;
        await Close();
        screenChannel.RaisePushScreen("UI_Settings");
    }

    private void HandleExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Método de seguridad para debuggear rápido en el editor
    private bool ValidateSceneGroup(string label)
    {
        if (sceneDatabase == null)
        {
            Debug.LogError("[MainMenuController] Falta asignar el SO_SceneList en el Inspector.");
            return false;
        }

        bool exists = sceneDatabase.ContainsGroup(label);
        if (!exists)
            Debug.LogWarning($"[MainMenuController] El grupo '{label}' no existe en el SO_SceneList.");

        return exists;
    }
}
