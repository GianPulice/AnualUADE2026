using UnityEngine;

/// <summary>
/// Pulsa suavemente el emission de un material PBR (URP/Lit) para simular el flicker
/// de una pantalla de monitor. flickerSpeed en Hz (0.2 = un ciclo cada 5 segundos).
///
/// Setup:
///   1. Aplicar `mat_monitor_pantalla` al Renderer del monitor.
///   2. Asignar el Renderer al campo `_renderer` (o dejar vacío para autodetectar).
///   3. Ajustar baseEmission al color HDR deseado para la pantalla.
///
/// Usa MaterialPropertyBlock → no instancia materials, no rompe batching.
/// Time.time (escalado por timeScale) → el flicker se congela cuando el juego está en pausa.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class MonitorFlicker : MonoBehaviour
{
    [Header("Emission")]
    [ColorUsage(showAlpha: true, hdr: true)]
    [SerializeField] private Color baseEmission = Color.white;

    [Header("Pulso")]
    [Tooltip("Intensidad mínima del pulso (multiplica el baseEmission).")]
    [SerializeField] private float minIntensity = 0.9f;

    [Tooltip("Intensidad máxima del pulso (multiplica el baseEmission).")]
    [SerializeField] private float maxIntensity = 1.0f;

    [Tooltip("Frecuencia del flicker en Hz. 0.2 = un ciclo completo cada 5 segundos.")]
    [SerializeField] private float flickerSpeed = 0.2f;

    [Tooltip("Desfase determinista por instancia (en segundos). Permite que varios monitores estén desincronizados.")]
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
        // sin01 ∈ [0,1], suave, frecuencia flickerSpeed Hz.
        float phase = (Time.time + flickerOffset) * flickerSpeed * 2f * Mathf.PI;
        float sin01 = (Mathf.Sin(phase) + 1f) * 0.5f;

        float intensity = Mathf.Lerp(minIntensity, maxIntensity, sin01);

        _renderer.GetPropertyBlock(_propBlock);
        _propBlock.SetColor(_emissionId, baseEmission * intensity);
        _renderer.SetPropertyBlock(_propBlock);
    }
}
