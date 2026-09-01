using UnityEngine;

/// <summary>
/// Eases a <see cref="FogLightBypass"/>'s <see cref="FogLightBypass.LightIntensityScale"/> down
/// as the player walks into its <see cref="SphereCollider"/> and back up as they leave.
///
/// WHY
///
/// A lamp tuned to read as a warm glow hanging in the fog from across the room is, by the time
/// you are standing under it, a wall of light in your face — the injected intensity that made it
/// legible at distance now blows out everything nearby. This trades one for the other on a
/// curve: full strength while the player is outside the sphere (where the reading matters), a
/// lower multiplier once they are inside it (where it would only glare).
///
/// It drives ONLY the multiplier. Colour, radius, clearAmount and which Light the zone reads
/// from all stay wherever the <see cref="FogLightBypass"/> has them — this cannot make the lamp
/// look like a different lamp, only quieter up close.
///
/// SETUP
///
///   1. On the same GameObject as the <see cref="FogLightBypass"/> and its trigger
///      <see cref="SphereCollider"/> (the "Light Base" prefab already has both).
///   2. Assign the bypass's own <c>Light Component</c> field to the real Light — the multiplier
///      is dead weight until it has a Light to scale. If it is empty this component still runs
///      but changes nothing visible.
///   3. Tune <see cref="insideScale"/> / <see cref="outsideScale"/> to taste.
///
/// The sphere used is the <see cref="SphereCollider"/>, NOT <see cref="FogLightBypass.radius"/>:
/// the collider is the volume the level designer already shaped for "the player is under this
/// lamp", and the bypass radius is usually larger on purpose (the glow reads past the light).
/// </summary>
[RequireComponent(typeof(FogLightBypass))]
[RequireComponent(typeof(SphereCollider))]
[DefaultExecutionOrder(50)] // before VisionRangeController (100) reads the zone in its LateUpdate
public class FogLightBypassPlayerFade : MonoBehaviour
{
    // Spanish tooltips, same reasoning as FogLightBypass / SO_VisionFogConfig: tuned by the
    // game designer in the inspector, not read from code.
    [Header("Referencias (se toman solas del mismo objeto si las dejás vacías)")]
    [SerializeField] private FogLightBypass bypass;

    [Tooltip("El volumen que cuenta como 'el player está debajo de la lámpara'. Se usa su radio " +
             "y su centro en mundo, no el radius del FogLightBypass.")]
    [SerializeField] private SphereCollider zone;

    [Header("Detección del player")]
    [Tooltip("Tag del objeto que se sigue. Igual que en LightZone.")]
    [SerializeField] private string playerTag = "Player";

    [Header("Multiplicador según la distancia")]
    [Tooltip("Valor en el CENTRO de la esfera — lo más metido que puede estar el player. Bajo a " +
             "propósito: es acá donde el halo, si va a full, le tapa la vista.")]
    [Range(0f, 4f)] [SerializeField] private float insideScale = 0.3f;

    [Tooltip("Valor en la SUPERFICIE de la esfera y para afuera. Este es el que se ve 'bien' de " +
             "lejos — normalmente 1.")]
    [Range(0f, 4f)] [SerializeField] private float outsideScale = 1f;

    [Tooltip("Cómo se reparte el cambio entre el centro (0) y el borde (1) de la esfera.\n\n" +
             "Recta = lineal. Con la ease de fábrica el player camina un rato adentro antes de " +
             "que el halo empiece a bajar en serio, y baja de golpe cerca del centro.")]
    [SerializeField] private AnimationCurve falloff = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Qué tan rápido persigue el valor al objetivo, por segundo. 0 = instantáneo (salta " +
             "en el borde). Un valor medio suaviza el cruce sin que se note lag.")]
    [Min(0f)] [SerializeField] private float responseSpeed = 6f;

    private Transform _player;
    private float _current;

    private void Reset()
    {
        bypass = GetComponent<FogLightBypass>();
        zone   = GetComponent<SphereCollider>();
    }

    private void OnEnable()
    {
        if (bypass == null) bypass = GetComponent<FogLightBypass>();
        if (zone == null)   zone   = GetComponent<SphereCollider>();

        // Start already settled on whatever the player's current position asks for, so the lamp
        // does not visibly ramp on the first frames if the player spawned inside the sphere.
        AcquirePlayer();
        _current = ComputeTarget();
        Apply();
    }

    private void LateUpdate()
    {
        if (bypass == null || zone == null) return;
        if (_player == null) AcquirePlayer();

        float target = ComputeTarget();

        _current = responseSpeed > 0f
            // Framerate-independent ease: same shape whether the game runs at 60 or 144.
            ? Mathf.Lerp(_current, target, 1f - Mathf.Exp(-responseSpeed * Time.deltaTime))
            : target;

        Apply();
    }

    /// <summary>Multiplier the player's current position asks for, before time smoothing.</summary>
    private float ComputeTarget()
    {
        if (_player == null) return outsideScale;

        Vector3 center = zone.transform.TransformPoint(zone.center);
        Vector3 s = zone.transform.lossyScale;
        float worldRadius = zone.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        if (worldRadius <= Mathf.Epsilon) return outsideScale;

        float t = Mathf.Clamp01(Vector3.Distance(_player.position, center) / worldRadius);
        return Mathf.LerpUnclamped(insideScale, outsideScale, falloff.Evaluate(t));
    }

    private void Apply() => bypass.LightIntensityScale = _current;

    private void AcquirePlayer()
    {
        GameObject go = GameObject.FindWithTag(playerTag);
        if (go != null) _player = go.transform;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        SphereCollider z = zone != null ? zone : GetComponent<SphereCollider>();
        if (z == null) return;

        Vector3 center = z.transform.TransformPoint(z.center);
        Vector3 s = z.transform.lossyScale;
        float worldRadius = z.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));

        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.12f);
        Gizmos.DrawSphere(center, worldRadius);
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.7f);
        Gizmos.DrawWireSphere(center, worldRadius);
    }
#endif
}
