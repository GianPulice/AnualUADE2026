using UnityEngine;

/// <summary>
/// Marks a spherical world area where light "cuts through" the fog of the
/// <see cref="VisionRangeController"/> — meant for street lamps, bonfires,
/// flares, narrative "there is something important here" signals, etc.
///
/// Unlike <see cref="FogLightSource"/> (which follows the player), these zones
/// are anchored to the world. The controller reads every active bypass zone
/// each frame and pushes them to the shader as an array (max.
/// <see cref="VisionRangeController.MaxBypassZones"/>).
///
/// ── CLEARING AND GLOWING ARE SEPARATE ───────────────────────────────────────
/// A zone does two things and they are deliberately not the same slider:
///   <see cref="clearAmount"/> — how much fog it dissolves. Combined across overlapping zones
///   with MAX, so two lamps side by side do not clear twice as hard, they are treated as one.
///   <see cref="intensity"/> + <see cref="color"/> — how much coloured light it INJECTS into the
///   fog. Those DO add up, because two lamps really are brighter than one, and this is what makes
///   a lamp read as a glow hanging in the dark rather than as a hole cut out of it.
///
/// A lamp seen through heavy fog is the case that needs both: high intensity so it glows, low
/// clearAmount so the geometry around it stays soft.
/// </summary>
[DisallowMultipleComponent]
public class FogLightBypass : MonoBehaviour
{
    // Tooltips in Spanish for the same reason as SO_VisionFogConfig's: this component is placed
    // and tuned by the game designer, not by a programmer.
    [Tooltip("Radio en metros donde la niebla reacciona. Se ve como una esfera amarilla en la " +
             "escena al seleccionar el objeto.\n\n" +
             "Con 0 el componente no hace nada. Suele convenir que sea MÁS grande que el range " +
             "de la Light real: el halo en la niebla se lee desde más lejos que lo que la " +
             "lámpara ilumina de verdad.")]
    [Min(0f)] public float radius = 3f;

    [Header("Apariencia")]
    [Tooltip("Apagado = usa los valores 'bypassDefault...' del SO_VisionFogConfig activo, así " +
             "todas las lámparas del área se ven iguales y se retocan de una sola vez.\n\n" +
             "Prendido = esta lámpara se sale de la norma del área. Usalo sólo cuando haga falta.")]
    public bool overrideAppearance = false;

    [Tooltip("Color de la luz que esta zona inyecta en la niebla.")]
    [ColorUsage(showAlpha: false, hdr: false)]
    public Color color = new Color(1f, 0.85f, 0.6f);

    [Tooltip("Cuánto BRILLA: la luz que la zona mete dentro de la niebla.\n\n" +
             "0 = sólo limpia niebla, sin halo. Es lo que hace que se lea como una lámpara y no " +
             "como un recorte.")]
    [Range(0f, 8f)] public float intensity = 1f;

    [Tooltip("Cuánto ACLARA: la niebla que la zona disuelve en su centro.\n\n" +
             "0 = sólo brilla, sin volver nítido lo que hay detrás. Una lámpara vista a través " +
             "de niebla espesa quiere intensity alto y clear bajo.")]
    [Range(0f, 1f)] public float clearAmount = 1f;

    [Header("Manejar desde una Light real")]
    [Tooltip("Si lo asignás, el color y la intensidad salen de esta Light en vez de los campos " +
             "de arriba — bajarle la intensidad a la lámpara real le baja el halo en la niebla " +
             "sola. Si la Light está apagada, la zona deja de aportar.\n\n" +
             "El radio y el clearAmount se siguen tomando de acá, no de la Light.")]
    [SerializeField] private Light lightComponent;

    [Tooltip("Multiplica a Light.intensity al leerla de la Light. Las intensidades reales casi " +
             "nunca están en la escala que la niebla quiere.\n\n" +
             "Si el halo te sale reventado, bajá esto antes de tocar la Light.")]
    [Range(0f, 4f)]
    [SerializeField] private float lightIntensityScale = 1f;

    private void OnEnable()  => VisionRangeController.RegisterBypass(this);
    private void OnDisable() => VisionRangeController.UnregisterBypass(this);

    /// <summary>
    /// Resolves what this zone contributes, falling back to the active preset's defaults.
    /// Called by the controller once per zone per frame.
    /// </summary>
    /// <param name="state">The fog state currently in effect, for its bypass defaults.</param>
    /// <param name="resolvedColor">Colour, still in sRGB — the controller converts.</param>
    /// <param name="resolvedIntensity">Light injected.</param>
    /// <param name="resolvedClear">Fog dissolved, 0..1.</param>
    public void Resolve(in VisionFogState state, out Color resolvedColor,
                        out float resolvedIntensity, out float resolvedClear)
    {
        // A Light that exists but is switched off should stop feeding the fog, otherwise turning
        // a lamp off leaves its glow hanging in the dark.
        if (lightComponent != null && lightComponent.isActiveAndEnabled)
        {
            resolvedColor     = lightComponent.color;
            resolvedIntensity = lightComponent.intensity * lightIntensityScale;
            resolvedClear     = clearAmount;
            return;
        }

        if (overrideAppearance)
        {
            resolvedColor     = color;
            resolvedIntensity = intensity;
            resolvedClear     = clearAmount;
            return;
        }

        resolvedColor     = state.bypassDefaultColor;
        resolvedIntensity = state.bypassDefaultIntensity;
        resolvedClear     = state.bypassDefaultClear;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Drawn in the zone's own colour rather than a fixed amber: with several lamps in one
        // room, the gizmo colour is the fastest way to see which one is which.
        Color c = lightComponent != null ? lightComponent.color
                : overrideAppearance     ? color
                : new Color(1f, 0.85f, 0.3f);

        Gizmos.color = new Color(c.r, c.g, c.b, 0.35f);
        Gizmos.DrawSphere(transform.position, radius);
        Gizmos.color = new Color(c.r, c.g, c.b, 0.9f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}
