using UnityEngine;

[CreateAssetMenu(fileName = "SO_DirectorPacing", menuName = "Scriptable Objects/SO_DirectorPacing")]
public class SO_DirectorPacing : ScriptableObject
{
    [Header("Medidor: qué lo sube (por segundo)")]
    [Tooltip("Por segundo, multiplicado por la proximidad (0..1, medida por NavMesh dentro de " +
             "Proximity Radius del Nemesis). Tenerlo encima sin que te vea también es tensión.")]
    [SerializeField, Min(0f)] private float proximityGain = 0.05f;

    [Tooltip("Por segundo mientras te persigue (Chasing, o Catch sin resolver).")]
    [SerializeField, Min(0f)] private float chaseGain = 0.08f;

    [Tooltip("Por segundo mientras el jugador tiene al Nemesis a la vista: raycast de la cabeza " +
             "del jugador al pecho del Nemesis contra las mismas paredes que usan sus sentidos. " +
             "No usa la cámara.")]
    [SerializeField, Min(0f)] private float playerSeesNemesisGain = 0.05f;

    [Tooltip("Por segundo mientras el jugador está escondido y el Nemesis busca o investiga cerca.")]
    [SerializeField, Min(0f)] private float hiddenNearSearchGain = 0.04f;

    [Header("Medidor: cómo baja")]
    [Tooltip("Cuánto baja por segundo cuando no pasa nada. Nunca baja en Chasing ni en Catch.")]
    [SerializeField, Min(0f)] private float decayPerSecond = 0.03f;

    [Tooltip("Segundos sin estímulos antes de empezar a bajar.")]
    [SerializeField, Min(0f)] private float decayDelay = 4f;

    [Header("Qué cuenta como verlo")]
    [Tooltip("Hasta qué distancia el jugador 've' al Nemesis. La niebla de visión va de 6 m " +
             "(oscuro) a 25 m (iluminado).")]
    [SerializeField, Min(1f)] private float playerSightRange = 12f;

    [Tooltip("Una búsqueda con un avistamiento más nuevo que esto todavía es el encuentro: " +
             "PeakFade la espera. Se lee del último avistamiento que guarda la creencia del Nemesis, " +
             "no de sus ojos.")]
    [SerializeField, Min(0.1f)] private float freshSightSeconds = 6f;

    [Header("Ritmo")]
    [Tooltip("Con la tensión en este valor, el ritmo entra en SustainPeak.")]
    [SerializeField, Range(0.1f, 1f)] private float peakThreshold = 0.85f;

    [Tooltip("Cuánto dura SustainPeak (se sortea entre x e y).")]
    [SerializeField] private Vector2 sustainPeakSeconds = new Vector2(3f, 5f);

    [Tooltip("Cuánto dura Relax, la retirada (se sortea entre x e y). Left 4 Dead usa 30-45 s.")]
    [SerializeField] private Vector2 relaxSeconds = new Vector2(30f, 45f);

    [Header("Sensibilidad creciente (BuildUp)")]
    [Tooltip("Segundos sin encuentros en BuildUp antes de que el Director empiece a presionar la " +
             "zona del jugador. No cuenta mientras el jugador está en el Hub. Qué es un encuentro: " +
             "ver 'Qué cuenta como encuentro', al final.")]
    [SerializeField, Min(5f)] private float quietTimeout = 90f;

    [Tooltip("Intensidad del primer pedido de presión.")]
    [SerializeField, Range(0f, 1f)] private float risingStartIntensity = 0.3f;

    [Tooltip("Cuánto sube la intensidad en cada renovación sin contacto.")]
    [SerializeField, Range(0f, 1f)] private float risingIntensityStep = 0.15f;

    [Tooltip("Techo de la rampa.")]
    [SerializeField, Range(0f, 1f)] private float risingMaxIntensity = 1f;

    [Tooltip("Cada cuántos segundos se renueva el pedido: vuelve a elegir la zona donde está el " +
             "jugador y sube un escalón.")]
    [SerializeField, Min(5f)] private float risingRepeatSeconds = 20f;

    [Header("Retirada (Relax)")]
    [Tooltip("Intensidad de la presión sobre la zona más lejana al jugador. Sólo mueve el ancla y " +
             "los pesos de ruta: sin ruido y sin sentidos extra. Si te lo cruzás, te persigue igual.")]
    [SerializeField, Range(0f, 1f)] private float retreatIntensity = 0.8f;

    // Campos nuevos siempre al final: un asset viejo los lee con el valor por defecto de acá.

    [Header("Persistencia de la búsqueda (plan §18.5 C1)")]
    //
    // Multiplica Search Quiet Window y Search Hard Cap del Nemesis según el ritmo: cuánto silencio
    // tolera antes de dejar de buscar, y el tope. Es un préstamo de números sobre una copia de
    // SO_NemesisData, como el boost de sentidos, y se devuelve. Nunca hace que ignore lo que siente:
    // una evidencia fresca renueva la búsqueda igual.

    [Tooltip("Persistencia en BuildUp, sin sensibilidad creciente. 1 = la búsqueda dura lo que dice " +
             "SO_NemesisData.")]
    [SerializeField, Min(0.1f)] private float buildUpPersistence = 1f;

    [Tooltip("Persistencia con la sensibilidad creciente al máximo. Sube con su intensidad: entre " +
             "Build Up Persistence (intensidad 0) y esto (intensidad 1). Si hace rato que no pasa " +
             "nada, cuando te encuentra te busca más (Mr. X, C1).\n\n" +
             "Por encima de 1.5, subir también Search Tail Timeout de NemesisChaseMusic: tiene que " +
             "quedar por encima de Search Hard Cap × esto, o la música se corta a mitad de búsqueda.")]
    [SerializeField, Min(0.1f)] private float risingMaxPersistence = 1.5f;

    [Tooltip("Persistencia en SustainPeak.")]
    [SerializeField, Min(0.1f)] private float sustainPeakPersistence = 1f;

    [Tooltip("Persistencia en PeakFade. Por debajo de 1 ayuda a que el encuentro termine solo, que " +
             "es lo que PeakFade espera para pasar a Relax.")]
    [SerializeField, Min(0.1f)] private float peakFadePersistence = 0.75f;

    [Tooltip("Persistencia en Relax: corta antes y se va, así la retirada deja de ser invisible. Con " +
             "0.5 la ventana de silencio queda en ~4 s; si el Nemesis se siente regalado, subir a 0.75.")]
    [SerializeField, Min(0.1f)] private float relaxPersistence = 0.5f;

    [Header("Vuelve a pasar (plan §18.5 C2)")]
    [Tooltip("Segundos (se sortea entre x e y) entre una búsqueda que terminó vacía en BuildUp y la " +
             "presión sobre la zona de esa búsqueda. Si en el medio termina otra vacía, cuenta desde " +
             "la última. Sorteada para que no se lea como una cita.\n\n" +
             "Se aplica sólo con el Nemesis patrullando, todavía en BuildUp y sin otra presión (un " +
             "puzzle o la sensibilidad creciente ganan). Si al vencer está cazando, reintenta 10 s " +
             "después. Una captura o salir de BuildUp la cancelan; en Relax nunca se programa.")]
    [SerializeField] private Vector2 revisitDelay = new Vector2(20f, 40f);

    [Tooltip("Intensidad de esa presión. Sólo ancla y pesos de ruta: sin ruido y sin sentidos. Es un " +
             "sesgo de la patrulla, no una orden de ir.")]
    [SerializeField, Range(0f, 1f)] private float revisitIntensity = 0.5f;

    [Tooltip("Cuántos segundos dura esa presión.")]
    [SerializeField, Min(1f)] private float revisitDuration = 30f;

    [Header("Qué cuenta como encuentro (silencio, plan §18.5 C3)")]
    //
    // El silencio (Quiet Time) se mide por encuentros, no por metros. Lo reinician: la persecución,
    // "el jugador lo ve", escondido con búsqueda cerca, una captura, y lo de abajo. La proximidad sigue
    // subiendo el medidor igual (Proximity Gain): lo que cambia es sólo qué corta el silencio.

    [Tooltip("Proximidad (0..1, por NavMesh dentro de Proximity Radius del Nemesis) por encima de la " +
             "cual tenerlo cerca ya es un encuentro. 0.75 ≈ 3 m con el radio de 12 m.\n\n" +
             "Alto a propósito: en la testbed el 94 % del NavMesh queda a menos de 12 m de alguna ruta, " +
             "y cuando cualquier pasada contaba, la patrulla reiniciaba el silencio sola.")]
    [SerializeField, Range(0f, 1f)] private float quietProximityThreshold = 0.75f;

    [Tooltip("Segundos: el Nemesis buscando o investigando con evidencia del jugador más nueva que " +
             "esto es un encuentro. Una pista (señuelo, ruido del Director) no cuenta: no es el jugador.")]
    [SerializeField, Min(0f)] private float encounterBeliefFreshness = 10f;

    public float ProximityGain => proximityGain;
    public float ChaseGain => chaseGain;
    public float PlayerSeesNemesisGain => playerSeesNemesisGain;
    public float HiddenNearSearchGain => hiddenNearSearchGain;
    public float DecayPerSecond => decayPerSecond;
    public float DecayDelay => decayDelay;
    public float PlayerSightRange => playerSightRange;
    public float FreshSightSeconds => freshSightSeconds;
    public float PeakThreshold => peakThreshold;
    public Vector2 SustainPeakSeconds => sustainPeakSeconds;
    public Vector2 RelaxSeconds => relaxSeconds;
    public float QuietTimeout => quietTimeout;
    public float RisingStartIntensity => risingStartIntensity;
    public float RisingIntensityStep => risingIntensityStep;
    public float RisingMaxIntensity => risingMaxIntensity;
    public float RisingRepeatSeconds => risingRepeatSeconds;
    public float RetreatIntensity => retreatIntensity;
    public float BuildUpPersistence => buildUpPersistence;
    public float RisingMaxPersistence => risingMaxPersistence;
    public float SustainPeakPersistence => sustainPeakPersistence;
    public float PeakFadePersistence => peakFadePersistence;
    public float RelaxPersistence => relaxPersistence;
    public Vector2 RevisitDelay => revisitDelay;
    public float RevisitIntensity => revisitIntensity;
    public float RevisitDuration => revisitDuration;
    public float QuietProximityThreshold => quietProximityThreshold;
    public float EncounterBeliefFreshness => encounterBeliefFreshness;

    public float RollSustainPeak() => Roll(sustainPeakSeconds);
    public float RollRelax() => Roll(relaxSeconds);
    public float RollRevisitDelay() => Roll(revisitDelay);

    private static float Roll(Vector2 range)
    {
        float min = Mathf.Max(0f, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        return Random.Range(min, max);
    }
}
