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

    [Tooltip("Corre el CENTRO de la esfera respecto del origen del objeto, en espacio local. El " +
             "eje Z sigue hacia donde apunta la lámpara, así que subir Z empuja el resplandor " +
             "haz adelante sin mover el GameObject. Funciona igual que el Center de un " +
             "SphereCollider: rota y escala con el transform.")]
    public Vector3 centerOffset = Vector3.zero;

    /// <summary>
    /// Where the sphere actually sits: the object's position with <see cref="centerOffset"/>
    /// applied in local space. The controller and the gizmo both read this rather than
    /// <c>transform.position</c>, so an offset zone clears and glows where it is drawn.
    /// </summary>
    public Vector3 WorldCenter => transform.TransformPoint(centerOffset);

    public enum BypassShape { Sphere, Cone }

    [Header("Forma")]
    [Tooltip("Sphere = esfera (lámpara de techo, fogata, señal narrativa).\n\n" +
             "Cone = cono, para una Spot: el resplandor arranca en el origen del objeto y se " +
             "abre hacia +Z local (o hacia donde mira la Spot de 'Light Component'). En Cone " +
             "conviene dejar Center Offset en 0 para que el vértice quede pegado a la lámpara.")]
    public BypassShape shape = BypassShape.Sphere;

    [Tooltip("Apertura TOTAL del cono, en grados. Se ignora si 'Light Component' es una Spot " +
             "Light: en ese caso se usa el Spot Angle de la luz, así el cono del bypass y el de " +
             "la lámpara son exactamente el mismo.")]
    [Range(1f, 179f)] public float coneAngle = 50f;

    /// <summary>
    /// Cone parameters for the shader, in world space. Returns <c>false</c> for a
    /// <see cref="BypassShape.Sphere"/> zone — the shader then skips the angular test and the zone
    /// stays a plain sphere.
    ///
    /// When <see cref="lightComponent"/> is a Spot Light, its <c>forward</c> and <c>spotAngle</c>
    /// win so the glow cone tracks the real lamp; otherwise the axis is this object's forward and
    /// the aperture is <see cref="coneAngle"/>.
    /// </summary>
    public bool TryGetCone(out Vector3 axis, out float cosHalfAngle)
    {
        axis = transform.forward;
        cosHalfAngle = 1f;
        if (shape != BypassShape.Cone) return false;

        float aperture = coneAngle;
        if (lightComponent != null && lightComponent.type == LightType.Spot)
        {
            axis = lightComponent.transform.forward;
            aperture = lightComponent.spotAngle;
        }

        cosHalfAngle = Mathf.Cos(aperture * 0.5f * Mathf.Deg2Rad);
        return true;
    }

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

    /// <summary>
    /// Runtime multiplier on <see cref="lightComponent"/>'s intensity, exposed so a helper can
    /// fade this zone's contribution while keeping everything else about it fixed — for example
    /// <see cref="FogLightBypassPlayerFade"/>, which eases it down while the player is standing in
    /// the pool so the glow does not wash out their view, and back to full seen from outside.
    ///
    /// Only bites when <see cref="lightComponent"/> is assigned; with no Light the zone's look
    /// comes from the fields above or the preset defaults and this is inert. Clamped to >= 0.
    /// </summary>
    public float LightIntensityScale
    {
        get => lightIntensityScale;
        set => lightIntensityScale = Mathf.Max(0f, value);
    }

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

        Vector3 center = WorldCenter;

        if (TryGetCone(out Vector3 axis, out float cosHalf))
        {
            // Apex + a few edge rays at the half-angle, plus the far cap — enough to read the
            // aperture and where it points without drawing a solid mesh.
            float half = Mathf.Acos(Mathf.Clamp(cosHalf, -1f, 1f));
            Vector3 up = Vector3.Cross(axis, Vector3.up).sqrMagnitude < 1e-4f
                ? Vector3.Cross(axis, Vector3.right) : Vector3.Cross(axis, Vector3.up);
            up.Normalize();
            Vector3 right = Vector3.Cross(axis, up);
            float capR = radius * Mathf.Sin(half);
            Vector3 capC = center + axis * (radius * Mathf.Cos(half));

            Gizmos.color = new Color(c.r, c.g, c.b, 0.9f);
            for (int k = 0; k < 4; k++)
            {
                Vector3 dir = (k == 0 ? up : k == 1 ? -up : k == 2 ? right : -right);
                Gizmos.DrawLine(center, capC + dir * capR);
            }
            UnityEditor.Handles.color = new Color(c.r, c.g, c.b, 0.6f);
            UnityEditor.Handles.DrawWireDisc(capC, axis, capR);
            return;
        }

        Gizmos.color = new Color(c.r, c.g, c.b, 0.35f);
        Gizmos.DrawSphere(center, radius);
        Gizmos.color = new Color(c.r, c.g, c.b, 0.9f);
        Gizmos.DrawWireSphere(center, radius);
    }
#endif
}
