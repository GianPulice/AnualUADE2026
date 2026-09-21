using UnityEngine;

/// <summary>
/// One amber light of the path to the gate (Paso 5). Fixed at its doorway: it is a lamp you run
/// towards, not a light that follows you. It only knows how lit it is right now — WHEN it is lit is
/// <see cref="EscapeFogCycle"/>'s job, and how it looks is <see cref="SO_EscapeSequenceConfig"/>.
///
/// Three pieces, driven by one 0..1 value, each doing the one thing the other two cannot:
///   - a <see cref="FogBeacon"/>: the point that stays readable through ANY fog, at any distance.
///     With the fog closed this is what shows the way.
///   - a small <see cref="FogLightBypass"/> sphere: the halo around the lamp. Kept small with a low
///     clear on purpose — a big clear sphere would hand the player a clean view of the room.
///   - an optional real <see cref="Light"/>: lights the geometry near the door.
///
/// It used to be a cone aimed at the player every frame, reaching a few metres past them: the light
/// travelled with the player instead of marking the way (WIR-039).
///
/// Place it on an empty at the doorway. The bypass is required and the beacon is added on Awake
/// when missing; leave their fields alone, this component drives them.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(FogLightBypass))]
public class EscapeGuideDoor : MonoBehaviour
{
    [Tooltip("Real light of the door frame (optional). Turns on with the path; its colour and " +
             "intensity come from SO_EscapeSequenceConfig.")]
    [SerializeField] private Light lamp;

    private const float BeaconWorldRadius = 0.15f;
    private const float BeaconMinPixels = 4f;

    private FogLightBypass bypass;
    private FogBeacon beacon;
    private float lit;

    /// <summary>How lit the door is, 0..1.</summary>
    public float Lit => lit;

    public Vector3 Position => transform.position;

    private void Awake()
    {
        bypass = GetComponent<FogLightBypass>();
        if (!TryGetComponent(out beacon)) beacon = gameObject.AddComponent<FogBeacon>();

        // A lamp is visible from every side, unlike an eye, and bigger than one: the beacon's
        // defaults are sized for the Nemesis's eyes.
        beacon.limitByFacing = false;
        beacon.worldRadius = BeaconWorldRadius;
        beacon.minPixelRadius = BeaconMinPixels;

        TurnOff();
    }

    /// <param name="litAmount">0 = off, 1 = fully on.</param>
    public void Apply(SO_EscapeSequenceConfig config, float litAmount)
    {
        lit = Mathf.Clamp01(litAmount);
        if (lit <= 0.0001f)
        {
            TurnOff();
            return;
        }

        bypass.overrideAppearance = true;
        bypass.shape = FogLightBypass.BypassShape.Sphere;
        bypass.color = config.LightColor;
        bypass.intensity = config.LightFogIntensity * lit;
        bypass.clearAmount = config.LightFogClear;
        bypass.radius = config.LightRadius;

        beacon.color = config.LightColor;
        beacon.intensity = config.BeaconIntensity;
        beacon.IntensityScale = lit;

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

        // Radius 0 is how FogLightBypass says "does nothing"; scale 0 is the beacon's.
        bypass.radius = 0f;
        if (beacon != null) beacon.IntensityScale = 0f;
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
