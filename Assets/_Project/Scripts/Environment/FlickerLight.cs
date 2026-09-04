using UnityEngine;

/// <summary>
/// Controls the flicker of a fluorescent tube: animates the intensity of a Light
/// and the <c>_Intensity</c> property of the material (Custom/FlickerLight shader).
///
/// Deterministic per instance: given <c>Time.time + flickerOffset</c>, evaluating the
/// curve always yields the same value → repeatable for debugging/replays.
///
/// Setup:
///   1. Tube GameObject with a child Light and a MeshRenderer using `mat_fluorescent_tube`
///      (an instance of Custom/FlickerLight).
///   2. Add this component to the GameObject that has the Light.
///   3. Assign the MeshRenderer to the `targetRenderer` field so the material also flickers.
///   4. Tune the AnimationCurve (X: 0-1 within the cycle, Y: 0-1 factor over maxIntensity).
///   5. On duplicated instances, set a different `flickerOffset` on each to avoid syncing.
/// </summary>
[RequireComponent(typeof(Light))]
public class FlickerLight : MonoBehaviour
{
    [Header("Curve (X: 0..1 within the cycle, Y: 0..1 factor)")]
    [SerializeField] private AnimationCurve flickerCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    [Header("Parameters")]
    [Tooltip("Final multiplier applied to the curve value.")]
    [SerializeField] private float maxIntensity = 2f;

    [Tooltip("Duration of a full cycle in seconds.")]
    [SerializeField] private float cycleDuration = 1f;

    [Tooltip("Time offset in seconds. Lets identical instances be desynchronized. Deterministic.")]
    [SerializeField] private float flickerOffset = 0f;

    [Header("Material (optional — to also flicker the tube's MeshRenderer)")]
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
        // Phase ∈ [0, 1) within the cycle. Deterministic for the same Time.time + offset.
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
        // Default curve: fast flicker + steady. Editable in the Inspector.
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
