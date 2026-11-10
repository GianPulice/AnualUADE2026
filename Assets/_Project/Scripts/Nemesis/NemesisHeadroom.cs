using UnityEngine;

/// <summary>
/// How far the Nemesis has to duck right now: the headroom over where it stands and a step ahead,
/// against how tall it is. Its one job — measuring. <see cref="NemesisLocomotionLean"/> turns the
/// answer into a hunch of the torso. Added to the Nemesis's root by <see cref="NemesisStateManager"/>.
///
/// Why (10/11/2026): the model walks 2.30 m tall (its baked mesh over E_Walk), and Zona 2's door
/// frames, headers, pipes and the passage under SUB_02's gallery leave 2.15–2.31 m over the routes
/// it patrols. The NavMesh agent is 2 m tall, so it paths under all of them and its head went through.
/// Shrinking the agent or cutting the NavMesh there would cut its routes; a 2.3 m monster bending
/// under a door frame is the better read anyway.
///
/// Sphere casts straight up from the hips, at the body and along the way it is moving: a vertical
/// wall beside it never stops a cast that rises parallel to it, so door jambs do not count — only
/// what is overhead. Triggers and its own colliders are ignored; everything else counts, Ignore
/// Raycast included (props that should duck it without being baked or blocking the player go there).
/// </summary>
public class NemesisHeadroom : MonoBehaviour
{
    [Tooltip("Metros de alto del Nemesis caminando, de los pies a lo más alto de la malla. Medido el " +
             "10/11/2026 sobre E_Walk: 2,30 m. Si cambia la escala del prefab o el modelo, medirlo de nuevo.")]
    [SerializeField, Min(0.5f)] private float bodyHeight = 2.3f;

    [Tooltip("Margen libre que deja sobre la cabeza.")]
    [SerializeField, Min(0f)] private float margin = 0.08f;

    [Tooltip("Grados de encorvamiento por metro que tiene que bajar. Medido: 15° bajan 0,12 m, 30° " +
             "0,25 m, 45° 0,40 m → ~115°/m.")]
    [SerializeField, Min(1f)] private float degreesPerMetre = 115f;

    [Tooltip("Encorvamiento máximo. Pasado ~45° la cabeza ya no baja más (lo que sube es la espalda).")]
    [SerializeField, Range(0f, 60f)] private float maxDuckAngle = 45f;

    [Tooltip("Metros hacia adelante en los que mira lo que tiene encima, en la dirección en la que se " +
             "mueve: empieza a agacharse antes de llegar al marco, no debajo.")]
    [SerializeField, Min(0f)] private float lookAhead = 1.1f;

    [Tooltip("Radio del barrido hacia arriba: el ancho de la cabeza y los hombros.")]
    [SerializeField, Min(0.05f)] private float probeRadius = 0.25f;

    [Tooltip("Segundos en que se agacha (rápido) y en que se vuelve a enderezar (más lento).")]
    [SerializeField, Min(0.01f)] private float duckTime = 0.15f;
    [SerializeField, Min(0.01f)] private float riseTime = 0.45f;

    /// <summary>Degrees the torso should hunch forward now, eased.</summary>
    public float DuckAngle { get; private set; }

    /// <summary>Lowest headroom found this frame, in metres over the feet (99 = nothing overhead).</summary>
    public float Clearance { get; private set; } = 99f;

    private const float ProbeStartHeight = 1.2f;
    private Vector3 lastPosition;
    private Vector3 heading;
    private readonly RaycastHit[] hits = new RaycastHit[8];
    private Collider[] own;

    private void Awake()
    {
        own = GetComponentsInChildren<Collider>(true);
        lastPosition = transform.position;
        heading = transform.forward;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 position = transform.position;
        Vector3 step = position - lastPosition;
        step.y = 0f;
        lastPosition = position;

        // Ahead is where it is going, or where it faces when it stands.
        if (step.sqrMagnitude > 0.0001f && step.magnitude < 1f) heading = step.normalized;
        else if (step.sqrMagnitude <= 0.0001f) heading = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        float clearance = 99f;
        clearance = Mathf.Min(clearance, Probe(position));
        clearance = Mathf.Min(clearance, Probe(position + heading * (lookAhead * 0.5f)));
        clearance = Mathf.Min(clearance, Probe(position + heading * lookAhead));
        Clearance = clearance;

        float drop = Mathf.Max(0f, bodyHeight + margin - clearance);
        float target = Mathf.Min(drop * degreesPerMetre, maxDuckAngle);

        float time = target > DuckAngle ? duckTime : riseTime;
        DuckAngle = Mathf.Lerp(DuckAngle, target, 1f - Mathf.Exp(-dt / time));
    }

    /// <summary>Headroom over a point at the feet's height, or 99 when nothing is within reach.</summary>
    private float Probe(Vector3 feet)
    {
        Vector3 origin = feet + Vector3.up * ProbeStartHeight;
        float reach = bodyHeight + margin - ProbeStartHeight + 0.2f;
        int count = Physics.SphereCastNonAlloc(origin, probeRadius, Vector3.up, hits, reach, ~0, QueryTriggerInteraction.Ignore);

        float best = 99f;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            // A cast that starts overlapping a collider reports it at distance 0: that is something
            // beside the body (a wall it brushes), not something over it.
            if (hit.distance <= 0f || IsOwn(hit.collider) || IsPlayer(hit.collider)) continue;
            best = Mathf.Min(best, ProbeStartHeight + hit.distance + probeRadius);
        }

        return best;
    }

    private bool IsOwn(Collider c)
    {
        for (int i = 0; i < own.Length; i++) if (own[i] == c) return true;
        return false;
    }

    private static bool IsPlayer(Collider c) => c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic;

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = DuckAngle > 1f ? new Color(1f, 0.5f, 0.1f) : new Color(0.3f, 0.9f, 0.4f);
        Vector3 top = transform.position + Vector3.up * (bodyHeight + margin);
        Gizmos.DrawLine(top - transform.right * 0.3f, top + transform.right * 0.3f);
        if (Clearance < 50f) Gizmos.DrawWireSphere(transform.position + Vector3.up * Clearance, 0.1f);
    }
}
