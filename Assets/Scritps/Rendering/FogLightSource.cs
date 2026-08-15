using UnityEngine;

/// <summary>
/// Marks a GameObject as the player's light source that "punches through" the fog of the
/// <see cref="VisionRangeController"/>. It typically goes on the player's child that has
/// the device's amber Point Light (spec §2.5).
///
/// Two modes:
/// <list type="bullet">
/// <item><b>useLightComponent = true</b> (default) — reads <c>light.range</c>,
/// <c>light.color</c>, <c>light.intensity</c> from the sibling <see cref="Light"/>
/// and pushes them as an override to the fog shader. Ideal for hooking into the
/// future module degradation (§2.5.1): lowering the Light's intensity shrinks
/// the fogClearRadius on its own.</item>
/// <item><b>useLightComponent = false</b> — the controller uses the values of the active
/// <see cref="SO_VisionFogConfig"/>. Useful if you want the fog to open with a radius
/// different from the real physical light.</item>
/// </list>
///
/// <see cref="rangeMultiplier"/> lets the fog radius be larger (or smaller) than the
/// visible <c>light.range</c> — typically 2× so the player sees "further" through the
/// fog than the light actually illuminates.
/// </summary>
[DisallowMultipleComponent]
public class FogLightSource : MonoBehaviour
{
    [Tooltip("Referenced Light. If empty, GetComponent<Light>() is used in Awake.")]
    [SerializeField] private Light lightComponent;

    [Tooltip("If true, the fog shader reads from the Light component (range, color, intensity). " +
             "If false, it uses the values of the active SO_VisionFogConfig.")]
    [SerializeField] private bool useLightComponent = true;

    [Tooltip("Multiplier applied to Light.range for the fog. 2 = the fog opens at twice " +
             "the real illumination radius. Lets the player see further than the light reaches.")]
    [Range(0.25f, 5f)]
    [SerializeField] private float rangeMultiplier = 2f;

    [Tooltip("Multiplier applied to intensity to compute the fog-piercing 'power'. " +
             "1 = same as the physical light. >1 = the fog dissolves more aggressively.")]
    [Range(0f, 5f)]
    [SerializeField] private float intensityMultiplier = 1f;

    /// <summary>True if this source provides its own values that should override the SO.</summary>
    public bool HasLightOverride => useLightComponent && lightComponent != null;

    public float OverrideRange     => lightComponent != null ? lightComponent.range * rangeMultiplier : 0f;
    public Color OverrideColor     => lightComponent != null ? lightComponent.color : Color.black;
    public float OverrideIntensity => lightComponent != null ? lightComponent.intensity * intensityMultiplier : 0f;

    private VisionRangeController _controller;

    private void Awake()
    {
        if (lightComponent == null) lightComponent = GetComponent<Light>();
    }

    private void OnEnable()
    {
        AcquireController();
        if (_controller != null) _controller.SetPlayerLightSource(this);

        // Nothing found: the controller's scene has not loaded yet. Deliberately no retry loop
        // here — VisionRangeController.OnEnable picks this component up from its own side, so
        // the pairing happens either way round without either of them polling.
    }

    private void OnDisable()
    {
        // Re-resolved instead of trusting the cached reference: when the controller came up after
        // this component, OnEnable found nothing and the controller registered us from its side,
        // leaving _controller null here. Without this the controller would keep pointing at a
        // disabled light and go on opening the fog around it.
        AcquireController();
        if (_controller == null) return;

        // Only clear ourselves. Another FogLightSource may have taken over in the meantime (a
        // swapped player rig), and blanking it would put the fog back to the config values.
        if (_controller.PlayerLightSource == this) _controller.SetPlayerLightSource(null);
    }

    private void AcquireController()
    {
        if (_controller != null) return;
        // Same reasoning as LightZone: FindFirstObjectByType is deprecated for relying on
        // instance ID ordering, and there is only ever one controller per scene anyway.
        _controller = FindAnyObjectByType<VisionRangeController>();
    }
}
