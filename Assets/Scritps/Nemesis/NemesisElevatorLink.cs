using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Marks a <see cref="MovingPlatform"/> as a freight elevator the Nemesis can use, and creates the
/// NavMeshLink that makes pathfinding aware of it.
///
/// Why a link is needed and baking the platform is not enough: the NavMesh is static. The platform
/// moves, so whatever gets baked from it stays at its bake position and is useless as a bridge
/// between floors. A NavMeshLink between the two landings is what tells the agent "there is a way
/// to the other floor here, and it costs this much" — and then something has to actually perform
/// that crossing, which is <see cref="NemesisElevatorUser"/>'s job.
///
/// SCENE SETUP:
///   1. Put this component on the freight elevator (or on an empty object next to it).
///   2. Assign the platform and both landings: bottomLanding where the Nemesis stands to go up,
///      topLanding where it ends up on arrival. Both must land on the NavMesh.
///   3. ridePoint is where it stands during the trip. Make it a CHILD of the platform so it
///      follows the movement without anyone having to recompute it.
///   4. The NavMeshLink is created and configured in Awake — no need to add it by hand.
/// </summary>
[RequireComponent(typeof(NavMeshLink))]
public class NemesisElevatorLink : MonoBehaviour
{
    [Header("Freight elevator")]
    [Tooltip("The platform that goes up and down. Left empty it is looked up on this object and " +
             "its children.")]
    [SerializeField] private MovingPlatform platform;

    [Header("Landings")]
    [Tooltip("Point on the lower floor's NavMesh where the Nemesis stands to go up.")]
    [SerializeField] private Transform bottomLanding;

    [Tooltip("Point on the upper floor's NavMesh where it steps off on arrival.")]
    [SerializeField] private Transform topLanding;

    [Tooltip("Where it stands during the trip. Make it a child of the platform so it moves with " +
             "it. Left empty, the platform's centre is used.")]
    [SerializeField] private Transform ridePoint;

    [Header("Cost")]
    [Tooltip("How much crossing the elevator costs for pathfinding.\n\n" +
             "High = only used when there is no reasonable staircase. Low = preferred. It is the " +
             "one knob deciding whether the Nemesis becomes a regular of the lift. Negative uses " +
             "the area cost with no surcharge, which is usually wrong: a lift ride takes far " +
             "longer than its length in metres.")]
    [SerializeField] private float traversalCost = 10f;

    [Tooltip("Width of the link. Slightly more than the agent radius is enough.")]
    [SerializeField, Min(0.1f)] private float linkWidth = 1.5f;

    private NavMeshLink link;
    private bool isUsable;

    public MovingPlatform Platform => platform;
    public Transform BottomLanding => bottomLanding;
    public Transform TopLanding => topLanding;

    /// <summary>
    /// Whether the elevator is set up correctly and can be used. <see cref="NemesisElevatorUser"/>
    /// checks this before starting a traversal: with it false, the link is treated as a plain one
    /// (crossed by interpolation) instead of blowing up on a null platform.
    /// </summary>
    public bool IsUsable => isUsable;

    /// <summary>Where the Nemesis stands during the trip. Read every time rather than cached: if
    /// it is a child of the platform, its position changes constantly.</summary>
    public Vector3 RidePosition =>
        ridePoint != null ? ridePoint.position :
        platform != null ? platform.transform.position : transform.position;

    private void Awake()
    {
        if (platform == null) platform = GetComponentInChildren<MovingPlatform>();

        link = GetComponent<NavMeshLink>();

        isUsable = ValidateSetup();

        if (!isUsable)
        {
            // With the link off, the agent simply never sees this crossing: it keeps patrolling its
            // own floor instead of getting wedged trying to use a misconfigured elevator.
            if (link != null) link.enabled = false;
            enabled = false;
            return;
        }

        ConfigureLink();
    }

    private bool ValidateSetup()
    {
        if (platform != null && bottomLanding != null && topLanding != null) return true;

        Debug.LogError($"[{nameof(NemesisElevatorLink)}] '{name}' is incomplete " +
                       $"(platform: {(platform != null ? "ok" : "MISSING")}, " +
                       $"bottomLanding: {(bottomLanding != null ? "ok" : "MISSING")}, " +
                       $"topLanding: {(topLanding != null ? "ok" : "MISSING")}). " +
                       "The link is disabled and the Nemesis will not use this elevator.", this);
        return false;
    }

    /// <summary>
    /// Ties the link's ends to the two landings.
    ///
    /// Via startTransform/endTransform and not startPoint/endPoint: those are LOCAL positions that
    /// have to be converted by hand, with the extra trap that NavMeshLink ignores the GameObject's
    /// scale — so a scaled object (and this scene's freight elevator is scaled 4x0.5x4) puts the
    /// ends anywhere but where they belong. Transforms are taken in world space and avoid all of it.
    ///
    /// Bidirectional on purpose: the elevator serves both up and down, and which way the next trip
    /// actually goes is decided by the platform, from where it is parked.
    /// </summary>
    private void ConfigureLink()
    {
        link.startTransform = bottomLanding;
        link.endTransform = topLanding;
        link.bidirectional = true;
        link.width = linkWidth;
        link.costModifier = traversalCost;
        link.autoUpdate = true;

        link.UpdateLink();

        WarnIfLandingOffNavMesh(bottomLanding, nameof(bottomLanding));
        WarnIfLandingOffNavMesh(topLanding, nameof(topLanding));
    }

    /// <summary>
    /// A landing off the NavMesh turns the link into scenery: the agent never picks it up and the
    /// elevator looks like it "does not work". This is reported here, on load, rather than when
    /// someone notices mid-playtest that the Nemesis never goes upstairs.
    /// </summary>
    private void WarnIfLandingOffNavMesh(Transform landing, string fieldName)
    {
        if (NemesisNav.IsOnNavMesh(landing.position)) return;

        Debug.LogWarning($"[{nameof(NemesisElevatorLink)}] '{name}': {fieldName} " +
                         $"('{landing.name}') does not land on the NavMesh. The link will not " +
                         "connect to anything and the Nemesis will never use this elevator. Drop " +
                         "it onto the floor, or check that its area is baked.", landing);
    }

    /// <summary>Which of the two landings is closer to a point. This is how a requested trip is
    /// resolved as going up or going down.</summary>
    public bool IsAtBottomSide(Vector3 position) =>
        Vector3.SqrMagnitude(position - bottomLanding.position) <=
        Vector3.SqrMagnitude(position - topLanding.position);

    /// <summary>The landing opposite the one being stood on: where the trip ends.</summary>
    public Transform GetExitLanding(Vector3 fromPosition) =>
        IsAtBottomSide(fromPosition) ? topLanding : bottomLanding;

    private void OnDrawGizmosSelected()
    {
        if (bottomLanding == null || topLanding == null) return;

        Gizmos.color = new Color(0.2f, 1f, 0.6f);
        Gizmos.DrawWireSphere(bottomLanding.position, 0.4f);
        Gizmos.DrawWireSphere(topLanding.position, 0.4f);
        Gizmos.DrawLine(bottomLanding.position, topLanding.position);

        if (ridePoint == null) return;

        Gizmos.color = new Color(1f, 0.9f, 0.2f);
        Gizmos.DrawWireCube(ridePoint.position, new Vector3(0.6f, 0.1f, 0.6f));
    }
}
