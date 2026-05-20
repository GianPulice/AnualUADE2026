using Cysharp.Threading.Tasks;
using UnityEngine;

public class SettingsController : BaseScreenController<SettingsView, SettingsModel>
{
    [Header("Input")]
    [SerializeField] private KeyCode _closeKey = KeyCode.Escape;

    private bool _isTransitioning;

    private void Awake()
    {
        model = new SettingsModel();
        model.Initialize();

        if (view != null) view.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        view.OnApplyClicked       += HandleApply;
        view.OnBackClicked        += HandleBack;
        view.OnMasterChanged      += model.SetMasterVolume;
        view.OnMusicChanged       += model.SetMusicVolume;
        view.OnSFXChanged         += model.SetSFXVolume;
        view.OnSensitivityChanged += model.SetSensitivity;
    }

    private void OnDisable()
    {
        view.OnApplyClicked       -= HandleApply;
        view.OnBackClicked        -= HandleBack;
        view.OnMasterChanged      -= model.SetMasterVolume;
        view.OnMusicChanged       -= model.SetMusicVolume;
        view.OnSFXChanged         -= model.SetSFXVolume;
        view.OnSensitivityChanged -= model.SetSensitivity;
    }

    private void Update()
    {
        if (IsActive && Input.GetKeyDown(_closeKey))
            HandleBack();
    }

    // Se llama cada vez que se abre: recarga PlayerPrefs y toma snapshot
    protected override void OnBeforeOpen()
    {
        model.Initialize();
        view.Populate(model);
    }

    // ── Botones ──────────────────────────────────────────────────

    private void HandleApply()
    {
        model.Apply();
        CloseSafe().Forget();
    }

    private void HandleBack()
    {
        model.Revert();
        CloseSafe().Forget();
    }

    private async UniTaskVoid CloseSafe()
    {
        if (_isTransitioning) return;
        _isTransitioning = true;
        await Close();
        _isTransitioning = false;
    }
}
