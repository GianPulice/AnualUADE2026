using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel de Controls del Settings: sensibilidad (funcional) + invertir Y (placeholder).
/// Los keybinds del wireframe son labels estáticos por ahora — el rebinding se hará
/// cuando se implemente con InputSystem.
/// </summary>
public class SettingsPanelControlsView : MonoBehaviour
{
    [Header("Sensibilidad")]
    [SerializeField] private Slider _sliderSensitivity;
    [SerializeField] private TextMeshProUGUI _labelSensitivity;

    [Header("Invertir Y (placeholder)")]
    [SerializeField] private Toggle _toggleInvertY;

    public event Action<float> OnSensitivityChanged;
    public event Action<bool>  OnInvertYChanged;

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
    }

    private void OnDestroy()
    {
        if (_sliderSensitivity != null) _sliderSensitivity.onValueChanged.RemoveAllListeners();
        if (_toggleInvertY != null)     _toggleInvertY.onValueChanged.RemoveAllListeners();
    }

    public void Populate(SettingsModel model)
    {
        if (model == null) return;

        if (_sliderSensitivity != null) _sliderSensitivity.SetValueWithoutNotify(model.Sensitivity);
        if (_toggleInvertY != null)     _toggleInvertY.SetIsOnWithoutNotify(model.InvertYAxis);

        SetLabel(_labelSensitivity, model.Sensitivity);
    }

    private static void SetLabel(TextMeshProUGUI label, float value)
    {
        if (label != null) label.text = value.ToString("0.00");
    }
}
