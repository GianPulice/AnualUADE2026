using System.Collections;
using UnityEngine;

/// <summary>
/// Lerpea los parámetros <c>_TintIntensity</c> y <c>_EmissionIntensity</c> del shader
/// ItemPSX cuando el player entra/sale del radio de interacción, según el spec
/// "Color &amp; Visual Language" (sección 2.1).
///
/// Estado lejano (default): tinte 0.15, emisión 0.0.
/// Estado próximo (player en radio): tinte 0.4, emisión 0.2.
/// Transición: lerp de 0.3 segundos.
///
/// Usa MaterialPropertyBlock — no instancia material, mantiene SRP Batcher.
///
/// Setup:
///   1. El Renderer del item debe usar un material con shader <c>Shader Graphs/ItemPSX</c>
///      (o cualquier shader que exponga las dos properties <c>_TintIntensity</c> y
///      <c>_EmissionIntensity</c>).
///   2. Pegar este componente al GameObject del item (Renderer en el mismo objeto, o
///      asignar a mano en <c>targetRenderer</c>).
///   3. Desde el sistema de interactuables (BaseRangeInteractable o el que sea),
///      llamar a <c>OnPlayerEnteredRange()</c> en el OnTriggerEnter del player,
///      y a <c>OnPlayerExitedRange()</c> en el OnTriggerExit.
///
/// Para puzzles e interactuables sin tinte de categoría (sec 6 del spec):
/// setear <c>farTint = 0</c> y <c>nearTint = 0</c> — solo brilla la emisión al acercarse.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class ItemProximityHighlight : MonoBehaviour
{
    [Header("Estado lejano (default)")]
    [Tooltip("Tinte de categoría apenas perceptible. Spec: 0.15.")]
    [SerializeField, Range(0f, 1f)] private float farTint      = 0.15f;

    [Tooltip("Emisión apagada en estado lejano. Spec: 0.0.")]
    [SerializeField, Range(0f, 1f)] private float farEmission  = 0.0f;

    [Header("Estado próximo (player en radio)")]
    [Tooltip("Tinte intensificado al acercarse. Spec: 0.4.")]
    [SerializeField, Range(0f, 1f)] private float nearTint     = 0.4f;

    [Tooltip("Emisión sutil al acercarse. Spec: 0.2.")]
    [SerializeField, Range(0f, 1f)] private float nearEmission = 0.2f;

    [Header("Transición")]
    [Tooltip("Duración del lerp en segundos. Spec: 0.3s.")]
    [SerializeField, Min(0.01f)] private float lerpDuration = 0.3f;

    [Header("Renderer (opcional — autodetecta el del GameObject)")]
    [SerializeField] private Renderer targetRenderer;

    private static readonly int TintId     = Shader.PropertyToID("_TintIntensity");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionIntensity");

    private MaterialPropertyBlock _propBlock;
    private Coroutine _activeLerp;
    private float _currentTint;
    private float _currentEmission;

    private void Awake()
    {
        if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        _propBlock = new MaterialPropertyBlock();
        _currentTint = farTint;
        _currentEmission = farEmission;
        ApplyProps();
    }

    /// <summary>Llamar cuando el player entra al radio de interacción del item.</summary>
    public void OnPlayerEnteredRange() => TransitionTo(nearTint, nearEmission);

    /// <summary>Llamar cuando el player sale del radio de interacción del item.</summary>
    public void OnPlayerExitedRange() => TransitionTo(farTint, farEmission);

    /// <summary>Forzar estado lejano sin animación (ej: al ocultar el item).</summary>
    public void SnapToFar()
    {
        if (_activeLerp != null) StopCoroutine(_activeLerp);
        _activeLerp = null;
        _currentTint = farTint;
        _currentEmission = farEmission;
        ApplyProps();
    }

    private void TransitionTo(float targetTint, float targetEmission)
    {
        if (!isActiveAndEnabled) return;
        if (_activeLerp != null) StopCoroutine(_activeLerp);
        _activeLerp = StartCoroutine(LerpRoutine(targetTint, targetEmission));
    }

    private IEnumerator LerpRoutine(float targetTint, float targetEmission)
    {
        float startTint     = _currentTint;
        float startEmission = _currentEmission;
        float elapsed = 0f;

        while (elapsed < lerpDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lerpDuration);
            // SmoothStep para que el "respirar" no se sienta lineal/mecánico.
            float eased = t * t * (3f - 2f * t);
            _currentTint     = Mathf.Lerp(startTint,     targetTint,     eased);
            _currentEmission = Mathf.Lerp(startEmission, targetEmission, eased);
            ApplyProps();
            yield return null;
        }

        _currentTint = targetTint;
        _currentEmission = targetEmission;
        ApplyProps();
        _activeLerp = null;
    }

    private void ApplyProps()
    {
        if (targetRenderer == null) return;
        targetRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetFloat(TintId,     _currentTint);
        _propBlock.SetFloat(EmissionId, _currentEmission);
        targetRenderer.SetPropertyBlock(_propBlock);
    }
}
