using UnityEngine;

/// <summary>
/// Controller del glitch VHS + chromatic aberration del filtro PS1.
///
/// Spec §6.10 de <c>WIRED_Handoff_Code.docx</c>:
///   - Intervalo entre glitches: 8–45 s (Random).
///   - Duración del glitch: 0.1–0.4 s.
///   - Sin trigger de gameplay — puramente estético.
///   - No dispara durante: inventario abierto, skill check, examine.
///   - Sí durante: gameplay, menús, pausa. En pausa el overlay se congela.
///
/// Escribe <c>_ChromaticAberrationOffset</c> sobre <see cref="ps1Material"/> —
/// no toca ninguna otra property (dither / scanlines los maneja <c>PS1EffectApplier</c>).
///
/// Accesibilidad: toggle desde Options → PlayerPrefs <c>Settings_VHSGlitch</c>.
/// Si el jugador lo apaga, el controller no dispara.
/// </summary>
public class GlitchController : MonoBehaviour
{
    private const string KEY_GLITCH = "Settings_VHSGlitch";
    private static readonly int PropCAOffset = Shader.PropertyToID("_ChromaticAberrationOffset");

    [Tooltip("Material PS1Effect.mat sobre el que se pulsa la CA. Mismo material que usa PS1EffectApplier.")]
    [SerializeField] private Material ps1Material;

    [Header("Timing (segundos)")]
    [SerializeField] private Vector2 intervalRange  = new Vector2(8f, 45f);
    [SerializeField] private Vector2 durationRange  = new Vector2(0.1f, 0.4f);

    [Header("CA amount")]
    [Tooltip("Offset máximo aplicado durante el glitch. Base del shader = 0.003, spec sugiere hasta 0.008 para un flash claro.")]
    [Range(0f, 0.02f)]
    [SerializeField] private float maxCAOffset = 0.008f;

    /// <summary>
    /// Externo: subir a true para congelar la ejecución (durante inventario / skill check / examine).
    /// El overlay actual queda visible pero el timer se pausa.
    /// </summary>
    public static bool SuspendTriggering { get; set; }

    private float _nextTriggerAt;
    private float _glitchEndsAt;
    private bool  _isGlitching;
    private bool  _isEnabled;

    private void OnEnable()
    {
        SettingsModel.OnSettingsApplied += Apply;
        Apply();
        ScheduleNext();
    }

    private void OnDisable()
    {
        SettingsModel.OnSettingsApplied -= Apply;
        ResetCA();
    }

    private void Update()
    {
        if (!_isEnabled || ps1Material == null) return;

        // Time.unscaledTime para que corra también en pausa — §6.10 dice "sí durante menús, pausa".
        float now = Time.unscaledTime;

        if (_isGlitching)
        {
            if (now >= _glitchEndsAt)
            {
                ResetCA();
                _isGlitching = false;
                ScheduleNext();
            }
            return;
        }

        if (SuspendTriggering) return;
        if (now < _nextTriggerAt) return;

        // Fire!
        float duration = Random.Range(durationRange.x, durationRange.y);
        float amount   = Random.Range(maxCAOffset * 0.4f, maxCAOffset);

        ps1Material.SetFloat(PropCAOffset, amount);
        _glitchEndsAt = now + duration;
        _isGlitching  = true;
    }

    private void Apply()
    {
        _isEnabled = PlayerPrefs.GetInt(KEY_GLITCH, 1) != 0;
        if (!_isEnabled) ResetCA();
    }

    private void ScheduleNext()
    {
        _nextTriggerAt = Time.unscaledTime + Random.Range(intervalRange.x, intervalRange.y);
    }

    private void ResetCA()
    {
        if (ps1Material != null) ps1Material.SetFloat(PropCAOffset, 0f);
    }
}
