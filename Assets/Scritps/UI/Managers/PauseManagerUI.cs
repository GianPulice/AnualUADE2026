using Cysharp.Threading.Tasks;
using UnityEngine;

public class PauseManagerUI : BaseScreenController<PauseView, EmptyScreenModel>
{
    [Header("Arquitectura (MVC)")]
    [Tooltip("Necesitamos el canal de eventos para salir correctamente al Menú Principal")]
    [SerializeField] private ScreenEventChannel screenChannel;

    [Header("Sub-Menues")]
    [Tooltip("El Prefab de Settings que se abrirá por encima de la pausa")]
    [SerializeField] private GameObject settingsPrefab;

    [Tooltip("El lugar del Canvas donde se instanciará Settings. Si está vacío, usa este mismo objeto.")]
    [SerializeField] private Transform subMenuContainer;

    private GameObject _settingsInstance;
    private bool _isTransitioning;

    private void Awake()
    {
        if (view == null)
        {
            Debug.LogError($"[{nameof(PauseManagerUI)}] view no asignada en el Inspector.");
            return;
        }

        view.gameObject.SetActive(false); // Nos aseguramos de que arranque apagado

        if (model == null)
        {
            model = new EmptyScreenModel();
            model.Initialize();
        }

        PauseManager.OnPauseStateChanged += HandlePauseStateChanged;

        view.OnContinueClicked += HandleContinue;
        view.OnSettingsClicked += HandleSettings;
        view.OnExitClicked     += HandleExit;
    }

    private void OnDestroy()
    {
        PauseManager.OnPauseStateChanged -= HandlePauseStateChanged;

        if (view == null) return;

        view.OnContinueClicked -= HandleContinue;
        view.OnSettingsClicked -= HandleSettings;
        view.OnExitClicked     -= HandleExit;
    }
    private void HandlePauseStateChanged(PauseState state)
    {
        if (_isTransitioning) return;

        if (state == PauseState.Paused)
            OpenSafe().Forget();
        else
            CloseSafe().Forget();
    }

    protected override void OnBeforeOpen()
    {
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    protected override void OnBeforeClose()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (_settingsInstance != null)
        {
            _settingsInstance.SetActive(false);
        }
        view.ResetButtonStates();
    }

    private async UniTaskVoid OpenSafe()
    {
        _isTransitioning = true;
        await Open();
        _isTransitioning = false;
    }

    private async UniTaskVoid CloseSafe()
    {
        _isTransitioning = true;
        if (_settingsInstance != null)
        {
            _settingsInstance.SetActive(false);
        }

        await Close();
        _isTransitioning = false;
    }

    // ── Reacción a los Botones de la UI ───────────────────────

    private void HandleContinue()
    {
        PauseManager.RequestUnpause();
    }

    private void HandleSettings()
    {
        if (settingsPrefab == null) return;
        if (_settingsInstance == null)
        {
            Transform parent = subMenuContainer != null ? subMenuContainer : transform;
            _settingsInstance = Instantiate(settingsPrefab, parent);
        }
        else
        {
            _settingsInstance.SetActive(true);
        }
    }

    private void HandleExit()
    {
        _isTransitioning = true;

        Time.timeScale = 1f;

        PauseManager.RequestUnpause();

        if (screenChannel != null)
            screenChannel.RaisePushScreen("Menu");
        else
            Debug.LogError("[PauseManagerUI] Falta asignar el ScreenEventChannel.");
    }

}
