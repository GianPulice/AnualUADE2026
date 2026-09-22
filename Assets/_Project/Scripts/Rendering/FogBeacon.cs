using UnityEngine;

/// <summary>
/// A point in the world that the fog cannot put out. One job, and only one: it stays readable from
/// any distance, through any preset.
///
/// ── WHY THIS EXISTS AND A FogLightBypass DOES NOT DO IT ─────────────────────
///
/// A <see cref="FogLightBypass"/> injects its light into <c>sceneColor</c>, and the shader then
/// multiplies <c>sceneColor</c> by the transmittance — so a bypass glow is attenuated by exactly
/// the fog it is meant to punch through. Worse, past <c>visionEnd</c> the optical depth saturates,
/// which makes that attenuation a CONSTANT: with the project's Dark preset it is e^-5.43 ≈ 0.0044.
/// A glow of intensity 1 lands on screen at 0.004, below the fog's own red in-scattering (0.007).
/// No intensity fixes that without blowing out at two metres, and the only other knob —
/// clearAmount — buys visibility by dissolving the fog in a sphere, which hands the player a clean
/// view of the whole room around whatever is glowing.
///
/// A beacon is composited AFTER the extinction instead (see <c>vfBeacons</c> in
/// VisionFog_HLSL.shader). That is the one place in the pipeline where the brightness asked for is
/// the brightness that arrives, identically in every preset.
///
/// ── WHAT IT IS NOT ──────────────────────────────────────────────────────────
///
/// It is not a light. It does not illuminate anything, it casts nothing, it does not clear fog and
/// it does not tell you where the thing is looking. It is a dot on the screen at a world position.
/// If you need the surroundings lit, that is a Light; if you need fog dissolved, that is a
/// <see cref="FogLightBypass"/>. Keeping those three apart is the point.
/// </summary>
[DisallowMultipleComponent]
public class FogBeacon : MonoBehaviour
{
    // Tooltips in Spanish, same convention as FogLightBypass and SO_VisionFogConfig: this gets
    // placed and tuned by the designer, not by a programmer.

    [Header("Apariencia")]
    [Tooltip("Color del punto. Se convierte a lineal al mandarlo al shader, no lo pre-conviertas.")]
    [ColorUsage(showAlpha: false, hdr: false)]
    public Color color = new Color(1f, 0.12f, 0.08f);

    [Tooltip("Brillo final en pantalla, en lineal. Es un valor ABSOLUTO: no lo toca la niebla, " +
             "así que el mismo número se ve igual con cualquier preset y a cualquier distancia.\n\n" +
             "Arriba de 1 el punto entra en el rango del Bloom y empieza a florecer, que es lo " +
             "que lo hace leer como una luz y no como un píxel prendido.")]
    [Min(0f)] public float intensity = 3.5f;

    [Tooltip("Tamaño REAL del punto, en metros. Sólo manda de cerca: de lejos el radio en " +
             "pantalla cae por debajo del mínimo de abajo y gana ese.")]
    // Min above zero, not zero: the shader uses this as the empty-slot sentinel, so a beacon
    // with radius 0 would be skipped silently rather than falling back to minPixelRadius.
    [Min(0.001f)] public float worldRadius = 0.045f;

    [Tooltip("Radio MÍNIMO en píxeles. Es lo que hace que esto funcione de lejos: un ojo de 4 cm " +
             "a 30 m mide una fracción de píxel, y sin un piso titila y desaparece según cómo " +
             "caiga el muestreo.\n\n" +
             "Por debajo de ~2 px se ve como ruido. 3-4 px es un punto nítido y estable.")]
    [Min(0f)] public float minPixelRadius = 3.5f;

    [Tooltip("Corre el centro del punto respecto del origen del objeto, en espacio local. Igual " +
             "que el Center de un SphereCollider: rota y escala con el transform.")]
    public Vector3 centreOffset = Vector3.zero;

    [Header("Cuándo se ve")]
    [Tooltip("Prendido, el punto se apaga cuando el objeto te da la espalda — un ojo no se ve " +
             "desde atrás de la cabeza. Usa el +Z (forward) de ESTE objeto como dirección.")]
    public bool limitByFacing = true;

    [Tooltip("Apertura TOTAL en grados dentro de la cual se ve. 180 = todo el hemisferio de " +
             "adelante.")]
    [Range(1f, 360f)] public float facingAngle = 150f;

    [Tooltip("Qué tan blando es el borde del cono. 0 = corte duro (se prende y se apaga de " +
             "golpe al girar la cabeza, se nota). 0.3-0.5 es un fundido natural.")]
    [Range(0f, 1f)] public float facingSoftness = 0.35f;

    [Tooltip("Distancia en metros por debajo de la cual el punto está apagado del todo. Con 0 " +
             "está siempre prendido.\n\n" +
             "Esto es un faro de lejos: de cerca ya ves la criatura entera y el punto sólo tapa " +
             "la cara. Subilo si te molesta a corta distancia.")]
    [Min(0f)] public float nearFadeStart = 0f;

    [Tooltip("Distancia a partir de la cual el punto está a brillo completo. Entre esta y la de " +
             "arriba hace un fundido. Ignorado si no es mayor que nearFadeStart.")]
    [Min(0f)] public float nearFadeEnd = 0f;

    [Tooltip("Distancia máxima a la que se ve, en metros. Con 0 no tiene límite — que es lo " +
             "normal: el punto ya se achica solo hasta el mínimo en píxeles.")]
    [Min(0f)] public float maxDistance = 0f;

    /// <summary>
    /// Runtime multiplier on <see cref="intensity"/>, so a driver can ease the beacon up and down
    /// without touching the authored value — the same role <c>LightIntensityScale</c> plays on
    /// <see cref="FogLightBypass"/>. Clamped to >= 0.
    /// </summary>
    public float IntensityScale
    {
        get => _intensityScale;
        set => _intensityScale = Mathf.Max(0f, value);
    }

    private float _intensityScale = 1f;

    /// <summary>Where the point actually sits: the object's position with the offset applied.</summary>
    public Vector3 WorldCentre => transform.TransformPoint(centreOffset);

    /// <summary>Physical radius in metres, read by the controller on its way to the shader.</summary>
    public float WorldRadius => worldRadius;

    private void OnEnable()  => VisionRangeController.RegisterBeacon(this);
    private void OnDisable() => VisionRangeController.UnregisterBeacon(this);

    /// <summary>
    /// Resolves what this beacon contributes this frame. Called by
    /// <see cref="VisionRangeController"/> once per beacon per frame.
    ///
    /// Returns <c>false</c> when it contributes nothing (out of range, facing away, faded out), so
    /// the controller can leave the shader slot empty instead of uploading a black point — the
    /// shader skips empty slots before it does any matrix work, so this is what keeps the cost at
    /// the beacons actually lit rather than the whole array.
    /// </summary>
    /// <param name="viewerPosition">Where the player is.</param>
    /// <param name="resolvedColor">Colour, still in sRGB — the controller converts.</param>
    /// <param name="resolvedIntensity">Brightness after every fade.</param>
    /// <param name="resolvedMinPixels">Floor of the on-screen radius, in pixels.</param>
    public bool Resolve(Vector3 viewerPosition, out Color resolvedColor,
                        out float resolvedIntensity, out float resolvedMinPixels)
    {
        resolvedColor     = color;
        resolvedIntensity = 0f;
        resolvedMinPixels = minPixelRadius;

        if (worldRadius <= 0.0001f) return false;   // see the note on the field

        float scaled = intensity * _intensityScale;
        if (scaled <= 0.0001f) return false;

        Vector3 toViewer = viewerPosition - WorldCentre;
        float distance = toViewer.magnitude;

        if (maxDistance > 0f && distance > maxDistance) return false;

        float weight = 1f;

        // Near fade. A beacon is a long-range tell; up close the creature is plainly visible and
        // the dot only sits on top of its face.
        if (nearFadeEnd > nearFadeStart)
        {
            weight *= Smooth(Mathf.InverseLerp(nearFadeStart, nearFadeEnd, distance));
            if (weight <= 0.0001f) return false;
        }

        // Facing. Standing right on top of it there is no meaningful direction to test, so the
        // cone is skipped rather than flickering on a near-zero vector.
        if (limitByFacing && distance > 0.001f)
        {
            float cosHalf = Mathf.Cos(facingAngle * 0.5f * Mathf.Deg2Rad);
            float d = Vector3.Dot(transform.forward, toViewer / distance);

            float upper = Mathf.Lerp(cosHalf, 1f, facingSoftness);
            weight *= upper > cosHalf + 1e-5f
                ? Smooth(Mathf.InverseLerp(cosHalf, upper, d))
                : (d >= cosHalf ? 1f : 0f);

            if (weight <= 0.0001f) return false;
        }

        resolvedIntensity = scaled * weight;
        return resolvedIntensity > 0.0001f;
    }

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 centre = WorldCentre;

        // The sphere is the physical size, which is tiny on purpose — the thing that makes it
        // visible is minPixelRadius, and that has no world-space size to draw. So the gizmo shows
        // the direction instead, which is the part that can actually be got wrong.
        Gizmos.color = new Color(color.r, color.g, color.b, 0.9f);
        Gizmos.DrawWireSphere(centre, Mathf.Max(worldRadius, 0.01f));

        if (!limitByFacing) return;

        float half = facingAngle * 0.5f * Mathf.Deg2Rad;
        Vector3 axis = transform.forward;
        Vector3 up = Vector3.Cross(axis, Vector3.up).sqrMagnitude < 1e-4f
            ? Vector3.Cross(axis, Vector3.right) : Vector3.Cross(axis, Vector3.up);
        up.Normalize();
        Vector3 right = Vector3.Cross(axis, up);

        const float len = 1.2f;
        Gizmos.color = new Color(color.r, color.g, color.b, 0.5f);
        for (int k = 0; k < 4; k++)
        {
            Vector3 side = k == 0 ? up : k == 1 ? -up : k == 2 ? right : -right;
            Gizmos.DrawLine(centre, centre + (axis * Mathf.Cos(half) + side * Mathf.Sin(half)) * len);
        }
    }
#endif
}
