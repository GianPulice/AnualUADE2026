using System.Collections.Generic;
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
/// SCENE SETUP — the hierarchy is not a matter of taste, it is the whole thing:
///
///   ElevatorRoot          &lt;- THIS component + NavMeshLink. Static. Never moves.
///   |-- Cabin             &lt;- MovingPlatform. The only object that travels.
///   |   \-- RidePoint     &lt;- child of the cabin, so it follows for free
///   |-- BottomLanding     &lt;- sibling of the cabin, on the lower floor's NavMesh
///   \-- TopLanding        &lt;- sibling of the cabin, on the upper floor's NavMesh
///
///   1. This component and its NavMeshLink go on the STATIC root, never on the cabin. The link
///      registers itself relative to its own GameObject's transform, so mounting it on the cabin
///      makes Unity tear the link down and rebuild it every frame of the ride — and it can
///      vanish from under an agent that is halfway across it.
///   2. bottomLanding and topLanding must NOT be children of the cabin. Parented to it they ride
///      along, the link's two ends leave the floor with the platform, and the crossing the agent
///      was promised stops existing the moment the lift moves. This is the single most common way
///      to get a "the Nemesis ignores the elevator" bug.
///   3. ridePoint IS a child of the cabin — that is what makes it follow the ride for free. Put
///      it on the cabin's top surface, not at its centre, or the Nemesis travels sunk into it.
///   4. Keep the root at scale 1 and scale the cabin instead. A scaled root multiplies every
///      landing's local offset and makes the numbers in the inspector unreadable.
///   5. The NavMeshLink is configured in Awake — no need to set its ends by hand. Anything wired
///      into its startTransform/endTransform in the scene is overwritten from the landings above.
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

    [Header("Cabin floor")]
    [Tooltip("Give the cabin a NavMesh of its own, so the Nemesis WALKS aboard instead of being " +
             "interpolated onto it.\n\n" +
             "On, the cabin floor is real ground: it is pathed onto around the shaft wall like " +
             "any other floor, and a player standing in a parked cabin can be chased and grabbed " +
             "there with no special case. Off, boarding falls back to a straight line from the " +
             "landing to the ride point — which passes through the landing barrier, because a " +
             "straight line is what it is.\n\n" +
             "Turn it off only to compare against the old behaviour.")]
    [SerializeField] private bool giveCabinItsOwnNavMesh = true;

    private NavMeshLink link;
    private bool isUsable;
    private ElevatorCabinNavMesh cabinNav;

    /// <summary>
    /// Every usable elevator currently in the scene.
    ///
    /// It exists because <see cref="NemesisNav.TryGetRoute"/> has to answer "did this path go
    /// through a lift?", and NavMeshPath gives it nothing but corner positions — no way to ask
    /// which corner was a link. Matching those corners against the landings is the only route to
    /// the answer, and that needs the landings to be findable without walking the scene graph
    /// every query.
    ///
    /// Registered from OnEnable and not Awake so a misconfigured elevator never enters the list:
    /// Awake disables this component when validation fails, and a component disabled during Awake
    /// never gets its OnEnable.
    /// </summary>
    private static readonly List<NemesisElevatorLink> active = new List<NemesisElevatorLink>();

    public static IReadOnlyList<NemesisElevatorLink> Active => active;

    /// <summary>
    /// Static state survives leaving Play mode when domain reload is disabled. OnDisable removes
    /// entries and Unity does raise it on the way out of Play, so in practice this list empties
    /// itself — but "in practice" is exactly the reasoning every other static hub in the project
    /// declined to rely on, and a stale entry here is not harmless: NemesisNav.FindCrossedElevator
    /// walks this list to decide whether a path goes through a lift, so a destroyed elevator left
    /// in it makes the Nemesis commit to a shaft that no longer exists.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active.Clear();

    public MovingPlatform Platform => platform;
    public Transform BottomLanding => bottomLanding;
    public Transform TopLanding => topLanding;

    /// <summary>The cabin's own NavMesh, or null when this shaft does not have one. See
    /// <see cref="ElevatorCabinNavMesh"/>.</summary>
    public ElevatorCabinNavMesh CabinNav => cabinNav;

    /// <summary>
    /// Takes the shaft link out of pathfinding, or puts it back.
    ///
    /// <see cref="NemesisElevatorUser"/> switches it off for the duration of a crossing it is
    /// already performing, and that is not tidiness: an agent standing on a link it may not
    /// auto-traverse cannot be steered anywhere — asked to keep going, it grinds along the link
    /// direction, which points through the shaft wall. Removing the link is what hands the body
    /// back to ordinary pathfinding so the walk aboard can be a walk.
    ///
    /// Through <c>activated</c> rather than <c>enabled</c>: it is one call into the navigation
    /// system, it leaves the component's own bookkeeping (and the endpoints it tracks) untouched,
    /// and it cannot be confused with the component having been switched off by a designer.
    /// </summary>
    public void SetShaftLinkActive(bool active)
    {
        if (link != null) link.activated = active;
    }

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
        CalibrateRideDistance();
        EnsureCabinNavMesh();
    }

    /// <summary>
    /// Adds the cabin's NavMesh component when the shaft is asking for one.
    ///
    /// Auto-added, unlike <see cref="NemesisElevatorUser"/> which deliberately is not, because the
    /// two are different kinds of thing. That one is a feature with scene wiring behind it, and a
    /// level with no lift should not silently grow a monster that uses one. This one has NO wiring:
    /// it derives everything it needs from the cabin's collider and the two landings, which this
    /// component has already validated. Requiring it to be dragged on by hand would mean every
    /// existing elevator prefab keeps the wall-crossing boarding until someone remembers.
    /// </summary>
    private void EnsureCabinNavMesh()
    {
        if (!giveCabinItsOwnNavMesh) return;

        cabinNav = GetComponent<ElevatorCabinNavMesh>();
        if (cabinNav == null) cabinNav = gameObject.AddComponent<ElevatorCabinNavMesh>();
    }

    /// <summary>
    /// Tells the platform how far this shaft actually is, measured from the two landings.
    ///
    /// The travel distance used to come from SO_MovingPlatform, which is a ScriptableObject and
    /// therefore shared by every platform in the project — so a single number had to be right for
    /// every lift at once, and it was not: the asset says 8 while the two shafts in this project
    /// span 4.95 and 6.63. The failure is quiet and nasty. The cabin still moves, still arrives,
    /// still reports HasArrived; it just stops a metre or two off the floor, and the Nemesis is
    /// then walked from the ride point to a landing that is no longer level with it — stepping
    /// out through the air, or into the slab.
    ///
    /// Measured rather than authored because the answer is already in the scene. The gap between
    /// the landings IS the distance the cabin has to cover: park it flush with the lower one and
    /// it ends up flush with the upper one. That also means moving a landing in the editor
    /// re-tunes the lift by itself, with no second number to remember to update.
    /// </summary>
    private void CalibrateRideDistance()
    {
        float shaftHeight = Mathf.Abs(topLanding.position.y - bottomLanding.position.y);

        if (shaftHeight < 0.01f)
        {
            // Both landings at the same height is not a lift. Left alone, the platform would use
            // the config's distance and travel somewhere neither landing is.
            Debug.LogWarning($"[{nameof(NemesisElevatorLink)}] '{name}': bottomLanding and " +
                             $"topLanding are at the same height, so there is no shaft to " +
                             "measure. Falling back to the shared config distance, which is " +
                             "almost certainly wrong for this lift.", this);
            return;
        }

        platform.SetRideDistance(shaftHeight);
    }

    private void OnEnable()
    {
        if (!isUsable || active.Contains(this)) return;
        active.Add(this);
    }

    private void OnDisable() => active.Remove(this);

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

    /// <summary>The landing being stood at: where the trip starts, and where a failed one has to
    /// put the passenger back.</summary>
    public Transform GetBoardingLanding(Vector3 fromPosition) =>
        IsAtBottomSide(fromPosition) ? bottomLanding : topLanding;

    /// <summary>
    /// Which landing the cabin is parked at RIGHT NOW, measured from the ride point instead of read
    /// off <see cref="MovingPlatform.GoingUp"/>.
    ///
    /// GoingUp answers "which way would the NEXT trip leave", and that is the same fact as "which
    /// end is it parked at" only while the cabin is idle. Mid-trip it still describes the trip in
    /// progress, so a cabin halfway up the shaft — because the player pressed a call panel — still
    /// reports itself as sitting at the bottom. Every "is it on my floor?" test that read the flag
    /// was therefore answering about the past, and boarding walked the Nemesis into thin air on the
    /// strength of it.
    ///
    /// Compared on height alone: the shaft is vertical, so Y is the only axis that separates the
    /// two landings, and the horizontal offset between them (3.7 m in this scene) only eats into
    /// the margin of a full 3D comparison.
    /// </summary>
    public bool IsCabinAtBottom =>
        Mathf.Abs(RidePosition.y - bottomLanding.position.y) <=
        Mathf.Abs(RidePosition.y - topLanding.position.y);

    /// <summary>Whether the cabin is parked on the same side as <paramref name="position"/> — the
    /// question "can something standing here actually step onto it".</summary>
    public bool IsCabinOnSameSideAs(Vector3 position) => IsCabinAtBottom == IsAtBottomSide(position);

    private void OnDrawGizmosSelected()
    {
        if (bottomLanding == null || topLanding == null) return;

        Color landingColor = new Color(0.2f, 1f, 0.6f);
        Gizmos.color = landingColor;
        Gizmos.DrawWireSphere(bottomLanding.position, 0.4f);
        Gizmos.DrawWireSphere(topLanding.position, 0.4f);
        Gizmos.DrawLine(bottomLanding.position, topLanding.position);

        // "bottom"/"top" plus the scene object's own name (e.g. "Start"/"End" in this project) —
        // the role alone does not tell you which physical object it is if the two ever get
        // renamed, and the name alone does not tell you which end of the shaft it serves.
        Label(bottomLanding.position, $"bottom ({bottomLanding.name})", landingColor);
        Label(topLanding.position, $"top ({topLanding.name})", landingColor);

        if (ridePoint == null) return;

        Color rideColor = new Color(1f, 0.9f, 0.2f);
        Gizmos.color = rideColor;
        Gizmos.DrawWireCube(ridePoint.position, new Vector3(0.6f, 0.1f, 0.6f));
        Label(ridePoint.position, $"ride point ({ridePoint.name})", rideColor);
    }

    private static void Label(Vector3 position, string text, Color color)
    {
#if UNITY_EDITOR
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.Label(position + Vector3.up * 0.5f, text);
#endif
    }
}
