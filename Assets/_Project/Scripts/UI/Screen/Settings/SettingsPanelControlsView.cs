using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls panel of the Settings screen: sensitivity + invert Y (applied by
/// CameraSensitivityApplier on Apply) and the sprint mode, hold or toggle (read by GameInput on Apply).
/// The wireframe's keybinds are static labels for now — rebinding will be done when it is
/// implemented with the InputSystem.
/// </summary>
public class SettingsPanelControlsView : MonoBehaviour
{
    [Header("Sensitivity")]
    [SerializeField] private Slider _sliderSensitivity;
    [SerializeField] private TextMeshProUGUI _labelSensitivity;

    [Header("Invert Y")]
    [SerializeField] private Toggle _toggleInvertY;

    [Header("Sprint")]
    [Tooltip("On = Shift toggles sprint. Off = hold Shift to sprint.")]
    [SerializeField] private Toggle _toggleSprintToggle;

    [Header("Crouch")]
    [Tooltip("On = Ctrl toggles crouch. Off = hold Ctrl to crouch.")]
    [SerializeField] private Toggle _toggleCrouchToggle;

    public event Action<float> OnSensitivityChanged;
    public event Action<bool>  OnInvertYChanged;
    public event Action<bool>  OnSprintToggleChanged;
    public event Action<bool>  OnCrouchToggleChanged;

    private void Awake()
    {
        if (_sliderSensitivity != null)
            _sliderSensitivity.onValueChanged.AddListener(v =>
            {
                SetLabel(_labelSensitivity, v);
                OnSensitivityChanged?.Invoke(v);
            });

        if (_toggleInvertY != null)
            _toggleInvertY.onValueChanged.AddListener(v => OnInvertYChanged?.Invoke(v));

        if (_toggleSprintToggle != null)
            _toggleSprintToggle.onValueChanged.AddListener(v => OnSprintToggleChanged?.Invoke(v));

        if (_toggleCrouchToggle != null)
            _toggleCrouchToggle.onValueChanged.AddListener(v => OnCrouchToggleChanged?.Invoke(v));
    }

    private void OnDestroy()
    {
        if (_sliderSensitivity != null) _sliderSensitivity.onValueChanged.RemoveAllListeners();
        if (_toggleInvertY != null)     _toggleInvertY.onValueChanged.RemoveAllListeners();
        if (_toggleSprintToggle != null) _toggleSprintToggle.onValueChanged.RemoveAllListeners();
        if (_toggleCrouchToggle != null) _toggleCrouchToggle.onValueChanged.RemoveAllListeners();
    }

    public void Populate(SettingsModel model)
    {
        if (model == null) return;

        if (_sliderSensitivity != null) _sliderSensitivity.SetValueWithoutNotify(model.Sensitivity);
        if (_toggleInvertY != null)     _toggleInvertY.SetIsOnWithoutNotify(model.InvertYAxis);
        if (_toggleSprintToggle != null) _toggleSprintToggle.SetIsOnWithoutNotify(model.SprintToggle);
        if (_toggleCrouchToggle != null) _toggleCrouchToggle.SetIsOnWithoutNotify(model.CrouchToggle);

        SetLabel(_labelSensitivity, model.Sensitivity);
    }

    private static void SetLabel(TextMeshProUGUI label, float value)
    {
        if (label != null) label.text = value.ToString("0.00");
    }
}
