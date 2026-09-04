using UnityEngine;

// Constant green LED of the save point. §4.7 Color Spec / A8.
// Base color: #1A5C1A. Slow ±0.05 intensity pulse to give the LED some life.
// Uses MaterialPropertyBlock so the SRP Batcher is not broken.
// Shader: any URP one with _EmissionColor exposed (e.g. URP/Lit with Emission enabled).
[RequireComponent(typeof(Renderer))]
public class SavePointMaterialApplier : MonoBehaviour
{
    [Header("Emission")]
    [SerializeField] private Color emissionColor = new Color(0.102f, 0.361f, 0.102f); // #1A5C1A

    [Header("Pulse")]
    [Tooltip("If true, the emission pulses slowly to give the LED some life.")]
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
