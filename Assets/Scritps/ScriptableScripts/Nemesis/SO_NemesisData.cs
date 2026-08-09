using UnityEngine;

[CreateAssetMenu(fileName = "SO_NemesisData", menuName = "Scriptable Objects/SO_NemesisData")]
public class SO_NemesisData : ScriptableObject
{
    [SerializeField] private float investigationTimeOut;
    [SerializeField] private float searchTimeOut;
    [SerializeField] private float visionLossGracePeriod;
    [SerializeField] private float patrolWaypointWaitTime;
    [SerializeField] private float noiseUpdateCooldown;

    [Header("Player feedback")]
    [Tooltip("Distance at which the proximity vignette starts to show. Independent of the " +
             "vision range: tension has to rise even if the Nemesis has never seen you. " +
             "A bit larger than the FieldOfView's viewRange (10 in the prefab) works well.")]
    [SerializeField] private float proximityRadius = 12f;

    public float InvestigationTimeOut { get => investigationTimeOut; set => investigationTimeOut = value; }
    public float SearchTimeOut { get => searchTimeOut; set => searchTimeOut = value; }
    public float VisionLossGracePeriod { get => visionLossGracePeriod; set => visionLossGracePeriod = value; }
    public float PatrolWaypointWaitTime { get => patrolWaypointWaitTime; set => patrolWaypointWaitTime = value; }
    public float NoiseUpdateCooldown { get => noiseUpdateCooldown; set => noiseUpdateCooldown = value; }
    public float ProximityRadius { get => proximityRadius; set => proximityRadius = value; }
}
