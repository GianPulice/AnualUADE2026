using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A low pass: something hanging across a passage low enough that the player has to go UNDER it
/// (crouched, or sliding in from a sprint) and the Nemesis cannot — a half-closed roller shutter, a
/// fallen beam. It does not stop the Nemesis, it costs it time: it walks up, strikes, the barrier
/// is thrown up out of the way, and it goes through. From then on the pass is open to everybody.
///
/// Its one job is to be that barrier: to say whether it still stands, and to open when told. The
/// walking up, the strike and the crossing are the Nemesis's (<c>NemesisLinkUser</c>, which finds
/// this on the link it steps onto).
///
/// HOW THE NEMESIS IS KEPT OUT. Not by the bake: the barrier carries a carving NavMeshObstacle, so the
/// floor under it is cut while it stands and comes back by itself when it is raised. The NavMeshLink
/// on this object bridges that cut, and is what the Nemesis's path takes; once the barrier is up and
/// the Nemesis is across, the link is switched off and the passage is plain floor.
///
/// SETUP (GameObject > WIRED > Nemesis > Low Pass builds this, as a blockout):
///
///   LowPass_&lt;place&gt;      &lt;- THIS + NavMeshLink (configured here, whatever is typed into it is overwritten)
///   |-- Barrier          &lt;- what hangs: mesh + solid collider (layer Wall), underside ~0.95 m over the
///   |   \-- NavBlock     &lt;- floor: the player's crouch is 0.8 m. NavBlock is a carving NavMeshObstacle
///   |                       from the floor up, no collider.
///   |-- SideA, SideB     &lt;- on the NavMesh, either side, ~1.2 m from the barrier.
///
/// The barrier must leave the Nemesis no room to duck: under 1.9 m (NemesisHeadroom hunches down to
/// about that). Nothing here needs baking.
/// </summary>
[RequireComponent(typeof(NavMeshLink))]
public class NemesisBreakLink : MonoBehaviour
{
    [Header("Piezas")]
    [Tooltip("Lo que cuelga y se levanta: la malla, su collider y el NavBlock.")]
    [SerializeField] private Transform barrier;

    [Tooltip("Los dos extremos del cruce, sobre el NavMesh, uno de cada lado.")]
    [SerializeField] private Transform sideA;
    [SerializeField] private Transform sideB;

    [Header("El cruce")]
    [Tooltip("Ancho del paso, en metros.")]
    [SerializeField, Min(0.3f)] private float linkWidth = 1.5f;

    [Tooltip("Costo del cruce mientras la barrera está puesta, en veces su largo. Si hay un rodeo " +
             "más corto que esto, el Nemesis lo prefiere; si no, rompe.")]
    [SerializeField, Min(1f)] private float costWhileStanding = 6f;

    [Header("El golpe")]
    [Tooltip("Segundos desde que llega hasta que el golpe conecta. Es lo que el jugador le gana. " +
             "E_Attack dura 1 s y conecta más o menos a la mitad.")]
    [SerializeField, Min(0f)] private float strikeSeconds = 0.55f;

    [Tooltip("Segundos que se queda después del golpe, mientras la barrera sube, antes de pasar.")]
    [SerializeField, Min(0f)] private float recoverSeconds = 0.45f;

    [Header("La barrera al abrirse")]
    [Tooltip("Metros que sube.")]
    [SerializeField, Min(0f)] private float raiseBy = 1.5f;

    [Tooltip("Segundos que tarda en subir. Corto: es un golpe, no un motor.")]
    [SerializeField, Min(0.01f)] private float raiseSeconds = 0.25f;

    [Tooltip("Sonido del golpe (id de AudioManager). Vacío = sin sonido.")]
    [SerializeField, SoundId] private string breakSoundId;

    private NavMeshLink link;
    private bool broken;

    /// <summary>The barrier has been struck open. It stays open.</summary>
    public bool IsBroken => broken;

    public float StrikeSeconds => strikeSeconds;
    public float RecoverSeconds => recoverSeconds;

    /// <summary>Where the barrier is, for the Nemesis to face.</summary>
    public Vector3 BarrierPosition => barrier != null ? barrier.position : transform.position;

    private void Awake()
    {
        link = GetComponent<NavMeshLink>();

        if (barrier == null || sideA == null || sideB == null)
        {
            Debug.LogWarning($"[{nameof(NemesisBreakLink)}] '{name}' is missing its barrier or one of its " +
                             "sides: the Nemesis cannot cross here.", this);
            link.enabled = false;
            return;
        }

        link.startTransform = sideA;
        link.endTransform = sideB;
        link.bidirectional = true;
        link.width = linkWidth;
        link.costModifier = costWhileStanding;
        link.autoUpdate = false;
        link.UpdateLink();
    }

    /// <summary>
    /// The strike landed: the barrier is thrown up, the floor under it comes back. Idempotent.
    /// </summary>
    public void Break()
    {
        if (broken) return;
        broken = true;

        if (barrier != null)
        {
            // The floor first: by the time the Nemesis steps off the link the cut has to be gone.
            foreach (NavMeshObstacle obstacle in barrier.GetComponentsInChildren<NavMeshObstacle>(true))
                obstacle.enabled = false;

            RaiseAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        if (!string.IsNullOrEmpty(breakSoundId) && AudioManager.Exists)
            AudioManager.Instance.PlaySFX(breakSoundId, BarrierPosition);
    }

    /// <summary>The Nemesis is across: the passage is plain floor from here on.</summary>
    public void NotifyCrossed()
    {
        if (broken && link != null) link.enabled = false;
    }

    private async UniTaskVoid RaiseAsync(CancellationToken token)
    {
        Vector3 from = barrier.position;
        Vector3 to = from + Vector3.up * raiseBy;
        float elapsed = 0f;

        while (elapsed < raiseSeconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / raiseSeconds);

            // Fast out, easing in at the top: thrown, not lifted.
            barrier.position = Vector3.Lerp(from, to, 1f - (1f - t) * (1f - t));
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        barrier.position = to;
    }

    private void OnDrawGizmos()
    {
        if (sideA == null || sideB == null) return;

        Gizmos.color = broken ? new Color(0.4f, 0.9f, 0.4f) : new Color(1f, 0.55f, 0.1f);
        Gizmos.DrawLine(sideA.position, sideB.position);
        Gizmos.DrawWireSphere(sideA.position, 0.15f);
        Gizmos.DrawWireSphere(sideB.position, 0.15f);
    }
}
