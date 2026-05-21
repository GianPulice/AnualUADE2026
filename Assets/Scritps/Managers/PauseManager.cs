using System;
using UnityEngine;
using UnityEngine.InputSystem;
public class PauseManager : Singleton<PauseManager>
{
    public static event Action<PauseState> OnPauseStateChanged;

    //-- Inputs ------------------------------
    [Header("Input System")]
    [SerializeField] private InputActionReference pauseAction;
    [Header("Fallback")]
    [SerializeField] private KeyCode pauseKey = KeyCode.Escape;

    //-- Model ------------------------------
    private PauseModel model;

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
            pauseAction.action.performed += _ => Toggle();
        }
    }
    private void OnDisable()
    {
        if (pauseAction != null)
        {
            pauseAction.action.performed -= _ => Toggle();
        }
    }

    private void OnDestroy() => model.OnStateChanged -= HandleStateChanged;

    private void Update()
    {
        if (pauseAction == null && Input.GetKeyDown(pauseKey))
        {
            // Si hay una UI bloqueante abierta, el ESC va a ella en lugar de a la pausa.
            if (IsBlockingUIOpen()) return;
            Toggle();
        }
    }

    /// <summary>
    /// True cuando hay una UI modal abierta que debe consumir el ESC antes que la pausa.
    /// </summary>
    private static bool IsBlockingUIOpen()
    {
        if (SequencePanelUIController.Exists && SequencePanelUIController.Instance.IsOpen) return true;
        if (InventoryManagerUI.Instance != null && InventoryManagerUI.Instance.IsInventoryOpen) return true;
        if (DocumentReaderController.Instance != null && DocumentReaderController.Instance.IsOpen) return true;
        if (SettingsController.Instance != null && SettingsController.Instance.IsOpen) return true;
        return false;
    }

    // -- Public Methods -------------------------
    public bool IsPaused => model.IsPaused;

    /// <summary>True cuando el gameplay debe ignorar inputs del player (movimiento, agacharse, etc.).</summary>
    public static bool IsGameplayInputBlocked => Exists && Instance.IsPaused;
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
            Debug.LogWarning("[PauseManager] La UI pidi� despausar, pero la Instancia de PauseManager no existe.");
        }
    }
}
