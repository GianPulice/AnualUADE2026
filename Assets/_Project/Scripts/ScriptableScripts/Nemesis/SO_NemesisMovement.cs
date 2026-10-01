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

    // ── Drops between floors (plan §15) ─────────────────────────────────────
    //
    // Also hand-driven: NemesisElevatorUser moves the body down a NemesisDropLink itself, through
    // the phases of plan §15.3, with the agent still standing on the link. Every field has an
    // initialiser for the reason given above. The shape of the drop (apex, gravity, air time, hang
    // depth) takes its defaults from DropTuning, which is also what the drop's gizmo draws with.

    [Header("Bajadas entre pisos (plan §15)")]
    [Tooltip("Costo del área NemesisDrop mientras caza: Chasing, Traversing, Searching e " +
             "Investigating. Barato, y más barato que el montacargas (10): cazando, una bajada es la " +
             "ruta corta que el jugador no puede tomar (plan D11).")]
    [SerializeField, Min(1f)] private float dropCostWhileHunting = 2f;

    [Tooltip("Costo del área NemesisDrop patrullando. Caro: una patrulla que se tira por el hueco en " +
             "cada ronda deja de asustar a la tercera (D11). Si es la única ruta a un waypoint, igual " +
             "la usa.")]
    [SerializeField, Min(1f)] private float dropCostWhilePatrolling = 20f;

    [Tooltip("Grados por segundo al girar hacia el hueco cuando llega al borde. Rápido: el giro no " +
             "es el aviso, lo es asomarse.")]
    [SerializeField, Min(1f)] private float dropAlignTurnSpeed = 360f;

    [Tooltip("Segundos que se queda mirando hacia abajo antes de tirarse, con el gruñido. ES EL " +
             "AVISO: desde abajo el jugador lo ve asomarse y lo oye antes de que caiga. Animación " +
             "'Drop Look' (0.5–0.8 s).")]
    [SerializeField, Min(0f)] private float dropLookTime = 0.6f;

    [Tooltip("Segundos que flexiona antes de saltar, en una bajada corta (Hop). Animación " +
             "'Hop Takeoff' (0.3–0.5 s).")]
    [SerializeField, Min(0f)] private float hopTakeoffTime = 0.35f;

    [Tooltip("Segundos que tarda en darse vuelta de espaldas al hueco y apoyar las manos en el " +
             "borde, en una bajada alta (Hang). Animación 'Hang Turn' (0.6–0.9 s).")]
    [SerializeField, Min(0f)] private float hangTurnTime = 0.75f;

    [Tooltip("Segundos que tarda en pasar del borde a colgarse de las manos, antes de soltarse. " +
             "Animación 'Hang Release' (0.3–0.5 s).")]
    [SerializeField, Min(0f)] private float hangReleaseTime = 0.4f;

    [Tooltip("Metros que baja el cuerpo al colgarse del borde antes de soltarse, más o menos el alto " +
             "del cuerpo. Una bajada alta es colgarse y caer el resto: así se lee como bajar, no como " +
             "tirarse de un techo.")]
    [SerializeField, Min(0f)] private float hangDepth = DropTuning.DefaultHangDepth;

    [Tooltip("Metros que sube una bajada corta (Hop) antes de caer. Con 0 se deja caer desde donde " +
             "está parado y el cuerpo roza el canto del piso.")]
    [SerializeField, Min(0f)] private float hopApexHeight = DropTuning.DefaultHopApexHeight;

    [Tooltip("Gravedad del arco, en m/s². 9.8 es la real; más alto pesa más y cae más rápido.")]
    [SerializeField, Min(1f)] private float dropGravity = DropTuning.DefaultGravity;

    [Tooltip("Segundos mínimos en el aire. Sólo pesa en caídas muy cortas, que sin esto se ven como " +
             "un teletransporte.")]
    [SerializeField, Min(0.05f)] private float dropMinAirTime = DropTuning.DefaultMinAirTime;

    [Tooltip("Segundos que se queda en el piso al aterrizar, sin poder agarrar a nadie. ES LA " +
             "VENTANA DEL JUGADOR (plan §15.3): con menos se siente injusto. Tiene que durar lo " +
             "mismo que la animación 'Land Heavy' (0.6–0.9 s).")]
    [SerializeField, Min(0f)] private float dropRecoveryTime = 0.75f;

    [Header("Bajadas: estados del Animator (plan §15.5)")]
    [Tooltip("Nombres de los estados del controller del Nemesis. Si falta uno, esa fase se hace " +
             "igual, sin animación; Tools > Nemesis > Validate Navigation Setup avisa cuáles faltan.")]
    [SerializeField] private string dropLookState = "Drop Look";
    [SerializeField] private string hopTakeoffState = "Hop Takeoff";
    [SerializeField] private string hangTurnState = "Hang Turn";
    [SerializeField] private string hangReleaseState = "Hang Release";
    [SerializeField] private string fallLoopState = "Fall Loop";
    [SerializeField] private string landHeavyState = "Land Heavy";

    [Tooltip("Segundos de fundido hacia cada estado de la bajada (0.1–0.15).")]
    [SerializeField, Min(0f)] private float dropCrossFade = 0.12f;

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
    public float DropCostWhileHunting { get => dropCostWhileHunting; set => dropCostWhileHunting = value; }
    public float DropCostWhilePatrolling { get => dropCostWhilePatrolling; set => dropCostWhilePatrolling = value; }
    public float DropAlignTurnSpeed { get => dropAlignTurnSpeed; set => dropAlignTurnSpeed = value; }
    public float DropLookTime { get => dropLookTime; set => dropLookTime = value; }
    public float HopTakeoffTime { get => hopTakeoffTime; set => hopTakeoffTime = value; }
    public float HangTurnTime { get => hangTurnTime; set => hangTurnTime = value; }
    public float HangReleaseTime { get => hangReleaseTime; set => hangReleaseTime = value; }
    public float HangDepth { get => hangDepth; set => hangDepth = value; }
    public float HopApexHeight { get => hopApexHeight; set => hopApexHeight = value; }
    public float DropGravity { get => dropGravity; set => dropGravity = value; }
    public float DropMinAirTime { get => dropMinAirTime; set => dropMinAirTime = value; }
    public float DropRecoveryTime { get => dropRecoveryTime; set => dropRecoveryTime = value; }
    public float DropCrossFade { get => dropCrossFade; set => dropCrossFade = value; }

    /// <summary>The shape of a drop with these numbers. The hang threshold is the Nemesis's
    /// FloorHeightThreshold and the body radius its agent's, neither of which lives here.</summary>
    public DropTuning DropTuningFor(float hangThreshold, float bodyRadius) =>
        new DropTuning(hangThreshold, hopApexHeight, dropGravity, dropMinAirTime, hangDepth, bodyRadius);

    /// <summary>The Animator state played for a phase of a drop, or null for a phase with none.
    /// </summary>
    public string AnimatorStateFor(EDropPhase phase)
    {
        switch (phase)
        {
            case EDropPhase.Look:        return dropLookState;
            case EDropPhase.HopTakeoff:  return hopTakeoffState;
            case EDropPhase.HangTurn:    return hangTurnState;
            case EDropPhase.HangRelease: return hangReleaseState;
            case EDropPhase.Fall:        return fallLoopState;
            case EDropPhase.Land:        return landHeavyState;
            default:                     return null;
        }
    }
}
