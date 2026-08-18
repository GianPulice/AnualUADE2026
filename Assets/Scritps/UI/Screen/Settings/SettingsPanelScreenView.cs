using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Screen panel. Resolution / Mode / FPSLimit / VSync are stored in the model and applied by
/// ScreenSettingsApplier on Apply (<c>Screen.SetResolution</c> +
/// <c>Application.targetFrameRate</c> + <c>QualitySettings.vSyncCount</c>).
/// Note: <c>Screen.SetResolution</c> is a no-op in the Editor's Play Mode; test in a build.
///
/// The dropdowns are populated in <see cref="Awake"/> with the serialized options
/// (defaults from the <c>options_menu_v2_wired.html</c> wireframe). Edit from the Inspector
/// to change the order or add resolutions.
/// </summary>
public class SettingsPanelScreenView : MonoBehaviour
{
    [Header("Dropdowns")]
    [SerializeField] private TMP_Dropdown _dropdownResolution;
    [SerializeField] private TMP_Dropdown _dropdownWindowMode;
    [SerializeField] private TMP_Dropdown _dropdownFPSLimit;

    [Header("Toggle")]
    [SerializeField] private Toggle _toggleVSync;

    [Header("Dropdown options")]
    [Tooltip("Available resolutions. The indices are persisted in SettingsModel.ResolutionIndex. " +
             "Changing the order invalidates previous saves.")]
    [SerializeField]
    private string[] _resolutionOptions =
    {
        "1920 x 1080",
        "2560 x 1440",
        "3840 x 2160",
        "1366 x 768",
        "1280 x 720",
    };

    [Tooltip("Window modes. The index maps to the FullScreenMode enum when connected. " +
             "Current order: Fullscreen = ExclusiveFullScreen, Windowed = Windowed, " +
             "Borderless = FullScreenWindow.")]
    [SerializeField]
    private string[] _windowModeOptions =
    {
        "Fullscreen",
        "Windowed",
        "Borderless",
    };

    [Tooltip("FPS limits. Index 0 = no limit (-1 in Application.targetFrameRate). " +
             "The rest map directly to the numeric value.")]
    [SerializeField]
    private string[] _fpsLimitOptions =
    {
        "No limit",
        "30",
        "60",
        "120",
        "144",
    };

    public event Action<int>  OnResolutionChanged;
    public event Action<int>  OnWindowModeChanged;
    public event Action<int>  OnFPSLimitChanged;
    public event Action<bool> OnVSyncChanged;

    private void Awake()
    {
        // Populate BEFORE subscribing listeners to avoid spurious onValueChanged on init.
        PopulateDropdown(_dropdownResolution, _resolutionOptions);
        PopulateDropdown(_dropdownWindowMode, _windowModeOptions);
        PopulateDropdown(_dropdownFPSLimit,   _fpsLimitOptions);

        if (_dropdownResolution != null)
            _dropdownResolution.onValueChanged.AddListener(v => OnResolutionChanged?.Invoke(v));
        if (_dropdownWindowMode != null)
            _dropdownWindowMode.onValueChanged.AddListener(v => OnWindowModeChanged?.Invoke(v));
        if (_dropdownFPSLimit != null)
            _dropdownFPSLimit.onValueChanged.AddListener(v => OnFPSLimitChanged?.Invoke(v));
        if (_toggleVSync != null)
            _toggleVSync.onValueChanged.AddListener(v => OnVSyncChanged?.Invoke(v));
    }

    private void OnDestroy()
    {
        if (_dropdownResolution != null) _dropdownResolution.onValueChanged.RemoveAllListeners();
        if (_dropdownWindowMode != null) _dropdownWindowMode.onValueChanged.RemoveAllListeners();
        if (_dropdownFPSLimit != null)   _dropdownFPSLimit.onValueChanged.RemoveAllListeners();
        if (_toggleVSync != null)        _toggleVSync.onValueChanged.RemoveAllListeners();
    }

    public void Populate(SettingsModel model)
    {
        if (model == null) return;
        if (_dropdownResolution != null) _dropdownResolution.SetValueWithoutNotify(model.ResolutionIndex);
        if (_dropdownWindowMode != null) _dropdownWindowMode.SetValueWithoutNotify(model.WindowMode);
        if (_dropdownFPSLimit != null)   _dropdownFPSLimit.SetValueWithoutNotify(model.FPSLimit);
        if (_toggleVSync != null)        _toggleVSync.SetIsOnWithoutNotify(model.VSync);
    }

    private static void PopulateDropdown(TMP_Dropdown dropdown, string[] options)
    {
        if (dropdown == null || options == null) return;
        dropdown.ClearOptions();

        var optionData = new System.Collections.Generic.List<TMP_Dropdown.OptionData>(options.Length);
        foreach (string label in options)
            optionData.Add(new TMP_Dropdown.OptionData(label));

        dropdown.AddOptions(optionData);
    }
}
