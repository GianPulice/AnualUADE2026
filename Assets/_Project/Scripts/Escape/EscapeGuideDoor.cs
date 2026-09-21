using UnityEngine;

/// <summary>
/// One door of the escape route, as a light: when lit it punches a corridor through the fog from
/// the door towards the player (Paso 4). It only knows how lit it is right now — WHEN to light it
/// is <see cref="EscapeFogCycle"/>'s job, and how it looks is <see cref="SO_EscapeSequenceConfig"/>.
///
/// The light does two things, both driven by one 0..1 value:
///   - a <see cref="FogLightBypass"/> that clears the fog. Its reach grows with the value, so the
///     opening starts AT the door and expands towards the player, not the other way round. With a
///     cone angle set in the config it is a narrow beam along the door-player axis, so the side
///     areas stay closed.
///   - an optional real <see cref="Light"/> at the door, so the geometry near it is lit too.
///
/// Place it on an empty at the doorway, on the side the player approaches from. Its own position is
/// the apex of the beam. Needs a <see cref="FogLightBypass"/> on the same object (added by the
/// setup), which this component drives — leave its radius and colours alone.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(FogLightBypass))]
public class EscapeGuideDoor : MonoBehaviour
{
    [Tooltip("Luz real del marco de la puerta (opcional). Se prende con la niebla; su color e " +
             "intensidad salen de SO_EscapeSequenceConfig.")]
    [SerializeField] private Light lamp;

    private const float StartReach = 0.5f;

    private FogLightBypass bypass;
    private float lit;

    /// <summary>How lit the door is, 0..1.</summary>
    public float Lit => lit;

    public Vector3 Position => transform.position;

    private void Awake()
    {
        bypass = GetComponent<FogLightBypass>();
        TurnOff();
    }

    /// <param name="litAmount">0 = off, 1 = fully open.</param>
    /// <param name="towards">Where the player is: the beam points there and reaches past it.</param>
    public void Apply(SO_EscapeSequenceConfig config, float litAmount, Vector3 towards)
    {
        lit = Mathf.Clamp01(litAmount);
        if (lit <= 0.0001f)
        {
            TurnOff();
            return;
        }

        Vector3 toPlayer = towards - transform.position;
        float reach = Mathf.Clamp(toPlayer.magnitude + config.LightReachPadding,
                                  config.LightMinReach, config.LightMaxReach);

        bypass.overrideAppearance = true;
        bypass.color = config.LightColor;
        bypass.intensity = config.LightFogIntensity * lit;
        bypass.clearAmount = config.LightFogClear;
        bypass.radius = Mathf.Lerp(StartReach, reach, lit);

        bool beam = config.LightConeAngle > 0.5f;
        bypass.shape = beam ? FogLightBypass.BypassShape.Cone : FogLightBypass.BypassShape.Sphere;
        bypass.coneAngle = Mathf.Max(1f, config.LightConeAngle);
        if (beam && toPlayer.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(toPlayer);

        if (lamp != null)
        {
            lamp.enabled = true;
            lamp.color = config.LightColor;
            lamp.intensity = config.LampIntensity * lit;
        }
    }

    public void TurnOff()
    {
        lit = 0f;
        if (bypass == null) bypass = GetComponent<FogLightBypass>();

        // Radius 0 is how FogLightBypass says "does nothing".
        bypass.radius = 0f;
        if (lamp != null) lamp.enabled = false;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.72f, 0.38f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.25f);
    }
#endif
}
