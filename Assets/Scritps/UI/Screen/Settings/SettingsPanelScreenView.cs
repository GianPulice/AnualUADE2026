using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Panel de Screen. Resolución / Modo / FPSLimit / VSync se guardan en el modelo y los
/// aplica ScreenSettingsApplier al hacer Apply (<c>Screen.SetResolution</c> +
/// <c>Application.targetFrameRate</c> + <c>QualitySettings.vSyncCount</c>).
/// Nota: <c>Screen.SetResolution</c> es no-op en Play Mode del Editor; probar en build.
///
/// Los dropdowns se pueblan en <see cref="Awake"/> con las opciones serializadas
/// (defaults del wireframe <c>options_menu_v2_wired.html</c>). Editar desde el Inspector
/// si querés cambiar el orden o agregar resoluciones.
/// </summary>
public class SettingsPanelScreenView : MonoBehaviour
{
    [Header("Dropdowns")]
    [SerializeField] private TMP_Dropdown _dropdownResolution;
    [SerializeField] private TMP_Dropdown _dropdownWindowMode;
    [SerializeField] private TMP_Dropdown _dropdownFPSLimit;

    [Header("Toggle")]
    [SerializeField] private Toggle _toggleVSync;

    [Header("Opciones de los dropdowns")]
    [Tooltip("Resoluciones disponibles. Los índices se persisten en SettingsModel.ResolutionIndex. " +
             "Cambiar el orden invalida saves previos.")]
    [SerializeField]
    private string[] _resolutionOptions =
    {
        "1920 x 1080",
        "2560 x 1440",
        "3840 x 2160",
        "1366 x 768",
        "1280 x 720",
    };

    [Tooltip("Modos de ventana. El índice mapea al enum FullScreenMode cuando se conecte. " +
             "Orden actual: Pantalla completa = ExclusiveFullScreen, Ventana = Windowed, " +
             "Sin bordes = FullScreenWindow.")]
    [SerializeField]
    private string[] _windowModeOptions =
    {
        "Pantalla completa",
        "Ventana",
        "Sin bordes",
    };

    [Tooltip("Límites de FPS. El índice 0 = sin límite (-1 en Application.targetFrameRate). " +
             "El resto mapea directo al valor numérico.")]
    [SerializeField]
    private string[] _fpsLimitOptions =
    {
        "Sin limite",
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
        // Poblar ANTES de suscribir listeners para evitar onValueChanged espurios al inicializar.
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
