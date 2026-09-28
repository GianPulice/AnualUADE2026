using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The phases of a drop, in the order <see cref="NemesisElevatorUser"/> runs them (plan §15.3).
/// Each one but <see cref="Align"/> has an Animator state of its own (plan §15.5).
/// </summary>
public enum EDropPhase
{
    None,

    /// <summary>Turns to face the gap.</summary>
    Align,

    /// <summary>Leans out and looks down, with the growl. THE TELL: from below, this is what the
    /// player sees and hears before anything happens.</summary>
    Look,

    /// <summary>Hop only: flexes before jumping.</summary>
    HopTakeoff,

    /// <summary>Hang only: turns its back to the gap and puts its hands on the edge. Still on the
    /// floor, so a capture can still cut the drop short here.</summary>
    HangTurn,

    /// <summary>Hang only: goes over the edge until it hangs by its hands, and lets go. From here
    /// on nothing cancels the drop.</summary>
    HangRelease,

    /// <summary>In the air.</summary>
    Fall,

    /// <summary>Has landed, and stays down for the recovery: the player's window.</summary>
    Land,
}

/// <summary>The one-shots of a drop, played by <see cref="NemesisAudio.PlayDropCue"/>.</summary>
public enum EDropCue
{
    Growl,
    HandSlam,
    Impact,
}

/// <summary>
/// A one-way way DOWN for the Nemesis (plan §15): a hole in the floor, a broken railing, the edge
/// of a catwalk. It hops down a short one; from a tall one it hangs by the edge and lets go. To go
/// back up it still takes the stairs or the freight elevator.
///
/// Mirrors <see cref="NemesisElevatorLink"/>: this marks the spot and creates the NavMeshLink that
/// makes pathfinding aware of it, and <see cref="NemesisElevatorUser"/> performs the crossing. The
/// drop is a third branch there, not a second component watching isOnOffMeshLink (plan §10).
///
/// What it is NOT:
/// - A shortcut the player never sees. Every drop is announced (it stops at the edge, looks down
///   and growls before it goes), and it lands hard and stays down a moment: the player's window.
/// - A way up.
/// - A duct (plan §2.3). The Nemesis never leaves the NavMesh.
///
/// SCENE SETUP (plan §15.6):
///
///   Drop_&lt;place&gt;        &lt;- THIS component + NavMeshLink. Static. Scale 1.
///   |-- TopEdge          &lt;- on the upper NavMesh, 0.3-0.5 m back from the edge
///   \-- BottomLanding    &lt;- on the lower NavMesh, 0.8-1.5 m out from below the edge
///
///   1. The NavMeshLink is configured in Awake from the two ends: one way (top to bottom), on the
///      NemesisDrop area, with no cost override. Whatever is typed into it by hand is overwritten.
///   2. The kind comes from the height alone: up to FloorHeightThreshold (2.5 m) a Hop, above it a
///      Hang. Between 1.5 and 5 m.
///   3. It faces the way the landing lies, so the ends' rotation does not matter. That is also
///      why the landing needs to be out from the edge: straight below it reads as a lift.
///   4. Keep the player off it unless the level means to share it (D9): a rail at the edge on
///      Ignore Raycast. It still collides with the player's capsule, and nothing else notices it
///      (the bake, the senses, the arc check). Not on Props, which blocks the Nemesis's sight down,
///      and not on Player, where FieldOfView's target mask would take the rail for the player.
///   5. It needs a way back up from the landing (stairs or lift). Without one it splits
///      NemesisRouteGraph's islands and can trap the Nemesis downstairs.
///
/// Tools/Nemesis/Validate Navigation Setup checks all of the above. Adding the component in the
/// editor creates the two ends if they are missing.
/// </summary>
[RequireComponent(typeof(NavMeshLink))]
public class NemesisDropLink : MonoBehaviour
{
    /// <summary>The NavMesh area the drops live on (plan §15.2). An area of their own so their
    /// cost can follow what the Nemesis is doing: cheap while it hunts, dear on patrol (D11).
    /// </summary>
    public const string AreaName = "NemesisDrop";

    /// <summary>Index 5, which the plan reserves for it. Used while the area has not been named in
    /// Project Settings &gt; Navigation &gt; Areas: unnamed, it still exists and still works.</summary>
    public const int FallbackArea = 5;

    /// <summary>Shortest and tallest drop worth authoring (plan §12). Below, the agent's step climb
    /// covers it. Above, a humanoid does not fall without consequences.</summary>
    public const float MinHeight = 1.5f;
    public const float MaxHeight = 5f;

    /// <summary>Where the edge is assumed to be, ahead of the start, when the NavMesh cannot say.
    /// The middle of the 0.3-0.5 m the setup asks for.</summary>
    private const float AssumedEdgeDistance = 0.4f;

    [Header("Ends")]
    [Tooltip("Sobre el NavMesh de ARRIBA, a 0.3-0.5 m del borde. Ahí se para, mira hacia abajo y se " +
             "tira. La rotación no importa: mira hacia donde está el aterrizaje.")]
    [SerializeField] private Transform topEdge;

    [Tooltip("Sobre el NavMesh de ABAJO, a 0.8-1.5 m de la vertical del borde: el arco necesita " +
             "avance horizontal, y justo abajo se ve como un ascensor. Con 1 m libre alrededor y fuera " +
             "del Hub (a más de 3 m de su puerta).")]
    [SerializeField] private Transform bottomLanding;

    [Header("Link")]
    [Tooltip("Ancho de los dos extremos del link. El agente lo toma en cualquier punto a lo ancho, " +
             "así que más ancho es un borde más ancho por donde tirarse.")]
    [SerializeField, Min(0.1f)] private float linkWidth = 1f;

    private NavMeshLink link;
    private bool isUsable;

    /// <summary>
    /// Every usable drop currently in the scene, for <see cref="NemesisNav"/>: it has to answer "did
    /// this path go down a drop?" from nothing but corner positions, and matching them against the
    /// ends needs the ends to be findable without walking the scene graph every query. Same list,
    /// for the same reason, as <see cref="NemesisElevatorLink.Active"/>.
    ///
    /// Registered from OnEnable and not Awake, so a drop that failed validation never enters it.
    /// </summary>
    private static readonly List<NemesisDropLink> active = new List<NemesisDropLink>();

    public static IReadOnlyList<NemesisDropLink> Active => active;

    /// <summary>Static state survives leaving Play mode when domain reload is disabled, and a
    /// destroyed drop left in the list would make the Nemesis commit to a way down that no longer
    /// exists.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active.Clear();

    /// <summary>The area index of <see cref="AreaName"/>, or <see cref="FallbackArea"/> while it is
    /// not named.</summary>
    public static int Area
    {
        get
        {
            int byName = NavMesh.GetAreaFromName(AreaName);
            return byName >= 0 ? byName : FallbackArea;
        }
    }

    public Transform TopEdge => topEdge;
    public Transform BottomLanding => bottomLanding;

    /// <summary>Whether the drop is set up and in use. <see cref="NemesisElevatorUser"/> crosses a
    /// link whose drop is not usable as a plain one, instead of blowing up on a missing end.</summary>
    public bool IsUsable => isUsable;

    /// <summary>The link is out of pathfinding for now: the Nemesis has just used it, or has just
    /// given up on it, and it is cooling down (see NemesisElevatorUser.SuspendDrop).</summary>
    public bool IsSuspended => link != null && !link.activated;

    /// <summary>Metres from the top end down to the landing.</summary>
    public float Height =>
        topEdge != null && bottomLanding != null ? topEdge.position.y - bottomLanding.position.y : 0f;

    /// <summary>What it faces while it looks down: towards the landing. TopEdge's own forward only
    /// for a landing straight below, where that direction is not defined.</summary>
    public Vector3 FallbackFacing => topEdge != null ? topEdge.forward : transform.forward;

    /// <summary>
    /// Takes the link out of pathfinding, or puts it back. Through <c>activated</c>, like
    /// NemesisElevatorLink.SetShaftLinkActive, and for the same reasons: one call into the
    /// navigation system, and it cannot be mistaken for a designer switching the component off.
    /// </summary>
    public void SetLinkActive(bool activated)
    {
        if (link != null) link.activated = activated;
    }

    /// <summary>
    /// The whole way down from <paramref name="start"/> to <paramref name="end"/>: see
    /// <see cref="DropPath"/>. The same call the traversal, the gizmo and the validator make, so
    /// the three of them agree on where the body goes.
    /// </summary>
    public DropPath PlanFrom(Vector3 start, Vector3 end, in DropTuning tuning, int areaMask)
    {
        // The edge only matters to a Hang, and finding it costs two NavMesh queries.
        Vector3 edge = DropPath.KindFor(start.y - end.y, tuning.HangThreshold) == EDropKind.Hang
            ? FindEdge(start, end, tuning.BodyRadius, areaMask)
            : start;

        return DropPath.Create(start, edge, end, FallbackFacing, tuning);
    }

    /// <summary>
    /// The rim of the floor <paramref name="start"/> stands on, in the direction of
    /// <paramref name="end"/>, at the start's height.
    ///
    /// Read off the NavMesh: a raycast along the floor stops where the baked mesh ends, and the
    /// bake keeps the agent's radius away from any drop-off, so the rim itself is one radius
    /// further on. When the mesh cannot say (it runs past the landing, or the start is off it) the
    /// edge is assumed where the setup asks TopEdge to be placed from it.
    /// </summary>
    public static Vector3 FindEdge(Vector3 start, Vector3 end, float bodyRadius, int areaMask)
    {
        Vector3 across = end - start;
        across.y = 0f;

        float reach = across.magnitude;
        if (reach < 0.01f) return start;

        Vector3 direction = across / reach;

        if (NavMesh.SamplePosition(start, out NavMeshHit onMesh, 0.5f, areaMask) &&
            NavMesh.Raycast(onMesh.position, onMesh.position + direction * reach, out NavMeshHit rim, areaMask))
        {
            Vector3 edge = rim.position + direction * Mathf.Max(0f, bodyRadius);
            edge.y = start.y;
            return edge;
        }

        return start + direction * Mathf.Min(AssumedEdgeDistance, reach * 0.5f);
    }

    private void Awake()
    {
        link = GetComponent<NavMeshLink>();

        isUsable = ValidateSetup();

        if (!isUsable)
        {
            // With the link off the agent simply never sees this drop, instead of trying to cross a
            // link with no ends it can make sense of.
            if (link != null) link.enabled = false;
            enabled = false;
            return;
        }

        ConfigureLink();
    }

    private void OnEnable()
    {
        if (!isUsable || active.Contains(this)) return;
        active.Add(this);
    }

    private void OnDisable() => active.Remove(this);

    private bool ValidateSetup()
    {
        if (topEdge == null || bottomLanding == null)
        {
            Debug.LogError($"[{nameof(NemesisDropLink)}] '{name}' is incomplete " +
                           $"(topEdge: {(topEdge != null ? "ok" : "MISSING")}, " +
                           $"bottomLanding: {(bottomLanding != null ? "ok" : "MISSING")}). The link " +
                           "is disabled and the Nemesis will not use this drop.", this);
            return false;
        }

        if (Height <= 0f)
        {
            Debug.LogError($"[{nameof(NemesisDropLink)}] '{name}': bottomLanding is not below topEdge " +
                           $"({Height:0.00} m). A drop only goes down; the link is disabled.", this);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Ties the link to the two ends, one way.
    ///
    /// Via startTransform/endTransform, like the lift: world positions, with none of the local-space
    /// and ignored-scale traps of startPoint/endPoint.
    ///
    /// NO COST OVERRIDE, and that is load-bearing. A zero or positive costModifier replaces the
    /// area cost outright, and the area cost is the one knob that makes the Nemesis prefer a drop
    /// while it hunts and avoid it on patrol (NemesisStateManager sets it per state, D11). Negative
    /// is "use the area's".
    ///
    /// Static, like the level around it: the link is placed once and not tracked.
    /// </summary>
    private void ConfigureLink()
    {
        link.startTransform = topEdge;
        link.endTransform = bottomLanding;
        link.bidirectional = false;
        link.width = linkWidth;
        link.area = Area;
        link.costModifier = -1f;
        link.autoUpdate = false;

        link.UpdateLink();

        WarnIfEndOffNavMesh(topEdge, nameof(topEdge));
        WarnIfEndOffNavMesh(bottomLanding, nameof(bottomLanding));
    }

    /// <summary>
    /// An end off the NavMesh turns the link into scenery: the agent never picks it up. Reported on
    /// load rather than when someone notices mid-playtest that the Nemesis never drops.
    ///
    /// Tighter than the lift's two metres: an end sampled that far can snap onto the OTHER floor.
    /// </summary>
    private void WarnIfEndOffNavMesh(Transform end, string fieldName)
    {
        if (NemesisNav.IsOnNavMesh(end.position, 0.5f)) return;

        Debug.LogWarning($"[{nameof(NemesisDropLink)}] '{name}': {fieldName} ('{end.name}') does not " +
                         "land on the NavMesh. The link will not connect to anything and the Nemesis " +
                         "will never use this drop. Put it on the floor, or check that its area is " +
                         "baked.", end);
    }

    // ── Gizmos ──────────────────────────────────────────────────────────────

    private static readonly Color HopColor = new Color(0.40f, 0.90f, 0.55f);
    private static readonly Color HangColor = new Color(0.95f, 0.65f, 0.20f);
    private static readonly Color SuspendedColor = new Color(0.50f, 0.50f, 0.50f);

    private const int ArcSegments = 16;

    /// <summary>Radius around the landing that has to be clear of solid geometry (plan §15.6).
    /// </summary>
    public const float LandingClearRadius = 1f;

    /// <summary>
    /// The arc of every drop, always: coloured by kind (green a Hop, amber a Hang), grey while it is
    /// cooling down. Lines only, so it costs next to nothing; the labels are in
    /// <see cref="OnDrawGizmosSelected"/>.
    ///
    /// Drawn with the default tuning, since a drop has no Nemesis to ask: it is the arc of a freshly
    /// created asset. Under NemesisGizmos' master switch, like the routes.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!NemesisGizmos.DrawingEnabled) return;
        if (topEdge == null || bottomLanding == null) return;

        DropPath path = PlanFrom(topEdge.position, bottomLanding.position, DropTuning.Default, NavMesh.AllAreas);

        Gizmos.color = IsSuspended ? SuspendedColor : path.Kind == EDropKind.Hop ? HopColor : HangColor;

        if (path.Kind == EDropKind.Hang)
        {
            Gizmos.DrawLine(path.Start, path.EdgeStand);
            Gizmos.DrawLine(path.EdgeStand, path.HangPoint);
        }

        DropArc arc = path.Arc;
        Vector3 previous = arc.From;

        for (int i = 1; i <= ArcSegments; i++)
        {
            Vector3 next = arc.PointAt(arc.Duration * i / ArcSegments);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }

        Gizmos.DrawWireSphere(path.Start, 0.15f);
        Gizmos.DrawWireSphere(path.End, 0.15f);
    }

    private void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        if (topEdge == null || bottomLanding == null) return;

        float height = Height;
        EDropKind kind = KindFor(DropTuning.DefaultHangThreshold);
        Color color = IsSuspended ? SuspendedColor : kind == EDropKind.Hop ? HopColor : HangColor;

        UnityEditor.Handles.color = color;
        UnityEditor.Handles.DrawWireDisc(bottomLanding.position, Vector3.up, LandingClearRadius);

        string state = IsSuspended ? " · enfriando" : "";
        UnityEditor.Handles.Label(topEdge.position + Vector3.up * 0.5f, $"bajada {kind} · {height:0.0} m{state}");
        UnityEditor.Handles.Label(bottomLanding.position + Vector3.up * 0.5f, "aterrizaje");
#endif
    }

    /// <summary>Which kind a drop this tall is, for a given hang threshold.</summary>
    public EDropKind KindFor(float hangThreshold) => DropPath.KindFor(Height, hangThreshold);

#if UNITY_EDITOR
    /// <summary>
    /// Creates the two ends when the component is added, placed where the setup wants them relative
    /// to this object: the top here, the landing three metres down and a metre and a half out. The
    /// designer moves them onto the level from there.
    /// </summary>
    private void Reset()
    {
        if (topEdge == null) topEdge = FindOrCreateEnd("TopEdge", Vector3.zero);
        if (bottomLanding == null) bottomLanding = FindOrCreateEnd("BottomLanding", new Vector3(0f, -3f, 1.5f));
    }

    private Transform FindOrCreateEnd(string endName, Vector3 localPosition)
    {
        Transform existing = transform.Find(endName);
        if (existing != null) return existing;

        GameObject end = new GameObject(endName);
        UnityEditor.Undo.RegisterCreatedObjectUndo(end, $"Create {endName}");
        end.transform.SetParent(transform, false);
        end.transform.localPosition = localPosition;
        return end.transform;
    }
#endif
}
