using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Aplica la sensibilidad guardada en PlayerPrefs al CinemachineInputAxisController del rig
/// de cámara. Multiplica los Gain base de cada axis. Se actualiza cuando SettingsModel.Apply()
/// dispara el evento estático OnSettingsApplied.
///
/// Colocar este componente en el mismo GameObject que tenga el CinemachineInputAxisController.
/// </summary>
[RequireComponent(typeof(CinemachineInputAxisController))]
public class CameraSensitivityApplier : MonoBehaviour
{
    private const string KEY_SENSITIVITY = "Settings_Sensitivity";
    private const float  DEFAULT_SENSITIVITY = 1f;

    private CinemachineInputAxisController _inputController;
    private float[] _baseGains; // gain original por axis (capturado en Start)

    private void Awake()
    {
        _inputController = GetComponent<CinemachineInputAxisController>();
    }

    private void Start()
    {
        CacheBaseGains();
        ApplySensitivity(LoadSensitivity());
    }

    private void OnEnable()  => SettingsModel.OnSettingsApplied += HandleSettingsApplied;
    private void OnDisable() => SettingsModel.OnSettingsApplied -= HandleSettingsApplied;

    private void HandleSettingsApplied() => ApplySensitivity(LoadSensitivity());

    private void CacheBaseGains()
    {
        if (_inputController == null || _inputController.Controllers == null) return;
        _baseGains = new float[_inputController.Controllers.Count];
        for (int i = 0; i < _inputController.Controllers.Count; i++)
        {
            _baseGains[i] = _inputController.Controllers[i].Input.Gain;
        }
    }

    private void ApplySensitivity(float sensitivity)
    {
        if (_inputController == null || _baseGains == null) return;
        for (int i = 0; i < _inputController.Controllers.Count && i < _baseGains.Length; i++)
        {
            var entry = _inputController.Controllers[i];
            entry.Input.Gain = _baseGains[i] * sensitivity;
        }
    }

    private static float LoadSensitivity() =>
        PlayerPrefs.GetFloat(KEY_SENSITIVITY, DEFAULT_SENSITIVITY);
}
