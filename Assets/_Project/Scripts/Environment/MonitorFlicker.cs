using UnityEngine;

/// <summary>
/// Softly pulses the emission of a PBR material (URP/Lit) to simulate the flicker of a
/// monitor screen. flickerSpeed is in Hz (0.2 = one cycle every 5 seconds).
///
/// Setup:
///   1. Apply `mat_monitor_pantalla` to the monitor's Renderer.
///   2. Assign the Renderer to the `_renderer` field (or leave it empty to autodetect).
///   3. Tune baseEmission to the desired HDR color for the screen.
///
/// Uses MaterialPropertyBlock → does not instance materials, does not break batching.
/// Time.time (scaled by timeScale) → the flicker freezes when the game is paused.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class MonitorFlicker : MonoBehaviour
{
    [Header("Emission")]
    [ColorUsage(showAlpha: true, hdr: true)]
    [SerializeField] private Color baseEmission = Color.white;

    [Header("Pulse")]
    [Tooltip("Minimum pulse intensity (multiplies baseEmission).")]
    [SerializeField] private float minIntensity = 0.9f;

    [Tooltip("Maximum pulse intensity (multiplies baseEmission).")]
    [SerializeField] private float maxIntensity = 1.0f;

    [Tooltip("Flicker frequency in Hz. 0.2 = one full cycle every 5 seconds.")]
    [SerializeField] private float flickerSpeed = 0.2f;

    [Tooltip("Deterministic per-instance offset (in seconds). Lets several monitors be desynchronized.")]
    [SerializeField] private float flickerOffset = 0f;

    [Header("Target")]
    [SerializeField] private Renderer _renderer;

    private MaterialPropertyBlock _propBlock;
    private int _emissionId;

    private void Awake()
    {
        if (_renderer == null) _renderer = GetComponent<Renderer>();
        _propBlock = new MaterialPropertyBlock();
        _emissionId = Shader.PropertyToID("_EmissionColor");
    }

    private void Update()
    {
        // sin01 ∈ [0,1], smooth, at flickerSpeed Hz.
        float phase = (Time.time + flickerOffset) * flickerSpeed * 2f * Mathf.PI;
        float sin01 = (Mathf.Sin(phase) + 1f) * 0.5f;

        float intensity = Mathf.Lerp(minIntensity, maxIntensity, sin01);

        _renderer.GetPropertyBlock(_propBlock);
        _propBlock.SetColor(_emissionId, baseEmission * intensity);
        _renderer.SetPropertyBlock(_propBlock);
    }
}
