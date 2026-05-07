using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
public class PauseManagerUI : BaseScreenController<PauseView, EmptyScreenModel>
{
    [Header("Sub-Menues")]
    [Tooltip("El Prefab de Settings que se abrirá por encima de la pausa")]
    [SerializeField] private GameObject settingsPrefab;

    [Tooltip("El lugar del Canvas donde se instanciará Settings. Si está vacío, usa este mismo objeto.")]
    [SerializeField] private Transform subMenuContainer;

    private GameObject _settingsInstance;
    private bool _isTransitioning;

    private void Awake()
    {
        if (view != null)
        {
            view.gameObject.SetActive(false);
        }
        if (model == null)
        {
            model = new EmptyScreenModel();
            model.Initialize();
        }
    }

    private void OnEnable()
    {
        PauseManager.OnPauseStateChanged += HandlePauseStateChanged;

        view.OnContinueClicked += HandleContinue;
        view.OnSettingsClicked += HandleSettings;
        view.OnExitClicked += HandleExit;
    }

    private void OnDisable()
    {
        PauseManager.OnPauseStateChanged -= HandlePauseStateChanged;

        view.OnContinueClicked -= HandleContinue;
        view.OnSettingsClicked -= HandleSettings;
        view.OnExitClicked -= HandleExit;
    }

    private void Start()
    {
        Open().Forget();
    }

    private void HandlePauseStateChanged(PauseState state)
    {
        // Evitamos que el jugador rompa la animación spameando Escape
        if (_isTransitioning) return;

        if (state == PauseState.Paused)
            OpenSafe().Forget();
        else
            CloseSafe().Forget();
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

        // Si teníamos las opciones abiertas, las cerramos para que no aparezcan mágicamente
        // la próxima vez que el jugador ponga pausa.
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
        if(settingsPrefab == null) return;
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
        PauseManager.RequestUnpause();

        SceneManager.LoadScene("MainMenu");
    }



}
