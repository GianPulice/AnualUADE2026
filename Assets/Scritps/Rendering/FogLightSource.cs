using UnityEngine;

/// <summary>
/// Marca un GameObject como la fuente de luz del player que "perfora" la niebla
/// del <see cref="VisionRangeController"/>. Se coloca típicamente en el hijo
/// del player que tiene la Point Light ámbar del dispositivo (spec §2.5).
///
/// Dos modos:
/// <list type="bullet">
/// <item><b>useLightComponent = true</b> (default) — lee <c>light.range</c>,
/// <c>light.color</c>, <c>light.intensity</c> del <see cref="Light"/> hermano
/// y los pushea como override al fog shader. Ideal para acoplarse a la
/// degradación futura de módulos (§2.5.1): al bajar la intensity de la Light,
/// el fogClearRadius se reduce solo.</item>
/// <item><b>useLightComponent = false</b> — el controller usa los valores del
/// <see cref="SO_VisionFogConfig"/> activo. Útil si querés que la niebla se
/// abra con un radio distinto al de la luz física real.</item>
/// </list>
///
/// El <see cref="rangeMultiplier"/> permite que el radio del fog sea mayor
/// (o menor) que el <c>light.range</c> visible — típicamente 2× para que el
/// player vea "más lejos" a través de la niebla que lo que la luz ilumina.
/// </summary>
[DisallowMultipleComponent]
public class FogLightSource : MonoBehaviour
{
    [Tooltip("Light referenciada. Si está vacía, se busca GetComponent<Light>() en Awake.")]
    [SerializeField] private Light lightComponent;

    [Tooltip("Si true, el fog shader lee del componente Light (range, color, intensity). " +
             "Si false, usa los valores del SO_VisionFogConfig activo.")]
    [SerializeField] private bool useLightComponent = true;

    [Tooltip("Multiplicador aplicado al Light.range para el fog. 2 = la niebla se abre al doble " +
             "del radio de iluminación real. Deja al player ver más lejos que lo que la luz ilumina.")]
    [Range(0.25f, 5f)]
    [SerializeField] private float rangeMultiplier = 2f;

    [Tooltip("Multiplicador aplicado a la intensity para calcular el 'poder' de perforación de niebla. " +
             "1 = igual que la luz física. >1 = la niebla se disuelve más agresivamente.")]
    [Range(0f, 5f)]
    [SerializeField] private float intensityMultiplier = 1f;

    /// <summary>True si esta fuente aporta valores propios que deben sobrescribir al SO.</summary>
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
    }

    private void OnDisable()
    {
        if (_controller != null) _controller.SetPlayerLightSource(null);
    }

    private void AcquireController()
    {
        if (_controller != null) return;
        _controller = FindFirstObjectByType<VisionRangeController>();
    }
}
