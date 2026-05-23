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
    /// Llamado por la action Player/Pause. Responsabilidad UNICA: ABRIR la pausa.
    /// El CIERRE de la pausa lo maneja UIStateManager via UI/Exit -> PauseManagerUI.RequestClose.
    ///
    /// La pausa es un OVERLAY GLOBAL: se abre encima de cualquier modal (inventario, document,
    /// SequencePanel...). Solo respeta BlocksPause (por si en el futuro alguna modal especifica
    /// no quiere ser interrumpida; hoy ninguna gameplay-UI lo bloquea).
    /// </summary>
    private void TryToggleFromInput()
    {
        if (IsPaused) return;   // si ya esta en pausa, el cierre va por UI/Exit.

        if (UIStateManager.Exists && UIStateManager.Instance.IsBlockingPause) return;

        Pause();
    }

    // -- Public Methods -------------------------
    public bool IsPaused => model.IsPaused;

    /// <summary>
    /// True cuando el gameplay debe ignorar inputs del player (movimiento, camara, interaccion).
    /// Bloquea tanto en pausa como con cualquier UI modal abierta.
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
            Debug.LogWarning("[PauseManager] La UI pidi� despausar, pero la Instancia de PauseManager no existe.");
        }
    }
}
