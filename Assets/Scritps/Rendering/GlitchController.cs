using UnityEngine;

/// <summary>
/// Controller of the VHS glitch + chromatic aberration of the PS1 filter.
///
/// Spec §6.10 of <c>WIRED_Handoff_Code.docx</c>:
///   - Interval between glitches: 8–45 s (Random).
///   - Glitch duration: 0.1–0.4 s.
///   - No gameplay trigger — purely aesthetic.
///   - Does not fire during: inventory open, skill check, examine.
///   - Does fire during: gameplay, menus, pause. During pause the overlay freezes.
///
/// Writes <c>_ChromaticAberrationOffset</c> on <see cref="ps1Material"/> —
/// it touches no other property (dither / scanlines are handled by <c>PS1EffectApplier</c>).
///
/// Accessibility: toggle from Options → PlayerPrefs <c>Settings_VHSGlitch</c>.
/// If the player turns it off, the controller does not fire.
/// </summary>
public class GlitchController : MonoBehaviour
{
    private const string KEY_GLITCH = "Settings_VHSGlitch";
    private static readonly int PropCAOffset = Shader.PropertyToID("_ChromaticAberrationOffset");

    [Tooltip("PS1Effect.mat material the CA is pulsed on. Same material PS1EffectApplier uses.")]
    [SerializeField] private Material ps1Material;

    [Header("Timing (seconds)")]
    [SerializeField] private Vector2 intervalRange  = new Vector2(8f, 45f);
    [SerializeField] private Vector2 durationRange  = new Vector2(0.1f, 0.4f);

    [Header("CA amount")]
    [Tooltip("Maximum offset applied during the glitch. Shader base = 0.003, the spec suggests up to 0.008 for a clear flash.")]
    [Range(0f, 0.02f)]
    [SerializeField] private float maxCAOffset = 0.008f;

    /// <summary>
    /// External: set to true to freeze execution (during inventory / skill check / examine).
    /// The current overlay stays visible but the timer pauses.
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

        // Time.unscaledTime so it also runs during pause — §6.10 says "yes during menus, pause".
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
