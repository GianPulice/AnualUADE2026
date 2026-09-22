using System.Collections.Generic;
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
             "project's radius of 0.3 with room to spare.")]
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

        // The shaft declared it has no NavMesh, so there is nothing to build a cabin floor on.
        if (elevator.NavMeshNotNeeded)
        {
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

        BuildIgnoringTheLevelBakeMarker();
    }

    /// <summary>
    /// Bakes the cabin's floor with the cabin's own "keep me out of the level bake" marker lifted
    /// for the duration.
    ///
    /// THE CABIN HAS TO BE IN EXACTLY ONE NAVMESH, AND IT IS NOT THE LEVEL'S.
    ///
    /// The scene's NavMeshSurface collects the whole scene by layer, from Physics Colliders. The
    /// cabin has a collider, and it sits on a layer that mask includes — so the offline bake
    /// swallows the cabin floor and freezes a copy of it into the LEVEL's static mesh, at whatever
    /// height the lift happened to be parked at when somebody pressed Bake. That ghost never
    /// moves. Two things follow, and both were reported as elevator bugs:
    ///
    ///   - Parked at the baked landing, the ghost and this component's island lie on top of each
    ///     other. Two coincident meshes are two answers to "where is the floor here": a path can
    ///     resolve on either, boarding stops needing the boarding link at all (so the Nemesis
    ///     walks in straight through where the barrier is instead of round to the door), and the
    ///     walk-aboard's own arrival test starts reading the wrong one.
    ///   - Away from it, the ghost is a slab of walkable NavMesh floating over an open shaft.
    ///
    /// The fix belongs in the scene — a <see cref="NavMeshModifier"/> with Ignore From Build on
    /// the cabin, which is the standard way to say "not part of the static level". But that marker
    /// is read by EVERY surface, including this one, so left standing it would empty the very bake
    /// this component exists to produce. Lifting it here is what lets one marker mean "out of the
    /// level's mesh" without also meaning "out of your own".
    ///
    /// Toggling <c>enabled</c> rather than the flag: the package keeps a static list of ACTIVE
    /// modifiers and builds its markups from that, so disabling is what actually takes a marker
    /// out of a build. Restored in a finally, because a marker left off would quietly put the
    /// cabin back into the next bake somebody runs.
    /// </summary>
    private void BuildIgnoringTheLevelBakeMarker()
    {
        List<NavMeshModifier> lifted = null;

        // Ancestors as well as descendants: Apply To Children means a marker on the shaft root
        // covers the cabin just as effectively as one on the cabin itself.
        Lift(platform.GetComponentsInParent<NavMeshModifier>(includeInactive: true), ref lifted);
        Lift(platform.GetComponentsInChildren<NavMeshModifier>(includeInactive: true), ref lifted);

        try
        {
            surface.BuildNavMesh();
        }
        finally
        {
            if (lifted != null)
            {
                for (int i = 0; i < lifted.Count; i++) lifted[i].enabled = true;
            }
        }

        WarnIfTheCabinIsInTheLevelBake(lifted != null);
    }

    /// <summary>Switches off every Ignore From Build marker in a set and records what it switched
    /// off. A marker on the cabin itself turns up in BOTH walks; the second pass skips it because
    /// the first already disabled it, which is what keeps a marker from being listed twice.
    /// </summary>
    private static void Lift(NavMeshModifier[] candidates, ref List<NavMeshModifier> lifted)
    {
        foreach (NavMeshModifier modifier in candidates)
        {
            if (modifier == null || !modifier.enabled || !modifier.ignoreFromBuild) continue;

            lifted ??= new List<NavMeshModifier>();
            lifted.Add(modifier);
            modifier.enabled = false;
        }
    }

    /// <summary>
    /// Says out loud that the cabin is being baked into the level's static NavMesh, because
    /// nothing else ever will.
    ///
    /// It is the quietest failure in this whole system: the bake succeeds, the console stays
    /// clean, the cabin's own island still shows up in Show NavMesh, and what you get is a second
    /// invisible floor that only misbehaves in ways that look like AI bugs. The three facts that
    /// produce it are each innocuous on their own, live in three different inspectors, and are
    /// never seen together — which is exactly the shape of thing worth spending a startup check on.
    ///
    /// Only asked when no marker was found, and only against surfaces other than this one.
    /// </summary>
    private void WarnIfTheCabinIsInTheLevelBake(bool markerFound)
    {
        if (markerFound) return;

        int cabinLayerBit = 1 << platform.gameObject.layer;

        foreach (NavMeshSurface other in NavMeshSurface.activeSurfaces)
        {
            if (other == null || other == surface) continue;
            if ((other.layerMask.value & cabinLayerBit) == 0) continue;

            Debug.LogWarning($"[{nameof(ElevatorCabinNavMesh)}] '{name}': the cabin " +
                             $"('{platform.name}') is on layer " +
                             $"'{LayerMask.LayerToName(platform.gameObject.layer)}', which " +
                             $"'{other.name}' bakes — so the level's static NavMesh contains a " +
                             "frozen copy of this cabin floor, wherever the lift was parked when " +
                             "it was baked. Add a NavMeshModifier with Ignore From Build to the " +
                             "cabin and re-bake; this component knows to look past it when " +
                             "building the cabin's own floor.", this);
            return;
        }
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
        if (!isReady) return;

        Refresh();
        TickBridgeCheck();
    }

    private void Refresh()
    {
        // Solid ground at both ends of the trip and nothing in between. IsMoving covers the
        // start-delay wait as well as the travel itself, so the floor stops being walkable before
        // the cabin actually sets off rather than a frame after.
        bool parked = !platform.IsMoving;

        if (surface.enabled != parked) surface.enabled = parked;

        SetLinkOpen(bottomLink, elevator.BottomLanding, bottomDoor, parked && elevator.IsCabinAtBottom);
        SetLinkOpen(topLink, elevator.TopLanding, topDoor, parked && !elevator.IsCabinAtBottom);
    }

    private void SetLinkOpen(NavMeshLink link, Transform landing, Transform door, bool open)
    {
        if (link == null || link.enabled == open) return;

        link.enabled = open;

        // Every opening is checked, because a link being enabled is not the same as a link being
        // CONNECTED — see TickBridgeCheck.
        if (open) ArmBridgeCheck(link, landing, door);
    }

    // ── Is the link actually bridging? ──────────────────────────────────────
    //
    // ENABLED AND CONNECTED ARE TWO DIFFERENT FACTS, and the gap between them is where boarding
    // was disappearing. A NavMeshLink resolves which NavMesh polygons it joins AT THE MOMENT IT IS
    // ADDED, and it does not go back and look again. The cabin's floor is a NavMesh instance that
    // is removed and re-added constantly — every time this component toggles the surface for a
    // trip, and every time the package notices the cabin's transform has moved — so a link added
    // against the wrong side of one of those swaps is enabled, drawn, reported open by
    // IsBoardingOpen, and joins nothing at all.
    //
    // What that looked like from the outside: the door gizmo green, no error anywhere, and
    // NemesisElevatorUser giving up with "a path was found but it STOPS SHORT of the cabin" —
    // because the path really did stop short. There was no way to tell that from a bad bake.
    //
    // So the link is re-registered until a path query says the two ends are genuinely joined. The
    // retries are frames, not seconds: this only ever runs for a few frames after a cabin parks.

    /// <summary>Frames a freshly opened link is given to come up connected before it is written
    /// off. Generous enough to outlast any ordering between this component, the package's own
    /// transform tracking and the navigation update; short enough to be over before the Nemesis
    /// has finished walking to the landing.</summary>
    private const int BridgeCheckRetries = 5;

    private NavMeshLink pendingLink;
    private Transform pendingLanding;
    private Transform pendingDoor;
    private int pendingRetries;

    /// <summary>Reported once per landing, not once per opening: a shaft that cannot bridge does
    /// not bridge on every trip for the rest of the session.</summary>
    private bool warnedBottomBridge;
    private bool warnedTopBridge;

    private void ArmBridgeCheck(NavMeshLink link, Transform landing, Transform door)
    {
        pendingLink = link;
        pendingLanding = landing;
        pendingDoor = door;
        pendingRetries = BridgeCheckRetries;
    }

    private void TickBridgeCheck()
    {
        if (pendingLink == null) return;

        // Closed again before the check finished — the cabin was called away. Nothing to prove.
        if (!pendingLink.enabled)
        {
            pendingLink = null;
            return;
        }

        if (IsBridging(pendingLanding, pendingDoor))
        {
            pendingLink = null;
            return;
        }

        if (--pendingRetries > 0)
        {
            // Re-registering is the whole repair: RemoveLink + AddLink against the NavMesh as it
            // stands NOW, rather than as it stood on the frame the link happened to be enabled.
            pendingLink.UpdateLink();
            return;
        }

        WarnBridgeFailed(pendingLanding, pendingDoor);
        pendingLink = null;
    }

    /// <summary>
    /// Whether a complete path exists from the landing to the cabin door — the exact question the
    /// boarding walk is about to ask, asked here where the answer can still be acted on.
    ///
    /// A PARTIAL path counts as not bridging, which is the whole point: partial is precisely what
    /// an unconnected island produces, and it is indistinguishable from success in every other
    /// measurement (both ends sample as walkable, the link is enabled, the gizmo is green).
    /// </summary>
    private bool IsBridging(Transform landing, Transform door)
    {
        if (landing == null || door == null) return true;   // Nothing to test; do not spin.

        if (!NavMesh.SamplePosition(landing.position, out NavMeshHit from, 1f, NavMesh.AllAreas)) return false;
        if (!NavMesh.SamplePosition(door.position, out NavMeshHit to, 1f, NavMesh.AllAreas)) return false;

        NavMeshPath path = bridgeProbe ??= new NavMeshPath();

        return NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) &&
               path.status == NavMeshPathStatus.PathComplete;
    }

    /// <summary>Reused rather than allocated per check: this runs from Update.</summary>
    private NavMeshPath bridgeProbe;

    private void WarnBridgeFailed(Transform landing, Transform door)
    {
        bool isBottom = landing == elevator.BottomLanding;

        if (isBottom && warnedBottomBridge) return;
        if (!isBottom && warnedTopBridge) return;

        if (isBottom) warnedBottomBridge = true;
        else          warnedTopBridge = true;

        Debug.LogError($"[{nameof(ElevatorCabinNavMesh)}] '{name}': the boarding link at " +
                       $"'{landing.name}' is enabled but joins NOTHING — after " +
                       $"{BridgeCheckRetries} re-registrations there is still no complete path " +
                       $"from the landing to the cabin door, so the Nemesis will board in a " +
                       $"straight line through the barrier.\n" +
                       $"landing {landing.position} → cabin door {door.position} " +
                       $"({Vector3.Distance(landing.position, door.position):0.00} m apart, " +
                       $"{Mathf.Abs(door.position.y - landing.position.y):0.00} m of that vertical)\n" +
                       "Since re-registering did not help, this is geometry rather than timing. " +
                       "In order of likelihood: the level's NavMesh reaches INTO the shaft under " +
                       "the cabin, so both of the link's ends snap to that same floor and the " +
                       "link becomes a no-op (put an ElevatorLandingBarrier at this landing and " +
                       "re-bake — the shaft footprint must not be walkable in the static mesh); " +
                       "or the cabin's own island does not actually cover the door point.", this);
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
