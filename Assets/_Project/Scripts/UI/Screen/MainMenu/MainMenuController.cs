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

    /// <summary>
    /// New Game was pressed with every save slot holding a save: nothing starts yet. The Save Slots
    /// panel opens asking which slot to overwrite (<see cref="SaveSlotsController.ShowOverwrite"/>);
    /// this is for anything else that wants to know.
    /// </summary>
    public event System.Action OnNoFreeSlot;

    // A second click while the first one is still taking the menu down must not start a second run.
    private bool enteringGameplay;

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

        enteringGameplay = false;

        // With no saved games there is nothing to load: the button starts disabled.
        RefreshLoadGameButton();

        _onNewGame = () => HandleNewGame().Forget();
        _onLoadGame = () => HandleLoadGame().Forget();
        _onSettings = () => HandleSettings().Forget();

        view.OnNewGameClicked += _onNewGame;
        view.OnLoadGameClicked += _onLoadGame;
        view.OnSettingsClicked += _onSettings;
        view.OnExitClicked += HandleExit;

        if (_saveSlotsController != null)
        {
            _saveSlotsController.OnSlotSelected += HandleSlotSelected;
            _saveSlotsController.OnSlotOverwrite += HandleSlotOverwrite;
            _saveSlotsController.OnSlotDeleted += HandleSlotDeleted;
        }
    }

    protected override void OnBeforeClose()
    {
        view.OnNewGameClicked -= _onNewGame;
        view.OnLoadGameClicked -= _onLoadGame;
        view.OnSettingsClicked -= _onSettings;
        view.OnExitClicked -= HandleExit;

        if (_saveSlotsController != null)
        {
            _saveSlotsController.OnSlotSelected -= HandleSlotSelected;
            _saveSlotsController.OnSlotOverwrite -= HandleSlotOverwrite;
            _saveSlotsController.OnSlotDeleted -= HandleSlotDeleted;
        }
    }

    private void RefreshLoadGameButton() =>
        view.SetLoadGameInteractable(_saveSlotsController != null && _saveSlotsController.HasAnySave);

    private void HandleSlotDeleted(int slotIndex) => RefreshLoadGameButton();

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

    /// <summary>
    /// A new run in the first free save slot (empty, else resolved). With every slot holding a save
    /// the Save Slots panel opens asking which one to overwrite (<see cref="OnNoFreeSlot"/>).
    /// </summary>
    private async UniTask HandleNewGame()
    {
        if (_saveSlotsController == null)
        {
            Debug.LogWarning("[MainMenuController] No SaveSlotsController: the new run will not be saved.");
            await StartNewRun(SaveGame.NoSlot);
            return;
        }

        int slot = _saveSlotsController.FindFirstEmptySlot();
        if (slot == SaveGame.NoSlot)
        {
            Debug.Log("[MainMenuController] New Game: every save slot is taken. Asking which one to overwrite.");
            OnNoFreeSlot?.Invoke();
            _saveSlotsController.ShowOverwrite();
            return;
        }

        await StartNewRun(slot);
    }

    private UniTask StartNewRun(int slotIndex) =>
        EnterGameplay(firstSceneLabel, () => SaveGame.BeginNewRun(slotIndex));

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

    /// <summary>
    /// A card was clicked: an empty slot ("[ new game ]") starts a new run in it, one with a save
    /// ("[ load game ]") loads it — the level it was saved at, with the run as it was then.
    /// A file that cannot be read shows as an empty card, and is treated as one.
    /// </summary>
    private void HandleSlotSelected(int slotIndex)
    {
        if (enteringGameplay) return;

        // Empty, unreadable or resolved (a won run frees its slot, as a lost one does): a new run.
        if (!SaveGame.TryReadSlot(slotIndex, out SaveData data) || data.resolved)
        {
            Debug.Log($"<color=cyan>[MainMenuController] Slot {slotIndex} is " +
                      $"{(data != null && data.resolved ? "resolved" : "empty")} -> new run.</color>");
            _saveSlotsController.Hide();
            StartNewRun(slotIndex).Forget();
            return;
        }

        if (!ValidateSceneGroup(data.sceneGroupLabel))
        {
            Debug.LogError($"[MainMenuController] Slot {slotIndex} was saved in group '{data.sceneGroupLabel}', " +
                           "which no longer exists: it cannot be loaded.");
            return;
        }

        Debug.Log($"<color=cyan>[MainMenuController] Slot {slotIndex} -> loading '{data.levelId}' " +
                  $"in '{data.sceneGroupLabel}'.</color>");

        _saveSlotsController.Hide();
        EnterGameplay(data.sceneGroupLabel, () => SaveGame.ApplyLoadedRun(data)).Forget();
    }

    /// <summary>
    /// New Game with every slot taken, and the player said YES to overwriting this one: a new run in
    /// it. The old save goes when the run begins (<see cref="SaveGame.BeginNewRun"/>), not before, so a
    /// failed start leaves it where it was.
    /// </summary>
    private void HandleSlotOverwrite(int slotIndex)
    {
        if (enteringGameplay) return;

        Debug.Log($"<color=cyan>[MainMenuController] Slot {slotIndex} overwritten -> new run.</color>");
        _saveSlotsController.Hide();
        StartNewRun(slotIndex).Forget();
    }

    /// <summary>
    /// Shared New Game / Load flow: closes the menu, unloads the bootstrap,
    /// resets the result session and pushes the gameplay scene group.
    /// </summary>
    /// <param name="afterSessionReset">Runs right after the persistent managers are cleared and
    /// before the level is pushed: where a run picks its slot, or a save is restored.</param>
    private async UniTask EnterGameplay(string sceneLabel, System.Action afterSessionReset = null)
    {
        if (enteringGameplay) return;
        if (!ValidateSceneGroup(sceneLabel)) return;

        enteringGameplay = true;

        StopMenuMusic();
        await Close();

        await UnloadBootstrapAsync();

        // Clears every persistent manager (modules, inventory, puzzles, result). New managers
        // opt in by implementing ISessionResettable — see GameSession.
        GameSession.BeginNewSession();
        afterSessionReset?.Invoke();
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
