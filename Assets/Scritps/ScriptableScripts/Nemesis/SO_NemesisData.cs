using UnityEngine;

[CreateAssetMenu(fileName = "SO_NemesisData", menuName = "Scriptable Objects/SO_NemesisData")]
public class SO_NemesisData : ScriptableObject
{
    [SerializeField] private float investigationTimeOut;
    [SerializeField] private float searchTimeOut;
    [SerializeField] private float visionLossGracePeriod;
    [SerializeField] private float patrolWaypointWaitTime;
    [SerializeField] private float noiseUpdateCooldown;

    // Tuneable detection parameters live here and not on the sensor components so that a
    // designer changes them in one asset, and so Tier 3.3 can scale difficulty by handing the
    // sensors a runtime copy of this SO. The LayerMasks stay on the components: those are scene
    // wiring, not design values.
    //
    // Defaults match what the Nemesis prefab had before the migration (viewRange 10,
    // viewAngle 90, listenRange 10) so moving them here did not retune the enemy.

    [Header("Vision")]
    [Tooltip("Radius of the vision OverlapSphere.")]
    [SerializeField] private float viewRange = 10f;

    [Tooltip("Full width of the vision cone in degrees. Halved when tested against the " +
             "forward vector.")]
    [Range(0, 360)]
    [SerializeField] private float viewAngle = 90f;

    [Tooltip("Hard detection radius. Inside it the Nemesis notices the player no matter what: " +
             "no cone, no occlusion raycast, and it is the only thing that defeats Hidden. " +
             "Keep it well under viewRange — this is 'it is literally on top of me', not a " +
             "second vision range. NOT the same as proximityRadius below, which only drives the " +
             "HUD vignette and detects nothing. 0 disables it.")]
    [SerializeField] private float proximityDetectionRange = 3f;

    [Tooltip("viewRange is multiplied by this while the player is crouching. " +
             "1 = crouching does not help at all, 0.5 = spotted at half the distance.")]
    [SerializeField, Range(0f, 1f)] private float crouchVisionMultiplier = 0.6f;

    [Header("Hearing")]
    [Tooltip("Radius of the hearing OverlapSphere.")]
    [SerializeField] private float listenRange = 10f;

    [Tooltip("Whether a wall between the Nemesis and a noise attenuates it.")]
    [SerializeField] private bool wallOcclusionEnabled = true;

    [Tooltip("Effective range through a wall = listenRange * this. Spec default: 0.6.")]
    [SerializeField, Range(0f, 1f)] private float wallOcclusionMultiplier = 0.6f;

    [Header("Player feedback")]
    [Tooltip("Distance at which the proximity vignette starts to show. Independent of the " +
             "vision range: tension has to rise even if the Nemesis has never seen you. " +
             "A bit larger than the FieldOfView's viewRange (10 in the prefab) works well.")]
    [SerializeField] private float proximityRadius = 12f;

    [Header("Patrol routes (Tier 3.1)")]
    [Tooltip("Chance, rolled once every time Patrolling is (re)entered, of walking the active " +
             "route in the opposite direction for that cycle.")]
    [SerializeField, Range(0f, 1f)] private float routeReverseChance = 0.15f;

    [Tooltip("Chance, rolled once every time Patrolling is (re)entered, of skipping the very " +
             "next waypoint on the first hop of that cycle (advances two waypoints instead of " +
             "one). Rolled independently from routeReverseChance, so both can land together.")]
    [SerializeField, Range(0f, 1f)] private float routeSkipWaypointChance = 0.15f;

    public float InvestigationTimeOut { get => investigationTimeOut; set => investigationTimeOut = value; }
    public float SearchTimeOut { get => searchTimeOut; set => searchTimeOut = value; }
    public float VisionLossGracePeriod { get => visionLossGracePeriod; set => visionLossGracePeriod = value; }
    public float PatrolWaypointWaitTime { get => patrolWaypointWaitTime; set => patrolWaypointWaitTime = value; }
    public float NoiseUpdateCooldown { get => noiseUpdateCooldown; set => noiseUpdateCooldown = value; }
    public float ViewRange { get => viewRange; set => viewRange = value; }
    public float ViewAngle { get => viewAngle; set => viewAngle = value; }
    public float ProximityDetectionRange { get => proximityDetectionRange; set => proximityDetectionRange = value; }
    public float CrouchVisionMultiplier { get => crouchVisionMultiplier; set => crouchVisionMultiplier = value; }
    public float ListenRange { get => listenRange; set => listenRange = value; }
    public bool WallOcclusionEnabled { get => wallOcclusionEnabled; set => wallOcclusionEnabled = value; }
    public float WallOcclusionMultiplier { get => wallOcclusionMultiplier; set => wallOcclusionMultiplier = value; }
    public float ProximityRadius { get => proximityRadius; set => proximityRadius = value; }
    public float RouteReverseChance { get => routeReverseChance; set => routeReverseChance = value; }
    public float RouteSkipWaypointChance { get => routeSkipWaypointChance; set => routeSkipWaypointChance = value; }
}
