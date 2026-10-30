using UnityEngine;

/// <summary>
/// Tuning shared by every <see cref="SecurityCamera"/> that points at this asset: how it sweeps,
/// how far and how wide it sees, and how it follows the player once it has them.
///
/// Placement values that differ camera by camera - where the sweep is centred, where in the sweep
/// it starts, how far down it looks while idle - live on the component instead, because they
/// describe one wall mount and not a kind of camera. A camera that needs to behave differently
/// (faster, longer-sighted) gets its own asset, not a local override.
///
/// Every angle here is a TOTAL width, the same convention <see cref="LineOfSight.CheckAngle"/>
/// documents: a 90 degree sweep is 45 to each side of its centre.
/// </summary>
[CreateAssetMenu(fileName = "SO_SecurityCameraData", menuName = "Scriptable Objects/SO_SecurityCameraData")]
public class SO_SecurityCameraData : ScriptableObject
{
    [Header("Sweep (idle)")]
    [Tooltip("Total width of the idle sweep, in degrees. 90 means 45 to each side of the sweep centre.")]
    [SerializeField, Range(0f, 180f)] private float sweepAngle = 90f;

    [Tooltip("How fast the camera pans while sweeping, in degrees per second. Also the speed it " +
             "uses to get back into its sweep after losing the player.")]
    [SerializeField, Min(1f)] private float sweepSpeed = 20f;

    [Tooltip("Seconds the camera holds still at each end of the sweep before turning back.")]
    [SerializeField, Min(0f)] private float sweepEndPause = 1f;

    [Header("Vision")]
    [Tooltip("How far the camera sees, in metres, measured from the lens.")]
    [SerializeField, Min(0.5f)] private float viewRange = 12f;

    [Tooltip("Total HORIZONTAL width of what the camera sees, in degrees. There is no vertical " +
             "limit: height never hides the player, only range, walls and hiding spots do.")]
    [SerializeField, Range(1f, 180f)] private float viewAngle = 30f;

    [Header("Tracking")]
    [Tooltip("How fast the camera turns to follow the player, in degrees per second. If the player " +
             "crosses its view faster than this, the camera falls behind and can lose them.")]
    [SerializeField, Min(1f)] private float trackingSpeed = 90f;

    [Tooltip("Mechanical pan limit, in degrees to EACH side of straight out of the wall. The " +
             "camera never turns past it, not even to follow the player.")]
    [SerializeField, Range(0f, 170f)] private float panLimit = 80f;

    [Tooltip("How far below the horizon the camera can tilt to follow the player, in degrees.")]
    [SerializeField, Range(0f, 89f)] private float maxTiltDown = 70f;

    [Tooltip("How far above the horizon the camera can tilt to follow the player, in degrees.")]
    [SerializeField, Range(0f, 89f)] private float maxTiltUp = 20f;

    [Tooltip("Seconds the camera keeps staring at where it last saw the player before giving up " +
             "and going back to its sweep.")]
    [SerializeField, Min(0f)] private float loseSightDelay = 1.5f;

    [Header("Scan (while it sees the player)")]
    [Tooltip("Drawn over the player's body while the camera scans them (shader WIRED/Security Scan " +
             "Overlay). Its colour, band and scanlines are tuned on the material itself.")]
    [SerializeField] private Material scanOverlayMaterial;

    [Tooltip("The single ray from the lens to the scan band, as wide as the body is at that height " +
             "(shader WIRED/Security Scan Beam). One per scanning camera. Its colour, softness and " +
             "dust are tuned on the material itself.")]
    [SerializeField] private Material scanBeamMaterial;

    [Tooltip("Seconds the ray holds on the top of the head before the scan starts down the body: the " +
             "ray lands first, then the body starts turning red from where it hits. Only on a fresh " +
             "scan - one that resumes a half-drained scan carries on at once.")]
    [SerializeField, Min(0f)] private float scanLockOnTime = 0.35f;

    [Tooltip("Seconds the scan takes to cover the whole body, filling it from the head down. The " +
             "band rides the edge of the fill while it advances.")]
    [SerializeField, Min(0.1f)] private float scanFillDuration = 2.5f;

    [Tooltip("Seconds a scan takes to drain away once no camera sees the player any more - from a " +
             "full body to nothing; a partial scan drains proportionally faster. If a camera sees " +
             "them again first, the scan carries on from whatever is left.")]
    [SerializeField, Min(0.1f)] private float scanDrainDuration = 1.5f;

    [Tooltip("Once the body is fully scanned, seconds the band takes to run once from feet to head " +
             "(and the same back) while the camera keeps watching.")]
    [SerializeField, Min(0.1f)] private float scanPassDuration = 1.2f;

    [Tooltip("Seconds the camera's beam and lens light take to fade in when it sees the player and " +
             "out when it stops; also how fast the scan on the body appears and goes once drained.")]
    [SerializeField, Min(0f)] private float scanFadeTime = 0.25f;

    public float SweepAngle => sweepAngle;
    public float SweepSpeed => sweepSpeed;
    public float SweepEndPause => sweepEndPause;
    public float ViewRange => viewRange;
    public float ViewAngle => viewAngle;
    public float TrackingSpeed => trackingSpeed;
    public float PanLimit => panLimit;
    public float MaxTiltDown => maxTiltDown;
    public float MaxTiltUp => maxTiltUp;
    public float LoseSightDelay => loseSightDelay;
    public Material ScanOverlayMaterial => scanOverlayMaterial;
    public Material ScanBeamMaterial => scanBeamMaterial;
    public float ScanLockOnTime => scanLockOnTime;
    public float ScanFillDuration => scanFillDuration;
    public float ScanDrainDuration => scanDrainDuration;
    public float ScanPassDuration => scanPassDuration;
    public float ScanFadeTime => scanFadeTime;
}
