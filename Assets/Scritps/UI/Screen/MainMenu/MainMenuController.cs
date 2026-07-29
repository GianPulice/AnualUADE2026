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

    //To unsubscribe lambdas
    private System.Action _onNewGame;
    private System.Action _onLoadGame;
    private System.Action _onSettings;

    private void Start()
    {
        Debug.Log("<color=yellow>[MainMenuController] Forcing the start of screen zero...</color>");
        Open().Forget();
    }

    protected override void OnBeforeOpen()
    {
        Debug.Log("<color=green>Controller: doing my thing</color>");

        FreeCursor();

        // With no saved games there is nothing to load: the button starts disabled.
        view.SetLoadGameInteractable(_saveSlotsController != null && _saveSlotsController.HasAnySave);

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

    /// <summary>
    /// Cursor safety net when entering the menu.
    ///
    /// Coming from gameplay the cursor is left Locked + invisible: the PlayerCameraController
    /// locks it, and the UIStateManager snapshot reapplies it when the last modal closes.
    /// Since the PlayerCameraController is destroyed along with the level, without this the
    /// menu is left with no mouse and nothing can be clicked.
    ///
    /// It is done here (and not on every exit button) to cover all the return paths at once:
    /// Pause -> Exit, result screen -> menu, and startup.
    /// </summary>
    private void FreeCursor()
    {
        if (UIStateManager.Exists)
        {
            UIStateManager.Instance.SetCursorFree();
            return;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private async UniTask HandleNewGame()
    {
        await EnterGameplay(firstSceneLabel);
    }

    private UniTask HandleLoadGame()
    {
        if (_saveSlotsController == null)
        {
            Debug.LogError("[MainMenuController] SaveSlotsController not assigned in the Inspector.");
            return UniTask.CompletedTask;
        }

        // Second line of defence: the button should already be disabled by
        // SetLoadGameInteractable, but if something re-enables it we do not want to open empty slots.
        if (!_saveSlotsController.HasAnySave)
        {
            Debug.LogWarning("[MainMenuController] Load Game with no saved games — ignored.");
            return UniTask.CompletedTask;
        }

        _saveSlotsController.Show();
        return UniTask.CompletedTask;
    }

    private void HandleSlotSelected(int slotIndex)
    {
        SO_SaveSlotData slot = _saveSlotsController.GetSlot(slotIndex);

        // With the current pieces there is no real game restore: both an empty slot
        // ("[ new game ]") and one with data ("[ load game ]") enter the same gameplay
        // scene. Once the save system exists, this is where we decide to load the slot's
        // data (currentZoneId, modules, items). See TODO-UI · Save Slots.
        string label = firstSceneLabel;
        Debug.Log($"<color=cyan>[MainMenuController] Slot {slotIndex} " +
                  $"({(slot != null && slot.IsEmpty ? "empty -> new" : "with data -> load")}) " +
                  $"-> entering '{label}'.</color>");

        _saveSlotsController.Hide();
        EnterGameplay(label).Forget();
    }

    /// <summary>
    /// Shared New Game / Load flow: closes the menu, unloads the bootstrap,
    /// resets the result session and pushes the gameplay scene group.
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
            Debug.LogError("[MainMenuController] SettingsController.Instance is null. " +
                           "Is the UI_Settings scene loaded in the bootstrap?");
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
        // Replace "Bootstrap" with the exact name of your scene if it differs
        Scene bootstrapScene = SceneManager.GetSceneByName("Bootstrap");

        if (bootstrapScene.isLoaded)
        {
            Debug.Log("<color=orange>[MainMenuController] Unloading the Bootstrap scene...</color>");
            await SceneManager.UnloadSceneAsync(bootstrapScene);
        }
    }
    // Safety method for quick debugging in the editor
    private bool ValidateSceneGroup(string label)
    {
        if (sceneDatabase == null)
        {
            Debug.LogError("[MainMenuController] The SO_SceneList is not assigned in the Inspector.");
            return false;
        }

        bool exists = sceneDatabase.ContainsGroup(label);
        if (!exists)
            Debug.LogWarning($"[MainMenuController] Group '{label}' does not exist in the SO_SceneList.");

        return exists;
    }
}
