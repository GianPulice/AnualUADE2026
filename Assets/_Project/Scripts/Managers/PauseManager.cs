using System;
using UnityEngine;
using UnityEngine.InputSystem;
public class PauseManager : Singleton<PauseManager>
{
    public static event Action<PauseState> OnPauseStateChanged;

    /// <summary>
    /// Domain-reload-disabled hygiene, same pattern as PlayerRegistry / NemesisEvents: without it
    /// the static event keeps last session's subscribers (a destroyed PauseManagerUI) and the
    /// cached instance points at a destroyed object, so the first pause of the new run fires into
    /// dead handlers — which shows up as the pause menu appearing, or the game booting frozen,
    /// every time you hit Play.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnPauseStateChanged = null;
        instance = null;
    }

    //-- Inputs ------------------------------
    [Header("Input System")]
    [SerializeField] private InputActionReference pauseAction;
    [Header("Fallback")]
    [SerializeField] private KeyCode pauseKey = KeyCode.Escape;

    //-- Model ------------------------------
    private PauseModel model;

    private Action<InputAction.CallbackContext> pauseActionHandler;

    private void Awake()
    {
        CreateSingleton(true);
        model = new PauseModel();
        model.Initialize();
        model.OnStateChanged += HandleStateChanged;
    }

    private void OnEnable()
    {
        if (pauseAction != null)
        {
            pauseAction.action.Enable();
            pauseActionHandler = _ => TryToggleFromInput();
            pauseAction.action.performed += pauseActionHandler;
        }
    }
    private void OnDisable()
    {
        if (pauseAction != null && pauseActionHandler != null)
        {
            pauseAction.action.performed -= pauseActionHandler;
            pauseActionHandler = null;
        }
    }

    private void OnDestroy() => model.OnStateChanged -= HandleStateChanged;

    private void Update()
    {
        if (pauseAction == null && Input.GetKeyDown(pauseKey)) TryToggleFromInput();
    }

    /// <summary>
    /// Called by the Player/Pause action. SINGLE responsibility: OPEN the pause menu.
    /// CLOSING the pause menu is handled by UIStateManager via UI/Exit -> PauseManagerUI.RequestClose.
    ///
    /// Pause is a GLOBAL OVERLAY: it opens on top of any modal (inventory, document,
    /// SequencePanel...). It only respects BlocksPause (in case some specific modal in the
    /// future does not want to be interrupted; today no gameplay UI blocks it).
    /// </summary>
    private void TryToggleFromInput()
    {
        if (IsPaused) return;   // if already paused, closing goes through UI/Exit.

        // No player in the loaded scenes means there is no gameplay to pause. An ESC on the main
        // menu (Player/Pause is enabled the whole run) or during a scene load would otherwise
        // latch Paused, and the next level boots frozen with no visible menu — the pause UI
        // subscribes to OnPauseStateChanged only after that state change already happened.
        if (!PlayerRegistry.HasPlayer) return;

        if (UIStateManager.Exists && UIStateManager.Instance.IsBlockingPause) return;

        Pause();
    }

    // -- Public Methods -------------------------
    public bool IsPaused => model.IsPaused;

    /// <summary>
    /// True when gameplay must ignore player input (movement, camera, interaction).
    /// Blocks both while paused and while any modal UI is open.
    /// </summary>
    public static bool IsGameplayInputBlocked
        => (Exists && Instance.IsPaused) || (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen);
    public void Pause() => model.Pause();
    public void Unpause() => model.Unpause();
    public void Toggle() => model.Toggle();

    private void HandleStateChanged(PauseState state)
    {
        OnPauseStateChanged?.Invoke(state);
    }
    public static void RequestUnpause()
    {
        if (Instance != null)
        {
            Instance.Unpause();
        }
        else
        {
            Debug.LogWarning("[PauseManager] The UI requested an unpause, but the PauseManager instance does not exist.");
        }
    }
}
