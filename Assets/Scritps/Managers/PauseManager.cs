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
            Toggle();
    }

    // -- Public Methods -------------------------
    public bool IsPaused => model.IsPaused;
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
            Debug.LogWarning("[PauseManager] La UI pidió despausar, pero la Instancia de PauseManager no existe.");
        }
    }
}
