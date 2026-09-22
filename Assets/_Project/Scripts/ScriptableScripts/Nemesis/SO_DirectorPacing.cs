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
             "PeakFade la espera. Mismo orden que Sight Commit Time del Nemesis.")]
    [SerializeField, Min(0.1f)] private float freshSightSeconds = 6f;

    [Header("Ritmo")]
    [Tooltip("Con la tensión en este valor, el ritmo entra en SustainPeak.")]
    [SerializeField, Range(0.1f, 1f)] private float peakThreshold = 0.85f;

    [Tooltip("Cuánto dura SustainPeak (se sortea entre x e y).")]
    [SerializeField] private Vector2 sustainPeakSeconds = new Vector2(3f, 5f);

    [Tooltip("Cuánto dura Relax, la retirada (se sortea entre x e y). Left 4 Dead usa 30-45 s.")]
    [SerializeField] private Vector2 relaxSeconds = new Vector2(30f, 45f);

    [Header("Sensibilidad creciente (BuildUp)")]
    [Tooltip("Segundos sin contacto en BuildUp antes de que el Director empiece a presionar la " +
             "zona del jugador. No cuenta mientras el jugador está en el Hub.")]
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

    public float RollSustainPeak() => Roll(sustainPeakSeconds);
    public float RollRelax() => Roll(relaxSeconds);

    private static float Roll(Vector2 range)
    {
        float min = Mathf.Max(0f, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        return Random.Range(min, max);
    }
}
