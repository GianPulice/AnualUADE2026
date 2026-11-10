using UnityEngine;

/// <summary>
/// How a family of hanging cloth behaves (every curtain shares one asset): how heavy and stiff it is,
/// how far a body can carry it, and how fine its simulation grid is. Read by <see cref="HangingCloth"/>.
/// </summary>
[CreateAssetMenu(fileName = "SO_HangingCloth", menuName = "Scriptable Objects/Environment/Hanging Cloth")]
public class SO_HangingClothConfig : ScriptableObject
{
    [Header("Hasta dónde se mueve")]
    [Tooltip("Metros que puede viajar el borde de ABAJO desde donde cuelga. Es el tope: un cuerpo lleva " +
             "la tela hacia adelante hasta acá y después la atraviesa. Las filas de arriba se mueven " +
             "proporcionalmente menos, hasta nada en el barral. Es también lo que impide que la malla " +
             "se rompa: nada sale de este alcance, pase lo que pase.")]
    [SerializeField, Range(0.05f, 2f)] private float maxSwing = 0.8f;

    [Tooltip("Cuánto se puede estirar la tela a lo alto (1.05–1.2). Más = se ve elástica.")]
    [SerializeField, Range(1f, 1.5f)] private float maxStretch = 1.1f;

    [Header("Cómo se mueve")]
    [Tooltip("Gravedad, m/s². Más = tela más pesada, vuelve antes.")]
    [SerializeField, Range(1f, 30f)] private float gravity = 9.81f;

    [Tooltip("Cuánta velocidad conserva por paso (resistencia del aire). Más bajo = se frena antes; " +
             "más alto = se queda balanceando más tiempo.")]
    [SerializeField, Range(0.9f, 0.999f)] private float damping = 0.985f;

    [Tooltip("Rigidez de la tela: cuánto tira para volver a su forma, por paso. 0 = trapo; 0.1 = lona.")]
    [SerializeField, Range(0f, 0.2f)] private float restPull = 0.03f;

    [Tooltip("Pasadas de las restricciones por paso. Más = menos elástica y más cara.")]
    [SerializeField, Range(2, 12)] private int iterations = 6;

    [Tooltip("Cuánto frena un cuerpo a la tela que toca (0 = resbala, 1 = se le pega).")]
    [SerializeField, Range(0f, 1f)] private float bodyFriction = 0.35f;

    [Tooltip("Metros de aire entre la tela y el cuerpo.")]
    [SerializeField, Range(0f, 0.3f)] private float thickness = 0.05f;

    [Tooltip("Velocidad máxima de un punto de la tela, m/s. Otro seguro contra latigazos.")]
    [SerializeField, Range(1f, 15f)] private float maxSpeed = 6f;

    [Header("Simulación")]
    [Tooltip("Metros entre puntos de la grilla. Más chico = dobla más fino y cuesta más. La malla de " +
             "la cortina sólo tiene ~6 filas de vértices a lo alto, así que por debajo de 0.25 no se nota.")]
    [SerializeField, Range(0.1f, 0.6f)] private float particleSpacing = 0.25f;

    [Tooltip("Metros alrededor de la tela desde los que un cuerpo la despierta. Dormida no cuesta nada.")]
    [SerializeField, Range(0.2f, 3f)] private float wakeDistance = 0.8f;

    [Tooltip("Recalcular las normales mientras se mueve, para que la luz acompañe el pliegue.")]
    [SerializeField] private bool recalculateNormals = true;

    [Header("Sonido")]
    [Tooltip("Sonido (id de AudioManager) cuando un cuerpo la empieza a mover. Vacío = sin sonido.")]
    [SerializeField, SoundId] private string swishSoundId;

    public float ParticleSpacing => particleSpacing;
    public float WakeDistance => wakeDistance;
    public float MaxSwing => maxSwing;
    public bool RecalculateNormals => recalculateNormals;
    public string SwishSoundId => swishSoundId;

    public HangingClothSolver.Settings SolverSettings => new HangingClothSolver.Settings
    {
        gravity = gravity, damping = damping, iterations = iterations, maxSwing = maxSwing, maxStretch = maxStretch,
        maxSpeed = maxSpeed, thickness = thickness, bodyFriction = bodyFriction, restPull = restPull,
    };
}
