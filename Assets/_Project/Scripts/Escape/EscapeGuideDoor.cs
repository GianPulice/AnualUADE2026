using UnityEngine;

/// <summary>
/// One amber light of the path to the gate (Paso 5). Fixed at its doorway: it is a lamp you run
/// towards, not a light that follows you. It only knows how lit it is right now — WHEN it is lit is
/// <see cref="EscapeFogCycle"/>'s job, and how it looks is <see cref="SO_EscapeSequenceConfig"/>.
///
/// The lamp hangs from whatever is right above the doorway point — the ceiling, or a door's lintel —
/// and shines down on the floor, a spotlight from above. The point itself stays where it is placed,
/// at the height of a person: the route measures arrival there. It used to be the light's own
/// position too, and a lamp floating at mid-height in the middle of the corridor read as a firefly.
///
/// Three pieces, driven by one 0..1 value, each doing the one thing the other two cannot:
///   - a <see cref="FogBeacon"/>: the lamp's bulb, the point that stays readable through ANY fog,
///     at any distance. With the fog closed this is what shows the way.
///   - a small <see cref="FogLightBypass"/> sphere: the halo around the lamp. Kept small with a low
///     clear on purpose — a big clear sphere would hand the player a clean view of the room.
///   - an optional real <see cref="Light"/>: a spot pointing at the floor, the pool of light under
///     the lamp.
///
/// It used to be a cone aimed at the player every frame, reaching a few metres past them: the light
/// travelled with the player instead of marking the way (WIR-039).
///
/// Place it on an empty at the doorway. The bypass is required and the beacon is added on Awake
/// when missing; leave their fields alone, this component drives them. The lamp is moved and turned
/// into a spot on Awake: its placement in the scene does not matter.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(FogLightBypass))]
public class EscapeGuideDoor : MonoBehaviour
{
    [Tooltip("Real light of the door (optional). Hung from the ceiling above the door as a spot " +
             "pointing at the floor; its colour, intensity and aperture come from " +
             "SO_EscapeSequenceConfig.")]
    [SerializeField] private Light lamp;

    private const float BeaconWorldRadius = 0.15f;
    private const float BeaconMinPixels = 4f;

    // Where the lamp hangs: the first thing above the doorway point, a hand below it. Looked for this
    // far up; with nothing there, it hangs this far above the point.
    private const float MountSearch = 6f;
    private const float MountBelowCeiling = 0.1f;
    private const float FallbackRise = 2.2f;

    // The floor is looked for this far below the lamp, and the spot reaches this much past it (its
    // light fades to nothing at its range: reaching only to the floor, the floor stays dark).
    private const float FloorSearch = 12f;
    private const float RangeOverHeight = 1.6f;

    private FogLightBypass bypass;
    private FogBeacon beacon;
    private float lit;

    private Vector3 mount;
    private float mountHeight;

    /// <summary>How lit the door is, 0..1.</summary>
    public float Lit => lit;

    public Vector3 Position => transform.position;

    private void Awake()
    {
        bypass = GetComponent<FogLightBypass>();
        if (!TryGetComponent(out beacon)) beacon = gameObject.AddComponent<FogBeacon>();

        mount = MountPoint(transform.position);
        mountHeight = HeightAboveFloor(mount, transform.position);

        // A lamp is visible from every side, unlike an eye, and bigger than one: the beacon's
        // defaults are sized for the Nemesis's eyes.
        beacon.limitByFacing = false;
        beacon.worldRadius = BeaconWorldRadius;
        beacon.minPixelRadius = BeaconMinPixels;

        // The bulb and its halo are up at the lamp, not at the doorway point.
        Vector3 local = transform.InverseTransformPoint(mount);
        beacon.centreOffset = local;
        bypass.centerOffset = local;

        if (lamp != null)
        {
            lamp.type = LightType.Spot;
            lamp.transform.SetPositionAndRotation(mount, Quaternion.LookRotation(Vector3.down, Vector3.forward));
        }

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
            lamp.intensity = config.GuideLampIntensity * lit;
            lamp.spotAngle = config.GuideSpotAngle;
            lamp.innerSpotAngle = config.GuideSpotAngle * 0.5f;
            lamp.range = mountHeight * RangeOverHeight;
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

    /// <summary>Right under whatever is above the point (triggers ignored), or a fixed rise over it
    /// when there is nothing.</summary>
    private static Vector3 MountPoint(Vector3 point)
    {
        if (Physics.Raycast(point, Vector3.up, out RaycastHit hit, MountSearch,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.point + Vector3.down * MountBelowCeiling;

        return point + Vector3.up * FallbackRise;
    }

    /// <summary>From the lamp down to the floor. With no floor found, down to a person's feet under
    /// the doorway point, which sits at head height.</summary>
    private static float HeightAboveFloor(Vector3 lampPoint, Vector3 doorwayPoint)
    {
        if (Physics.Raycast(lampPoint, Vector3.down, out RaycastHit hit, FloorSearch,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return Mathf.Max(0.5f, lampPoint.y - hit.point.y);

        return Mathf.Max(0.5f, lampPoint.y - doorwayPoint.y + 1.6f);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.72f, 0.38f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, 0.25f);

        // Where the lamp will hang.
        Vector3 hang = Application.isPlaying ? mount : MountPoint(transform.position);
        Gizmos.DrawLine(transform.position, hang);
        Gizmos.DrawWireSphere(hang, 0.12f);
    }
#endif
}
