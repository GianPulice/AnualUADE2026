using UnityEngine;

[CreateAssetMenu(fileName = "SO_NemesisData", menuName = "Scriptable Objects/SO_NemesisData")]
public class SO_NemesisData : ScriptableObject
{
    [SerializeField] private float investigationTimeOut;
    [SerializeField] private float searchTimeOut;
    [SerializeField] private float visionLossGracePeriod;
    [SerializeField] private float patrolWaypointWaitTime;
    [SerializeField] private float noiseUpdateCooldown;

    [Header("Feedback al jugador")]
    [Tooltip("Distancia a la que empieza a notarse la vignette de proximidad. Independiente " +
             "del rango de vision: la tension tiene que subir aunque el Nemesis no te haya visto. " +
             "Conviene un poco mas que el viewRange del FieldOfView (10 en el prefab).")]
    [SerializeField] private float proximityRadius = 12f;

    public float InvestigationTimeOut { get => investigationTimeOut; set => investigationTimeOut = value; }
    public float SearchTimeOut { get => searchTimeOut; set => searchTimeOut = value; }
    public float VisionLossGracePeriod { get => visionLossGracePeriod; set => visionLossGracePeriod = value; }
    public float PatrolWaypointWaitTime { get => patrolWaypointWaitTime; set => patrolWaypointWaitTime = value; }
    public float NoiseUpdateCooldown { get => noiseUpdateCooldown; set => noiseUpdateCooldown = value; }
    public float ProximityRadius { get => proximityRadius; set => proximityRadius = value; }
}
