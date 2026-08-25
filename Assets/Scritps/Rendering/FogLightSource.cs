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
    // Tooltips in Spanish, same reasoning as SO_VisionFogConfig and FogLightBypass: whoever
    // opens this component is tuning how the fog feels, not reading the code.
    [Tooltip("La Light del módulo. Si lo dejás vacío se toma el Light de este mismo GameObject " +
             "en Awake.")]
    [SerializeField] private Light lightComponent;

    [Tooltip("Prendido = la niebla lee range, color e intensity de la Light real, así que " +
             "bajarle la intensidad al módulo achica el agujero en la niebla solo (sirve para " +
             "la degradación del módulo, §2.5.1).\n\n" +
             "Apagado = se usan los valores 'playerLight...' del SO_VisionFogConfig activo.")]
    [SerializeField] private bool useLightComponent = true;

    [Tooltip("Multiplica al range de la Light para calcular el radio de niebla.\n\n" +
             "2 = la niebla se abre al doble del radio que la luz ilumina de verdad, así el " +
             "jugador VE más lejos de lo que la lámpara alcanza a iluminar. Bajarlo a 1 los " +
             "iguala y se siente mucho más cerrado.")]
    [Range(0.25f, 5f)]
    [SerializeField] private float rangeMultiplier = 2f;

    [Tooltip("Multiplica al intensity de la Light para calcular cuánta niebla disuelve.\n\n" +
             "El resultado se recorta a 0..1, así que con una Light en intensity 1 cualquier " +
             "valor de 1 para arriba ya significa \"limpia del todo en el centro\". Bajalo si " +
             "querés que quede algo de niebla adentro del radio, que suele ser lo que un área " +
             "oscura pide.")]
    [Range(0f, 5f)]
    [SerializeField] private float intensityMultiplier = 1f;

    /// <summary>True if this source provides its own values that should override the SO.</summary>
    public bool HasLightOverride => useLightComponent && lightComponent != null;

    public float OverrideRange => lightComponent != null ? lightComponent.range * rangeMultiplier : 0f;
    public Color OverrideColor => lightComponent != null ? lightComponent.color : Color.black;

    /// <summary>
    /// How much fog the light dissolves at the centre of its radius, as a fraction.
    ///
    /// Clamped, unlike the v1 <c>OverrideIntensity</c> it replaces. The shader term used to be an
    /// unbounded multiplier fed into a <c>saturate()</c>, so a Light at intensity 3 already meant
    /// "fully clear" and every value above 1 collapsed to the same result — the slider looked like
    /// it had range it did not have. Now the clamp is explicit and visible here.
    /// </summary>
    public float OverrideClear => lightComponent != null
        ? Mathf.Clamp01(lightComponent.intensity * intensityMultiplier)
        : 0f;

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
