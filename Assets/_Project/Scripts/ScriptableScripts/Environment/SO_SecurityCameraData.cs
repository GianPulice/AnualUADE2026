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
}
