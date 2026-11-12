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

    // ── Locomotion animation ────────────────────────────────────────────────
    //
    // The model has ONE locomotion clip, E_Walk (2.5 s, in place, no root motion). Walking and
    // running both play it, at the rate the body is actually moving: the feet stay planted at any
    // speed, and there is no crossfade between two copies of the same clip to pop the stride.

    [Header("Locomoción (animación)")]
    [Tooltip("Metros por segundo que cubre el ciclo de E_Walk reproducido a velocidad 1. Medido el " +
             "10/11/2026: cada pie barre ~1,78 m por paso a la escala del prefab, 1,2–1,4 m/s. Si los " +
             "pies patinan hacia adelante, subilo; si pisan para atrás, bajalo.")]
    [SerializeField, Min(0.1f)] private float walkClipSpeed = 1.3f;

    [Tooltip("Cuánto de la velocidad por encima de la natural del clip lo ponen las piernas. 1 = todo " +
             "(pies clavados, pero a 4,5 m/s es una caminata frenética: se ve ridículo, 10/11/2026). " +
             "Menos = la cadencia crece más despacio y el resto se lee como zancada larga, con algo de " +
             "patinaje. 0,5 se parece al ×2 fijo de antes. Por debajo de Walk Clip Speed no cuenta.")]
    [SerializeField, Range(0.3f, 1f)] private float cadenceExponent = 0.6f;

    [Tooltip("Velocidad de reproducción mínima mientras se mueve: arrancando, no se ve en cámara lenta.")]
    [SerializeField, Min(0.05f)] private float minLocomotionRate = 0.6f;

    [Tooltip("Velocidad de reproducción máxima. Por encima, las piernas dejan de acelerar y el " +
             "cuerpo patina un poco: es el techo de lo que el clip de caminar aguanta sin verse " +
             "frenético (a 4,5 m/s pide 3,5).")]
    [SerializeField, Min(0.1f)] private float maxLocomotionRate = 3.5f;

    [Tooltip("Segundos que tarda la reproducción en seguir un cambio de velocidad. Suaviza el " +
             "arranque y la frenada sin que las piernas vayan atrasadas.")]
    [SerializeField, Min(0f)] private float locomotionRateSmoothing = 0.15f;

    // ── Hand-driven movement ────────────────────────────────────────────────
    //
    // Used while NemesisLinkUser is moving the Nemesis itself across a link. They live here rather than on that component because they are the same kind of value
    // as everything above — how fast the monster moves — and a designer tuning its weight should
    // not have to know which of the two systems happens to be driving it at the time.
    //
    // Initialisers matter on this asset: unlike SO_NemesisData nothing here had one, so a field
    // added without a default deserialises to 0 on the existing asset — which for a speed means
    // the Nemesis freezes mid-traversal and never completes the link.

    [Header("Link crossing (by hand)")]
    [Tooltip("Metres per second crossing a plain NavMeshLink — a jump.")]
    [SerializeField, Min(0.1f)] private float linkTraversalSpeed = 2.5f;

    [Tooltip("Metres per second it walks at right after breaking a low-pass barrier.")]
    [SerializeField, Min(0.1f)] private float boardingSpeed = 1.5f;

    [Tooltip("Degrees per second it turns while being moved by hand.\n\n" +
             "Separate from angularSpeed because nothing else writes rotation while a link is " +
             "crossed by hand.")]
    [SerializeField, Min(1f)] private float traversalTurnSpeed = 180f;

    public float PatrolSpeed { get => patrolSpeed; set => patrolSpeed = value; }
    public float InvestigationSpeed { get => investigationSpeed; set => investigationSpeed = value; }
    public float ChaseSpeed { get => chaseSpeed; set => chaseSpeed = value; }
    public float SearchSpeed { get => searchSpeed; set => searchSpeed = value; }
    public float AngularSpeed { get => angularSpeed; set => angularSpeed = value; }
    public float Acceleration { get => acceleration; set => acceleration = value; }
    public float StoppingDistance { get => stoppingDistance; set => stoppingDistance = value; }
    public float WalkClipSpeed => walkClipSpeed;
    public float CadenceExponent => cadenceExponent;
    public float MinLocomotionRate => minLocomotionRate;
    public float MaxLocomotionRate => Mathf.Max(maxLocomotionRate, minLocomotionRate);
    public float LocomotionRateSmoothing => locomotionRateSmoothing;
    public float LinkTraversalSpeed { get => linkTraversalSpeed; set => linkTraversalSpeed = value; }
    public float BoardingSpeed { get => boardingSpeed; set => boardingSpeed = value; }
    public float TraversalTurnSpeed { get => traversalTurnSpeed; set => traversalTurnSpeed = value; }
}
