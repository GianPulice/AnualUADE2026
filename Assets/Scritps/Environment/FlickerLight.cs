using UnityEngine;

/// <summary>
/// Controla el flicker de un tubo fluorescente: anima la intensidad de un Light
/// y la propiedad <c>_Intensity</c> del material (shader Custom/FlickerLight).
///
/// Determinista por instancia: dado <c>Time.time + flickerOffset</c>, la evaluación
/// de la curva siempre da el mismo valor → repetible para debug/replays.
///
/// Setup:
///   1. GameObject del tubo con Light child y MeshRenderer con `mat_fluorescent_tube`
///      (instancia de Custom/FlickerLight).
///   2. Agregar este componente al GameObject que tiene el Light.
///   3. Asignar el MeshRenderer al campo `targetRenderer` para que parpadee el material.
///   4. Ajustar la AnimationCurve (X: 0-1 dentro del ciclo, Y: factor 0-1 sobre maxIntensity).
///   5. En instancias duplicadas, setear `flickerOffset` distinto a cada una para evitar sync.
/// </summary>
[RequireComponent(typeof(Light))]
public class FlickerLight : MonoBehaviour
{
    [Header("Curva (X: 0..1 dentro del ciclo, Y: factor 0..1)")]
    [SerializeField] private AnimationCurve flickerCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    [Header("Parámetros")]
    [Tooltip("Multiplicador final aplicado al valor de la curva.")]
    [SerializeField] private float maxIntensity = 2f;

    [Tooltip("Duración de un ciclo completo en segundos.")]
    [SerializeField] private float cycleDuration = 1f;

    [Tooltip("Desfase temporal en segundos. Permite desincronizar instancias idénticas. Determinista.")]
    [SerializeField] private float flickerOffset = 0f;

    [Header("Material (opcional — para parpadear también el MeshRenderer del tubo)")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private string intensityPropertyName = "_Intensity";

    private Light _light;
    private MaterialPropertyBlock _propBlock;
    private int _intensityId;

    private void Awake()
    {
        _light = GetComponent<Light>();
        _intensityId = Shader.PropertyToID(intensityPropertyName);
        if (targetRenderer != null) _propBlock = new MaterialPropertyBlock();
    }

    private void Update()
    {
        // Phase ∈ [0, 1) dentro del ciclo. Determinista para mismo Time.time + offset.
        float t = ((Time.time + flickerOffset) % cycleDuration) / cycleDuration;
        float v = flickerCurve.Evaluate(t) * maxIntensity;

        _light.intensity = v;

        if (targetRenderer != null && _propBlock != null)
        {
            targetRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(_intensityId, v);
            targetRenderer.SetPropertyBlock(_propBlock);
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        // Curva default: parpadeo rápido + estable. Editable en Inspector.
        flickerCurve = new AnimationCurve(
            new Keyframe(0f,   1f),
            new Keyframe(0.5f, 0.85f),
            new Keyframe(0.55f, 0.2f),
            new Keyframe(0.6f, 1f),
            new Keyframe(1f,   1f)
        );
    }
#endif
}
