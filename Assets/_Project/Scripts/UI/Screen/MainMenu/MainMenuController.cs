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

    [Header("Audio")]
    [Tooltip("Menu music: SO_SoundData ids (Music category), played in this order by a " +
             "MusicCrossfadePlaylist. Starts when the menu opens and fades out when the game starts. " +
             "Empty = silent menu.")]
    [SerializeField, SoundId] private string[] menuMusicIds = { "mus_mainmenu", "mus_mainmenu_alt_01" };

    [Tooltip("Times each track plays, whole, before the next one takes over (2 = A A B B A A ...).")]
    [SerializeField, Min(1)] private int menuMusicPlaysPerTrack = 2;

    [Tooltip("Seconds of overlap at every transition, between two tracks and between two plays of " +
             "the same one. It is what hides the loop point. Clamped to a quarter of the shortest clip.")]
    [SerializeField, Min(0.25f)] private float menuMusicCrossfade = 4f;

    [Tooltip("Seconds the music takes to fade out when the game starts.")]
    [SerializeField, Min(0f)] private float menuMusicFadeOut = 1.5f;

    private MusicCrossfadePlaylist menuMusic;

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
        PlayMenuMusic();

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

    // AudioManager lives in the Data scene; with the menu opened on its own there may be none.
    // The playlist hangs from the AudioManager and not from the menu, so its fade-out can finish
    // after the menu scene is unloaded.
    private void PlayMenuMusic()
    {
        if (menuMusicIds == null || menuMusicIds.Length == 0 || !AudioManager.Exists) return;
        if (menuMusic != null && menuMusic.IsActive) return;

        var tracks = new System.Collections.Generic.List<SO_SoundData>(menuMusicIds.Length);
        foreach (string id in menuMusicIds)
        {
            SO_SoundData data = AudioManager.Instance.GetSound(id);
            if (data == null)
                Debug.LogWarning($"[MainMenuController] Menu music id '{id}' is not registered in the AudioManager.");
            else
                tracks.Add(data);
        }
        if (tracks.Count == 0) return;

        menuMusic = MusicCrossfadePlaylist.Create(AudioManager.Instance.transform, "MenuMusic");
        menuMusic.Begin(tracks, menuMusicPlaysPerTrack, menuMusicCrossfade);
    }

    private void StopMenuMusic()
    {
        if (menuMusic == null) return;
        menuMusic.FadeOutAndDestroy(menuMusicFadeOut);
        menuMusic = null;
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

        StopMenuMusic();
        await Close();

        await UnloadBootstrapAsync();

        // Clears every persistent manager (modules, inventory, puzzles, result). New managers
        // opt in by implementing ISessionResettable — see GameSession.
        GameSession.BeginNewSession();
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

    // Through the ScreenManager so quitting gets the same fade and loading screen as a scene change.
    private void HandleExit() => ScreenManager.RequestQuit();
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
