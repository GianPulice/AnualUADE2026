using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Aplica la sensibilidad guardada en PlayerPrefs al CinemachineInputAxisController del rig
/// de cámara. Multiplica los Gain base de cada axis y, si Invert Y está activo, niega el gain
/// del axis vertical. Se actualiza cuando SettingsModel.Apply() dispara OnSettingsApplied.
///
/// Colocar este componente en el mismo GameObject que tenga el CinemachineInputAxisController.
/// </summary>
[RequireComponent(typeof(CinemachineInputAxisController))]
public class CameraSensitivityApplier : MonoBehaviour
{
    private const string KEY_SENSITIVITY = "Settings_Sensitivity";
    private const string KEY_INVERT_Y    = "Settings_InvertYAxis";
    private const float  DEFAULT_SENSITIVITY = 1f;

    [Tooltip("Si el heurístico por nombre no encuentra el axis vertical, usar este nombre exacto " +
             "(ej. 'Look Orbit Y'). Dejar vacío para usar solo el heurístico.")]
    [SerializeField] private string _verticalAxisName = "";

    private CinemachineInputAxisController _inputController;
    private float[] _baseGains;
    private bool[]  _isVertical;

    private void Awake()
    {
        _inputController = GetComponent<CinemachineInputAxisController>();
    }

    private void Start()
    {
        CacheBaseGains();
        ApplySensitivity();
    }

    private void OnEnable()  => SettingsModel.OnSettingsApplied += HandleSettingsApplied;
    private void OnDisable() => SettingsModel.OnSettingsApplied -= HandleSettingsApplied;

    private void HandleSettingsApplied() => ApplySensitivity();

    private void CacheBaseGains()
    {
        if (_inputController == null || _inputController.Controllers == null) return;
        int n = _inputController.Controllers.Count;
        _baseGains  = new float[n];
        _isVertical = new bool[n];
        for (int i = 0; i < n; i++)
        {
            _baseGains[i] = _inputController.Controllers[i].Input.Gain;
            string name = _inputController.Controllers[i].Name ?? "";
            _isVertical[i] = IsVerticalAxis(name);
        }
    }

    private bool IsVerticalAxis(string name)
    {
        if (!string.IsNullOrEmpty(_verticalAxisName) && name == _verticalAxisName) return true;
        return name.Contains("Y") || name.Contains("Vertical")
            || name.Contains("Pitch") || name.Contains("Tilt");
    }

    private void ApplySensitivity()
    {
        if (_inputController == null || _baseGains == null) return;

        float sensitivity = PlayerPrefs.GetFloat(KEY_SENSITIVITY, DEFAULT_SENSITIVITY);
        bool  invertY     = PlayerPrefs.GetInt(KEY_INVERT_Y, 0) != 0;

        int count = _inputController.Controllers.Count;
        for (int i = 0; i < count && i < _baseGains.Length; i++)
        {
            float gain = _baseGains[i] * sensitivity;
            if (invertY && _isVertical != null && i < _isVertical.Length && _isVertical[i])
                gain = -gain;
            _inputController.Controllers[i].Input.Gain = gain;
        }
    }
}
