using UnityEngine;

/// <summary>
/// Applies CRT Scanlines and PSX Dithering to the PS1Effect.mat material by setting floats.
/// Requires the PS1_PostProcess.shadergraph shader to expose the _EnableScanlines and
/// _EnableDither properties (see S6a).
///
/// NOTE: SetFloat on a material asset persists to the file in the Editor's Play Mode
/// (it stays dirty in git). Acceptable because PlayerPrefs always overwrites it on Play.
/// If it becomes a nuisance, declare the props as Global in the shader and use Shader.SetGlobalFloat.
///
/// Place this component on a persistent GameObject.
/// </summary>
public class PS1EffectApplier : MonoBehaviour
{
    private const string KEY_CRT    = "Settings_CRTScanlines";
    private const string KEY_DITHER = "Settings_PSXDithering";

    private static readonly int PropScanlines = Shader.PropertyToID("_EnableScanlines");
    private static readonly int PropDither    = Shader.PropertyToID("_EnableDither");

    [Tooltip("PS1Effect.mat material (Assets/Materials/Post Process/PS1Effect.mat).")]
    [SerializeField] private Material _ps1Material;

    private void Awake()
    {
        SettingsModel.OnSettingsApplied += Apply;
        Apply();
    }

    private void OnDestroy() => SettingsModel.OnSettingsApplied -= Apply;

    private void Apply()
    {
        if (_ps1Material == null) return;

        bool crt    = PlayerPrefs.GetInt(KEY_CRT,    1) != 0;
        bool dither = PlayerPrefs.GetInt(KEY_DITHER, 1) != 0;

        _ps1Material.SetFloat(PropScanlines, crt    ? 1f : 0f);
        _ps1Material.SetFloat(PropDither,    dither ? 1f : 0f);
    }
}
