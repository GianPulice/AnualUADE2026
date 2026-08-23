using UnityEngine;

[CreateAssetMenu(fileName = "SO_NemesisMovement", menuName = "Scriptable Objects/SO_NemesisMovement")]
public class SO_NemesisMovement : ScriptableObject
{
    [Header("Speeds by state")]
    [SerializeField] private float patrolSpeed;
    [SerializeField] private float investigationSpeed;
    [SerializeField] private float chaseSpeed;
    [SerializeField] private float searchSpeed;

    [Header("NavMeshAgent tuning")]
    [Tooltip("Degrees per second, applied to the NavMeshAgent on activation. Around 200 gives the " +
             "heavy, committed turn of a large pursuer; very high values pivot on the spot with " +
             "no arc at all.")]
    [SerializeField] private float angularSpeed;

    [SerializeField] private float acceleration;
    [SerializeField] private float stoppingDistance;

    // ── Hand-driven movement ────────────────────────────────────────────────
    //
    // Used while NemesisElevatorUser is moving the Nemesis itself, with the NavMeshAgent switched
    // off. They live here rather than on that component because they are the same kind of value
    // as everything above — how fast the monster moves — and a designer tuning its weight should
    // not have to know which of the two systems happens to be driving it at the time.
    //
    // Initialisers matter on this asset: unlike SO_NemesisData nothing here had one, so a field
    // added without a default deserialises to 0 on the existing asset — which for a speed means
    // the Nemesis freezes mid-traversal and never completes the link.

    [Header("Link traversal (agent switched off)")]
    [Tooltip("Metres per second crossing a plain NavMeshLink — a jump or a drop.")]
    [SerializeField, Min(0.1f)] private float linkTraversalSpeed = 2.5f;

    [Tooltip("Metres per second stepping onto and off the freight elevator platform.")]
    [SerializeField, Min(0.1f)] private float boardingSpeed = 1.5f;

    [Tooltip("Degrees per second it turns while being moved by hand.\n\n" +
             "Separate from angularSpeed because the NavMeshAgent is OFF during a traversal and " +
             "nothing else writes rotation: without this the Nemesis rides the whole shaft — and " +
             "arrives — facing whatever way it happened to step onto the link.")]
    [SerializeField, Min(1f)] private float traversalTurnSpeed = 180f;

    public float PatrolSpeed { get => patrolSpeed; set => patrolSpeed = value; }
    public float InvestigationSpeed { get => investigationSpeed; set => investigationSpeed = value; }
    public float ChaseSpeed { get => chaseSpeed; set => chaseSpeed = value; }
    public float SearchSpeed { get => searchSpeed; set => searchSpeed = value; }
    public float AngularSpeed { get => angularSpeed; set => angularSpeed = value; }
    public float Acceleration { get => acceleration; set => acceleration = value; }
    public float StoppingDistance { get => stoppingDistance; set => stoppingDistance = value; }
    public float LinkTraversalSpeed { get => linkTraversalSpeed; set => linkTraversalSpeed = value; }
    public float BoardingSpeed { get => boardingSpeed; set => boardingSpeed = value; }
    public float TraversalTurnSpeed { get => traversalTurnSpeed; set => traversalTurnSpeed = value; }
}
