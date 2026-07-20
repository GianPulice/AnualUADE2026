using UnityEngine;

// LED verde constante del save point. §4.7 Color Spec / A8.
// Color base: #1A5C1A. Pulso lento de ±0.05 en intensity para dar vida al LED.
// Usa MaterialPropertyBlock para no romper SRP Batcher.
// Shader: cualquier URP con _EmissionColor expuesto (ej. URP/Lit con Emission habilitado).
[RequireComponent(typeof(Renderer))]
public class SavePointMaterialApplier : MonoBehaviour
{
    [Header("Emisión")]
    [SerializeField] private Color emissionColor = new Color(0.102f, 0.361f, 0.102f); // #1A5C1A

    [Header("Pulso")]
    [Tooltip("Si true, la emisión pulsa lentamente para dar vida al LED.")]
    [SerializeField] private bool pulse = true;
    [SerializeField] private float pulseSpeed  = 1.2f;
    [SerializeField] private float pulseAmount = 0.05f;

    [Header("Renderer")]
    [SerializeField] private Renderer targetRenderer;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private MaterialPropertyBlock _propBlock;
    private float _baseIntensity;

    private void Awake()
    {
        if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        _propBlock = new MaterialPropertyBlock();
        _baseIntensity = emissionColor.maxColorComponent;
        Apply(0f);
    }

    private void Update()
    {
        float offset = pulse ? Mathf.Sin(Time.time * pulseSpeed) * pulseAmount : 0f;
        Apply(offset);
    }

    private void Apply(float intensityOffset)
    {
        if (targetRenderer == null) return;
        targetRenderer.GetPropertyBlock(_propBlock);
        Color c = emissionColor * (1f + intensityOffset);
        _propBlock.SetColor(EmissionColorId, c);
        targetRenderer.SetPropertyBlock(_propBlock);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        Apply(0f);
    }
#endif
}
