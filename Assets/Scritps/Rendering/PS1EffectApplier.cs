using UnityEngine;

/// <summary>
/// Aplica CRT Scanlines y PSX Dithering al material PS1Effect.mat seteando floats.
/// Requiere que el shader PS1_PostProcess.shadergraph exponga las propiedades
/// _EnableScanlines y _EnableDither (ver S6a).
///
/// NOTA: SetFloat sobre un material-asset persiste al archivo en Play Mode del Editor
/// (queda dirty en git). Aceptable porque PlayerPrefs siempre sobreescribe en Play.
/// Si molesta, declarar las props como Global en el shader y usar Shader.SetGlobalFloat.
///
/// Colocar este componente en un GameObject persistente.
/// </summary>
public class PS1EffectApplier : MonoBehaviour
{
    private const string KEY_CRT    = "Settings_CRTScanlines";
    private const string KEY_DITHER = "Settings_PSXDithering";

    private static readonly int PropScanlines = Shader.PropertyToID("_EnableScanlines");
    private static readonly int PropDither    = Shader.PropertyToID("_EnableDither");

    [Tooltip("Material PS1Effect.mat (Assets/Materials/Post Process/PS1Effect.mat).")]
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
