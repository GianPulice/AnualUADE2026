using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : BaseScreenController<MainMenuView,EmptyScreenModel>
{
    [Header("Event Channels")]
    [SerializeField] private ScreenEventChannel screenChannel;

    [Header("Data Reference")]
    [SerializeField] private SO_SceneList sceneDatabase;

    [Header("Panels")]
    [SerializeField] private SaveSlotsController _saveSlotsController;

    [Header("Temp")]
    [SerializeField] private string firstSceneLabel = "TestBlocking";

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

        if (_saveSlotsController != null)
            _saveSlotsController.OnSlotSelected += HandleSlotSelected;
    }

    protected override void OnBeforeClose()
    {
        view.OnNewGameClicked -= _onNewGame;
        view.OnLoadGameClicked -= _onLoadGame;
        view.OnSettingsClicked -= _onSettings;
        view.OnExitClicked -= HandleExit;

        if (_saveSlotsController != null)
            _saveSlotsController.OnSlotSelected -= HandleSlotSelected;
    }

    private async UniTask HandleNewGame()
    {
        await EnterGameplay(firstSceneLabel);
    }

    private UniTask HandleLoadGame()
    {
        if (_saveSlotsController == null)
        {
            Debug.LogError("[MainMenuController] Falta asignar SaveSlotsController en el Inspector.");
            return UniTask.CompletedTask;
        }
        _saveSlotsController.Show();
        return UniTask.CompletedTask;
    }

    private void HandleSlotSelected(int slotIndex)
    {
        SO_SaveSlotData slot = _saveSlotsController.GetSlot(slotIndex);

        // Con las piezas actuales no hay restore real de partida: tanto un slot vacío
        // ("[ nueva partida ]") como uno con datos ("[ cargar partida ]") entran a la
        // misma escena de gameplay. Cuando exista el save system, acá se decide cargar
        // los datos del slot (currentZoneId, módulos, items). Ver TODO-UI · Save Slots.
        string label = firstSceneLabel;
        Debug.Log($"<color=cyan>[MainMenuController] Slot {slotIndex} " +
                  $"({(slot != null && slot.IsEmpty ? "vacío → nueva" : "con datos → cargar")}) " +
                  $"→ entrando a '{label}'.</color>");

        _saveSlotsController.Hide();
        EnterGameplay(label).Forget();
    }

    /// <summary>
    /// Flujo compartido New Game / Load: cierra el menú, descarga el bootstrap,
    /// resetea la sesión de resultado y empuja el grupo de escenas de gameplay.
    /// </summary>
    private async UniTask EnterGameplay(string sceneLabel)
    {
        if (!ValidateSceneGroup(sceneLabel)) return;

        await Close();

        await UnloadBootstrapAsync();

        GameResultManager.ResetSession();
        screenChannel.RaisePushScreen(sceneLabel);
    }

    private async UniTask HandleSettings()
    {
        if (SettingsController.Instance == null)
        {
            Debug.LogError("[MainMenuController] SettingsController.Instance es null. " +
                           "¿Está la escena UI_Settings cargada en el bootstrap?");
            return;
        }

        SettingsController.Instance.OpenScreen();
        await UniTask.CompletedTask;
    }

    private void HandleExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    private async UniTask UnloadBootstrapAsync()
    {
        // Reemplaza "Bootstrap" por el nombre exacto de tu escena si es diferente
        Scene bootstrapScene = SceneManager.GetSceneByName("Bootstrap");

        if (bootstrapScene.isLoaded)
        {
            Debug.Log("<color=orange>[MainMenuController] Descargando escena Bootstrap...</color>");
            await SceneManager.UnloadSceneAsync(bootstrapScene);
        }
    }
    // M�todo de seguridad para debuggear r�pido en el editor
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
