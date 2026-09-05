using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Gives the freight elevator's cabin a NavMesh of its own, and connects it to whichever landing
/// the cabin is currently parked at.
///
/// WHY THIS EXISTS
///
/// Before it, the cabin floor was not walkable ground — it was a hole in the world that the
/// Nemesis crossed by hand: <see cref="NemesisElevatorUser"/> switched the agent off and
/// interpolated the body in a straight line from the landing to the ride point. That line runs
/// through the <see cref="ElevatorLandingBarrier"/> and through the shaft wall, because a straight
/// line is exactly what it is. Every symptom of the old boarding came from that one decision: the
/// monster walked through the wall, it walked to the ride point instead of around to the door, and
/// while it was being carried nothing about it was a NavMeshAgent, so nothing about it behaved
/// like one.
///
/// With the cabin carrying its own NavMesh, boarding is a walk. The Nemesis paths onto the cabin
/// the way it paths onto any other floor, the animation follows the body because the body is
/// really moving, and a player standing in a parked cabin is standing on the NavMesh — which is
/// what makes chasing and grabbing them there work with no special case anywhere.
///
/// HOW IT WORKS — two Unity facts do most of the job
///
///   1. A <see cref="NavMeshSurface"/> FOLLOWS ITS TRANSFORM. The package re-adds its NavMesh data
///      instance at the new position whenever the transform moves (NavMeshSurface subscribes to
///      NavMesh.onPreUpdate for exactly this). So a surface parented to the cabin travels with the
///      cabin for free — no per-frame code here does that.
///   2. SEPARATE SURFACES DO NOT CONNECT TO EACH OTHER. Two NavMesh instances that merely touch
///      are still two islands; the only bridge is a <see cref="NavMeshLink"/>. That is where the
///      "arriba y abajo" comes from: one short link per landing, live only while the cabin is
///      parked at that landing.
///
/// Everything is built at runtime from what is already in the scene, so an elevator needs no extra
/// wiring to gain this — the cabin's collider, the two landings and the shaft are all the
/// information required. Nothing is baked into the scene's own NavMesh, which is correct: the
/// cabin moves, and anything baked from it would stay behind at its bake position.
///
/// WHAT IT DELIBERATELY DOES NOT DO
///
/// While the cabin is travelling, both the surface and the links go OFF. There is no NavMesh in
/// the middle of a shaft, on purpose:
///
///   - An island that moves under a live agent is worse than no island. The package moves the data
///     by REMOVING and RE-ADDING it, so an agent standing on it loses isOnNavMesh every frame of
///     the ride, and its path with it. The ride stays what it always was — agent off, body carried
///     by <see cref="MovingPlatform"/> — and this component is what makes the two ENDS solid.
///   - A live link whose far end is climbing the shaft is an invitation to walk into thin air.
/// </summary>
[RequireComponent(typeof(NemesisElevatorLink))]
public class ElevatorCabinNavMesh : MonoBehaviour
{
    [Header("Bake")]
    [Tooltip("Agent type the cabin's NavMesh is built for. Must match the one the level is baked " +
             "for, or the two meshes cannot be linked at all — 0 is Unity's Humanoid, which is " +
             "what this project uses.")]
    [SerializeField] private int agentTypeID = 0;

    [Tooltip("Which layers the cabin's own NavMesh is built from.\n\n" +
             "Left empty it uses the cabin collider's own layer, which is the right answer in " +
             "this project: the cabin is on Interactable, deliberately outside the level bake so " +
             "nothing tries to bake a floor that moves. Set it by hand only if the cabin's " +
             "walkable surface lives on a different object than its collider.")]
    [SerializeField] private LayerMask geometryLayers;

    [Tooltip("Headroom above the cabin floor included in the bake volume. Has to clear the agent's " +
             "height or the floor comes out unwalkable — the voxelizer needs somewhere to stand.")]
    [SerializeField, Min(0.5f)] private float bakeHeadroom = 3f;

    [Tooltip("Smallest patch of NavMesh kept. Above the size of the props inside the cabin (the " +
             "ride button) and well under the cabin floor itself, so the button does not come out " +
             "as a walkable shelf floating at chest height.")]
    [SerializeField, Min(0f)] private float minRegionArea = 1.5f;

    [Header("Boarding")]
    [Tooltip("How far inside the cabin floor the boarding point sits, measured from the edge.\n\n" +
             "It has to clear the agent's radius, or the point lands on the strip the NavMesh " +
             "shrinks away from every edge and the link connects to nothing. 0.9 covers this " +
             "project's radius of 0.5 with room to spare.")]
    [SerializeField, Min(0.1f)] private float boardingInset = 0.9f;

    [Tooltip("Width of the landing-to-cabin links. Comfortably more than the agent radius: a " +
             "narrow link makes the Nemesis thread a needle to get aboard.")]
    [SerializeField, Min(0.1f)] private float boardingLinkWidth = 1.5f;

    private NemesisElevatorLink elevator;
    private MovingPlatform platform;

    private NavMeshSurface surface;
    private Transform bottomDoor;
    private Transform topDoor;
    private NavMeshLink bottomLink;
    private NavMeshLink topLink;

    private bool isReady;

    /// <summary>
    /// Whether the cabin actually carries a NavMesh right now.
    ///
    /// <see cref="NemesisElevatorUser"/> reads this to choose between walking aboard and the old
    /// hand-driven interpolation. False is not a crash: it is the pre-existing behaviour, which
    /// crosses the wall but does cross. Every path to false logs why.
    /// </summary>
    public bool IsReady => isReady;

    /// <summary>Where the Nemesis steps to when boarding from <paramref name="landing"/> — a point
    /// on the cabin floor, inset from the edge nearest that landing. Read live: it is a child of
    /// the cabin and moves with it.</summary>
    public Vector3 BoardingPointFor(Transform landing)
    {
        Transform door = DoorFor(landing);
        return door != null ? door.position : elevator.RidePosition;
    }

    /// <summary>
    /// Whether the cabin floor is walkable AND joined to this landing right now — the exact
    /// question "can something standing here walk aboard".
    /// </summary>
    public bool IsBoardingOpen(Transform landing)
    {
        if (!isReady || surface == null || !surface.enabled) return false;

        NavMeshLink link = LinkFor(landing);
        return link != null && link.enabled;
    }

    /// <summary>
    /// Matched against the shaft's own two landings, and answering null for anything else rather
    /// than falling through to one of them. A caller holding a landing from a different elevator
    /// would otherwise be told that THIS cabin is open to it — and the wrong answer here is a
    /// Nemesis walking into a shaft with no cabin in it.
    /// </summary>
    private NavMeshLink LinkFor(Transform landing)
    {
        if (landing == null) return null;
        if (landing == elevator.BottomLanding) return bottomLink;
        if (landing == elevator.TopLanding) return topLink;
        return null;
    }

    private Transform DoorFor(Transform landing)
    {
        if (landing == null) return null;
        if (landing == elevator.BottomLanding) return bottomDoor;
        if (landing == elevator.TopLanding) return topDoor;
        return null;
    }

    /// <summary>
    /// Built in Start and not Awake: <see cref="NemesisElevatorLink"/> validates the shaft and
    /// calibrates the ride distance in ITS Awake, and a cabin measured before that can be parked
    /// somewhere neither landing is.
    /// </summary>
    private void Start()
    {
        elevator = GetComponent<NemesisElevatorLink>();

        if (!elevator.IsUsable)
        {
            // The shaft itself is misconfigured and has already said so. Adding a second error
            // about the cabin's NavMesh only buries the one that matters.
            enabled = false;
            return;
        }

        platform = elevator.Platform;

        Collider floor = FindCabinCollider();
        if (floor == null)
        {
            Debug.LogError($"[{nameof(ElevatorCabinNavMesh)}] '{name}': the cabin " +
                           $"('{platform.name}') has no non-trigger Collider, so there is nothing " +
                           "to build a floor from. The Nemesis falls back to crossing the shaft " +
                           "by hand, which walks it through the barrier.", this);
            enabled = false;
            return;
        }

        BuildDoors(floor);
        BuildSurface(floor);
        BuildLinks();

        isReady = VerifyBoardingPointsAreWalkable();

        // Only when the mesh is real. Opening a link into a bake that produced nothing adds a
        // second broken thing to debug on top of the one already reported.
        if (isReady) Refresh();
    }

    /// <summary>
    /// The cabin's floor collider: the largest non-trigger one on the platform or below it.
    ///
    /// Triggers are skipped because the cabin's own boarding trigger and its ride button are both
    /// triggers sitting inside the same space, and "largest" because a cabin with a railing or a
    /// button housing has several — the floor is the big one.
    /// </summary>
    private Collider FindCabinCollider()
    {
        Collider best = null;
        float bestVolume = 0f;

        foreach (Collider candidate in platform.GetComponentsInChildren<Collider>())
        {
            if (candidate.isTrigger) continue;

            Vector3 size = candidate.bounds.size;
            float volume = size.x * size.y * size.z;

            if (volume <= bestVolume) continue;

            best = candidate;
            bestVolume = volume;
        }

        return best;
    }

    /// <summary>
    /// Places one boarding point per landing, as children of the cabin so they ride along.
    ///
    /// The point is the landing's own position pulled onto the cabin floor: clamped into the floor
    /// rectangle and then inset from its edge. That means the Nemesis boards through the side the
    /// landing is actually on, which is the whole difference between walking in through the door
    /// and walking in through the wall.
    /// </summary>
    private void BuildDoors(Collider floor)
    {
        bottomDoor = CreateDoor(floor, elevator.BottomLanding, "CabinDoor_Bottom");
        topDoor = CreateDoor(floor, elevator.TopLanding, "CabinDoor_Top");
    }

    private Transform CreateDoor(Collider floor, Transform landing, string doorName)
    {
        Bounds bounds = floor.bounds;

        float halfX = Mathf.Max(0.05f, bounds.extents.x - boardingInset);
        float halfZ = Mathf.Max(0.05f, bounds.extents.z - boardingInset);

        Vector3 point = new Vector3(
            Mathf.Clamp(landing.position.x, bounds.center.x - halfX, bounds.center.x + halfX),
            bounds.max.y,
            Mathf.Clamp(landing.position.z, bounds.center.z - halfZ, bounds.center.z + halfZ));

        GameObject door = new GameObject(doorName);
        door.transform.SetParent(platform.transform, worldPositionStays: true);
        door.transform.position = point;

        return door.transform;
    }

    /// <summary>
    /// Bakes the cabin floor into a NavMesh of its own.
    ///
    /// Collected by VOLUME rather than by children, and that is not a detail: the volume is the one
    /// collect mode that ignores the transform's scale (the package builds its world bounds with a
    /// unit-scale matrix), and this project's cabin is scaled 4.57 x 1 x 4.45. Collecting children
    /// off a scaled transform is how a cabin ends up with a floor several metres wider than itself.
    ///
    /// The volume starts slightly BELOW the floor surface so the collider's top face is inside it —
    /// a volume that begins exactly at the surface voxelizes nothing — and reaches
    /// <see cref="bakeHeadroom"/> above, which has to clear the agent's height or the floor comes
    /// out unwalkable.
    /// </summary>
    private void BuildSurface(Collider floor)
    {
        Bounds bounds = floor.bounds;

        GameObject host = new GameObject("CabinNavMesh");
        host.transform.SetParent(platform.transform, worldPositionStays: true);
        host.transform.position = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        host.transform.rotation = Quaternion.identity;

        surface = host.AddComponent<NavMeshSurface>();
        surface.agentTypeID = agentTypeID;
        surface.collectObjects = CollectObjects.Volume;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

        // Typed as int on both branches: LayerMask converts implicitly in both directions, which
        // makes a ternary mixing the two ambiguous rather than convenient.
        surface.layerMask = geometryLayers.value != 0 ? geometryLayers.value
                                                      : 1 << floor.gameObject.layer;
        surface.defaultArea = 0;                 // Walkable
        surface.minRegionArea = minRegionArea;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;

        const float slabBite = 0.25f;            // how much of the floor slab to include
        const float sideMargin = 0.5f;           // keeps the voxelizer off the geometry's own edge

        surface.size = new Vector3(bounds.size.x + sideMargin,
                                   bakeHeadroom + slabBite,
                                   bounds.size.z + sideMargin);
        surface.center = new Vector3(0f, (bakeHeadroom - slabBite) * 0.5f, 0f);

        surface.BuildNavMesh();
    }

    /// <summary>
    /// One link per landing, mounted on the STATIC root and pointed at a cabin-side transform.
    ///
    /// On a static transform and not on the cabin for the same reason the shaft link is
    /// (<see cref="NemesisElevatorLink"/>'s class doc): a link registers itself relative to its own
    /// GameObject, so one mounted on the cabin is torn down and rebuilt every frame of the ride and
    /// can vanish from under an agent halfway across it. The END is allowed to travel, because
    /// autoUpdate re-points a link whose endpoint transforms have moved.
    ///
    /// Which transform that is has to be worked out rather than assumed — see
    /// <see cref="ResolveStaticRoot"/>.
    /// </summary>
    private void BuildLinks()
    {
        Transform staticRoot = ResolveStaticRoot();

        bottomLink = CreateLink(staticRoot, elevator.BottomLanding, bottomDoor, "BoardingLink_Bottom");
        topLink = CreateLink(staticRoot, elevator.TopLanding, topDoor, "BoardingLink_Top");
    }

    /// <summary>
    /// Something that does NOT travel with the cabin, to hang the boarding links off.
    ///
    /// It cannot just be <c>transform</c>. This component is auto-added next to
    /// <see cref="NemesisElevatorLink"/>, and in this project's prefab that link is mounted on the
    /// cabin rather than on the static root it is documented to live on — so parenting to
    /// <c>transform</c> would put the boarding links on the moving lift, which is precisely the
    /// arrangement both classes warn against.
    ///
    /// The landings are the definition of "does not move": the entire elevator depends on them
    /// staying on their floor while the cabin travels between them, so whatever they hang off is
    /// static by construction. That holds whether the link was wired correctly or not, which is
    /// what makes this safe to rely on rather than a second guess.
    /// </summary>
    private Transform ResolveStaticRoot()
    {
        Transform cabin = platform.transform;

        Transform landingParent = elevator.BottomLanding != null ? elevator.BottomLanding.parent : null;
        if (IsStatic(landingParent, cabin)) return landingParent;

        if (IsStatic(transform, cabin)) return transform;
        if (IsStatic(cabin.parent, cabin)) return cabin.parent;

        // Nothing static to be found: the whole shaft is parented under the cabin somehow. The
        // links still work — they are only ever enabled while the cabin is parked — but they are
        // being rebuilt for the whole of every trip, so it is worth knowing.
        Debug.LogWarning($"[{nameof(ElevatorCabinNavMesh)}] '{name}': found nothing outside the " +
                         "cabin to mount the boarding links on, so they travel with it. Check the " +
                         "shaft's hierarchy — the landings should hang off a root the cabin is a " +
                         "child of.", this);
        return transform;
    }

    private static bool IsStatic(Transform candidate, Transform cabin) =>
        candidate != null && candidate != cabin && !candidate.IsChildOf(cabin);

    private NavMeshLink CreateLink(Transform parent, Transform landing, Transform door, string linkName)
    {
        GameObject host = new GameObject(linkName);
        host.transform.SetParent(parent, worldPositionStays: false);

        NavMeshLink link = host.AddComponent<NavMeshLink>();
        link.agentTypeID = agentTypeID;
        link.startTransform = landing;
        link.endTransform = door;
        link.bidirectional = true;
        link.width = boardingLinkWidth;
        link.autoUpdate = true;
        link.area = 0;                           // Walkable: stepping into a lift is not a jump
        link.UpdateLink();

        // Off until Refresh decides otherwise. Toggled by ENABLING the component rather than
        // through link.activated: a deactivated link that still tracks its endpoints keeps being
        // removed and re-added for the whole ride, since one of its ends is climbing the shaft.
        link.enabled = false;

        return link;
    }

    /// <summary>
    /// Confirms the two boarding points actually landed on the mesh that was just built.
    ///
    /// A bake that produces nothing is silent — no exception, no warning, just a link that
    /// connects to nothing and a Nemesis that never boards. The three ways it happens are all
    /// worth naming in the message, because none of them is visible from the inspector.
    /// </summary>
    private bool VerifyBoardingPointsAreWalkable()
    {
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agentTypeID,
            areaMask = NavMesh.AllAreas,
        };

        bool bottomOk = NavMesh.SamplePosition(bottomDoor.position, out _, 0.6f, filter);
        bool topOk = NavMesh.SamplePosition(topDoor.position, out _, 0.6f, filter);

        if (bottomOk && topOk) return true;

        Debug.LogError($"[{nameof(ElevatorCabinNavMesh)}] '{name}': the cabin's NavMesh came out " +
                       $"empty at the boarding points (bottom: {(bottomOk ? "ok" : "MISSING")}, " +
                       $"top: {(topOk ? "ok" : "MISSING")}). Usual causes, in order of " +
                       $"likelihood: the cabin's collider is not on the layers this component " +
                       $"bakes (mask {surface.layerMask.value}), there is not {bakeHeadroom}m of " +
                       $"clearance above the cabin floor for the agent to stand in, or the cabin " +
                       $"floor is smaller than Min Region Area ({minRegionArea}). Until it is " +
                       "fixed the Nemesis boards the old way, straight through the barrier.", this);
        return false;
    }

    /// <summary>
    /// Polled, like <see cref="ElevatorLandingBarrier"/> and for the same reason: arriving is only
    /// one of the four ways the cabin's whereabouts change, and it is the only one that raises an
    /// event. The check is two float comparisons and a bool.
    /// </summary>
    private void Update()
    {
        if (isReady) Refresh();
    }

    private void Refresh()
    {
        // Solid ground at both ends of the trip and nothing in between. IsMoving covers the
        // start-delay wait as well as the travel itself, so the floor stops being walkable before
        // the cabin actually sets off rather than a frame after.
        bool parked = !platform.IsMoving;

        if (surface.enabled != parked) surface.enabled = parked;

        SetLinkOpen(bottomLink, parked && elevator.IsCabinAtBottom);
        SetLinkOpen(topLink, parked && !elevator.IsCabinAtBottom);
    }

    private static void SetLinkOpen(NavMeshLink link, bool open)
    {
        if (link != null && link.enabled != open) link.enabled = open;
    }

    private void OnDrawGizmos()
    {
        if (bottomDoor == null || topDoor == null) return;

        // Green while this end is actually joined to the level, grey while it is not: the whole
        // question a designer has when the Nemesis will not board is which of the two is live.
        DrawDoor(bottomDoor, elevator != null && elevator.BottomLanding != null &&
                             IsBoardingOpen(elevator.BottomLanding));
        DrawDoor(topDoor, elevator != null && elevator.TopLanding != null &&
                          IsBoardingOpen(elevator.TopLanding));
    }

    private static void DrawDoor(Transform door, bool open)
    {
        Gizmos.color = open ? new Color(0.3f, 1f, 0.4f) : new Color(0.5f, 0.5f, 0.5f, 0.6f);
        Gizmos.DrawWireSphere(door.position, 0.35f);
    }
}
