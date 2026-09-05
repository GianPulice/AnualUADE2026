#if UNITY_EDITOR
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a scene for exercising the Nemesis end to end: states, senses, doors, the freight
/// elevator, safe zones and the spawn-in rules.
///
/// It replaces the old <c>Tools/Scenes/Build Testing Blockout</c>, which laid out the real level's
/// rooms and told you nothing about the AI. This one is shaped by what the Nemesis systems actually
/// need to be observed: a long sight line, a room with exactly one waypoint, a room with none, cover
/// to break line of sight, two doors with opposite policies, and a shaft.
///
/// <b>Half the point is that the layers are right.</b> Two of the bugs this scene exists to catch
/// were layer mistakes that fail silently — a NavMeshModifierVolume the surface never collects, and
/// door geometry outside the bake. So the geometry here is authored on the same layers the real
/// level uses, and the scene deliberately contains ONE broken volume, labelled as such, so the
/// difference between working and broken is visible side by side.
///
/// Menu: Tools / Nemesis / Build Nemesis Test Scene
/// </summary>
public static class NemesisTestSceneBuilder
{
    private const string ScenePath = "Assets/_Project/Scenes/Dev/NemesisTestbed.unity";

    // Matches the real level. Ground + Wall + Props = 6152, and Default stays out so ceilings do
    // not bake as walkable roofs — see docs/CLAUDE.md § Layers.
    private const int LayerDefault = 0;
    private const int LayerGround = 3;
    private const int LayerWall = 11;
    private const int LayerProps = 12;

    // Measured off WIRED_Zona1_Blockout rather than picked, so anything tuned here transfers.
    // In the real level the P1 walls sit at y 1.75 with a height of 3.5, the slabs are 0.2 thick
    // with their surface at 0, and the corridors (PASILLO_PLANTA, PASILLO_TECNICO) are 3-4 wide.
    // A testbed at different proportions quietly tests a different agent: the same NavMeshAgent
    // radius means something else in a 6 m corridor than in a 3 m one.
    private const float WallHeight = 3.5f;
    private const float WallThickness = 0.2f;

    /// <summary>Standard doorway width. Wide enough for the agent (radius 0.5) plus the margin the
    /// NavMesh erodes from every edge, narrow enough to actually break line of sight.</summary>
    private const float DoorWidth = 3f;

    private static Material floorMat, wallMat, safeMat, brokenMat, propMat;

    [MenuItem("Tools/Nemesis/Build Nemesis Test Scene")]
    public static void Build()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildMaterials();
        BuildLighting();

        GameObject root = new GameObject("Testbed");

        // Order matters in one place: the props bring the elevator, and the upper storey is
        // measured off its landings rather than authored, so it cannot be built before them.
        Transform geo = BuildGeometry(root.transform);
        NemesisElevatorLink elevator = BuildProps(root.transform);
        BuildUpperFloor(geo, elevator);

        BuildNavigation(root.transform);
        BuildRoutes(root.transform, elevator);
        BuildSpawnPoints(root.transform, elevator);
        BuildDirector(root.transform, elevator);
        BuildActors(root.transform);

        EnsureFolder("Assets/_Project/Scenes/Dev");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        Debug.Log(
            "[NemesisTestSceneBuilder] Built " + ScenePath + ".\n" +
            "NEXT, BY HAND:\n" +
            "  1. Navigation window > Bake. Nothing here is baked yet.\n" +
            "  2. Confirm the navmesh is ONE connected island across ENTRADA / PASILLO / the west " +
            "loop / PASILLO_CARGA. If it comes out in pieces, a doorway is narrower than the " +
            "agent plus the margin the bake erodes from every edge.\n" +
            "  3. Confirm PLANTA_ALTA is its own island, reachable only through the shaft. That " +
            "separation IS the Traversing test.\n" +
            "  4. Confirm SALA_SEGURA has NO navmesh and SALA_ROTA still has one — that is the " +
            "layer bug from docs/CLAUDE.md § Safe zones, reproduced on purpose.\n" +
            "  5. Run Tools/Nemesis/Validate Navigation Setup: it should report exactly one " +
            "problem, the broken volume.\n" +
            "  6. Play. F9 = debug HUD, F10 = test console (situations, director, and 1-6 to pin " +
            "a state).\n" +
            "WHERE TO WATCH WHAT: Chasing and Searching in the west loop (break line of sight and " +
            "come back round); Investigating in the ALCOVE (pin nothing, use the director's noise " +
            "instead); Traversing by standing upstairs; Patrolling anywhere, with two routes " +
            "covering different ground.");
    }

    // ── Geometry ────────────────────────────────────────────────────────────

    /// <summary>
    /// The ground floor. Every space is here because one specific behaviour cannot be watched
    /// without it — the old version was a spine of three sealed boxes, which is why nothing that
    /// needed the Nemesis to walk anywhere could be seen in it.
    ///
    ///   ENTRADA          — where the player starts. Long sight line north.
    ///   PASILLO          — 30 m of clear line of sight. Vision cone, spawn view test, CHASING.
    ///   PASILLO_OESTE    — runs parallel, joined to PASILLO at two points. THE LOOP: the player
    ///                      can break line of sight and come back around, which is the difference
    ///                      between a chase and a corridor with a dead end at the end of it.
    ///   SALA_LATERAL     — a room off the loop, with columns. SEARCHING has somewhere to sample
    ///                      several candidate points that are not all in a line.
    ///   PASILLO_CARGA    — the junction north. Leads to the shaft and to the alcove.
    ///   SALA_MONTACARGAS — the shaft's own room. TRAVERSING starts here.
    ///   ALCOVE           — a dead end far from every patrol route. Somewhere for a noise (or the
    ///                      Director) to pull it to, where going there is unambiguous. INVESTIGATING.
    ///   SALA_SEGURA      — Not Walkable volume, correctly on Props, and REACHABLE now: it has a
    ///                      doorway, so the Nemesis can genuinely try and fail to enter. Sealed
    ///                      behind four walls the way it used to be, the volume proved nothing.
    ///   SALA_ROTA        — the same volume on Default, which the bake silently ignores.
    /// </summary>
    private static Transform BuildGeometry(Transform parent)
    {
        Transform geo = NewContainer(parent, "Geometry");

        // ── The spine ───────────────────────────────────────────────────────
        BuildRoom(geo, "ENTRADA", new Vector2(0f, 0f), new Vector2(12f, 10f),
                  doorways: new[]
                  {
                      new Doorway(Side.North),
                      new Doorway(Side.East),
                      new Doorway(Side.West),
                  });

        // The corridor's own end walls are left out: ENTRADA's north wall and PASILLO_CARGA's
        // south wall already close it, each with the doorway. Two coplanar walls would z-fight and
        // one of them could seal the other's doorway.
        BuildRoom(geo, "PASILLO", new Vector2(0f, 20f), new Vector2(4f, 30f),
                  omitWalls: Side.North | Side.South,
                  doorways: new[]
                  {
                      new Doorway(Side.West, -6f),
                      new Doorway(Side.West, 6f),
                  });

        BuildRoom(geo, "PASILLO_CARGA", new Vector2(0f, 40f), new Vector2(14f, 10f),
                  doorways: new[]
                  {
                      new Doorway(Side.South),
                      new Doorway(Side.East),
                      new Doorway(Side.North),
                  });

        // ── The loop ────────────────────────────────────────────────────────
        BuildRoom(geo, "PASILLO_OESTE", new Vector2(-8f, 20f), new Vector2(4f, 30f),
                  doorways: new[]
                  {
                      new Doorway(Side.East, -6f),
                      new Doorway(Side.East, 6f),
                      new Doorway(Side.West),
                  });

        BuildRoom(geo, "Link_Loop_S", new Vector2(-4f, 14f), new Vector2(4f, 4f),
                  omitWalls: Side.East | Side.West);
        BuildRoom(geo, "Link_Loop_N", new Vector2(-4f, 26f), new Vector2(4f, 4f),
                  omitWalls: Side.East | Side.West);

        BuildRoom(geo, "SALA_LATERAL", new Vector2(-16f, 20f), new Vector2(12f, 10f),
                  omitWalls: Side.East);

        // ── The shaft, the alcove and the two volumes ───────────────────────
        BuildRoom(geo, "SALA_MONTACARGAS", new Vector2(13f, 40f), new Vector2(12f, 10f),
                  omitWalls: Side.West);

        BuildRoom(geo, "Link_Alcove", new Vector2(0f, 46f), new Vector2(3f, 2f),
                  omitWalls: Side.North | Side.South);
        BuildRoom(geo, "ALCOVE", new Vector2(0f, 50f), new Vector2(8f, 6f),
                  doorways: new[] { new Doorway(Side.South) });

        BuildRoom(geo, "Link_Segura", new Vector2(7.5f, 0f), new Vector2(3f, 4f),
                  omitWalls: Side.East | Side.West);
        BuildRoom(geo, "SALA_SEGURA", new Vector2(14f, 0f), new Vector2(10f, 8f),
                  doorways: new[] { new Doorway(Side.West) }, floorOverride: safeMat);

        BuildRoom(geo, "Link_Rota", new Vector2(-7.5f, 0f), new Vector2(3f, 4f),
                  omitWalls: Side.East | Side.West);
        BuildRoom(geo, "SALA_ROTA", new Vector2(-14f, 0f), new Vector2(10f, 8f),
                  doorways: new[] { new Doorway(Side.East) }, floorOverride: brokenMat);

        BuildCover(geo);
        return geo;
    }

    /// <summary>
    /// Things to hide behind and lose the player around.
    ///
    /// Columns rather than boxes, at the real level's own 0.9 square on an 8 m grid: a chase
    /// through columns is the case where vision flickers on and off frame to frame, which is what
    /// VisionLossGracePeriod exists for and what nothing else in this scene produces.
    /// </summary>
    private static void BuildCover(Transform geo)
    {
        Transform cover = NewContainer(geo, "Cover");

        BuildBox(cover, "Cover_Pasillo", new Vector3(0f, WallHeight * 0.5f, 20f),
                 new Vector3(1.4f, WallHeight, 1.4f), LayerWall, wallMat);

        for (int i = 0; i < 3; i++)
        {
            float z = 16f + i * 4f;
            BuildBox(cover, $"Column_Lateral_{i}", new Vector3(-16f + (i - 1) * 4f, WallHeight * 0.5f, z),
                     new Vector3(0.9f, WallHeight, 0.9f), LayerWall, wallMat);
        }

        // A ceiling on Default, which the surface excludes. It is here so that anyone who "fixes"
        // the layer mask by adding Default immediately sees a walkable roof appear in the bake.
        // Stops short of the upper storey so the two do not read as one slab.
        BuildBox(geo, "Ceiling (Default - excluded from bake ON PURPOSE)",
                 new Vector3(0f, WallHeight, 17f), new Vector3(14f, 0.2f, 44f),
                 LayerDefault, propMat);
    }

    /// <summary>
    /// The second storey, whose only access is the freight elevator. TRAVERSING cannot be observed
    /// without it: the state exists for "the player is a floor up and the lift is the way there",
    /// and with nothing above the ground floor that route verdict is never true. The old scene had
    /// the elevator prefab dropped in a room with no upstairs at all.
    ///
    /// MEASURED OFF THE PREFAB, NOT AUTHORED. The floor height comes from the link's own top
    /// landing and the shaft's footprint from the cabin, so moving the elevator or re-scaling it
    /// re-fits the storey instead of leaving it floating half a metre off. (For reference, the
    /// real level's P2 walls put its second floor at y = 5.0, and this shaft measures 5.07.)
    ///
    /// The floor deliberately stops short of the cabin: what would be over the shaft is the hole
    /// the cabin travels through. The wall between them carries the doorway the Nemesis steps out
    /// through, lined up with the landing.
    /// </summary>
    private static void BuildUpperFloor(Transform geo, NemesisElevatorLink link)
    {
        if (link == null || link.TopLanding == null || link.Platform == null)
        {
            Debug.LogWarning("[NemesisTestSceneBuilder] No usable elevator, so no upper storey was " +
                             "built. Traversing cannot be tested in this scene.");
            return;
        }

        Vector3 landing = link.TopLanding.position;
        Vector3 cabin = link.Platform.transform.position;
        float y = landing.y;

        // Which way the landing lies from the cabin, snapped to an axis so the boxes stay
        // axis-aligned. In this prefab it is +Z.
        Vector3 away = landing - cabin;
        away.y = 0f;
        bool alongZ = Mathf.Abs(away.z) >= Mathf.Abs(away.x);
        float sign = alongZ ? Mathf.Sign(away.z) : Mathf.Sign(away.x);

        // The near edge sits between the cabin and the landing: past the cabin's extent so the
        // shaft stays open, short of the landing so the landing is on the floor.
        float cabinEdge = (alongZ ? cabin.z : cabin.x) + sign * 2.6f;
        float landingLine = alongZ ? landing.z : landing.x;
        float nearEdge = Mathf.Lerp(cabinEdge, landingLine, 0.4f);

        const float depth = 12f;
        float centreLine = nearEdge + sign * depth * 0.5f;

        Vector2 centre = alongZ
            ? new Vector2(landing.x, centreLine)
            : new Vector2(centreLine, landing.z);

        Side shaftSide = alongZ
            ? (sign > 0f ? Side.South : Side.North)
            : (sign > 0f ? Side.West : Side.East);

        BuildRoom(geo, "PLANTA_ALTA", centre, new Vector2(12f, depth),
                  doorways: new[]
                  {
                      // Onto the cabin. Wider than a door: the Nemesis walks out of a lift, not
                      // through a doorframe, and a tight opening here is where boarding fails.
                      new Doorway(shaftSide, 0f, 4f),
                      new Doorway(Side.West, 0f, DoorWidth),
                  },
                  floorTopY: y);

        // Somewhere to go once upstairs. A landing with nothing beyond it is a lift ride to a
        // cupboard, and the patrol upstairs needs room for more than one waypoint.
        BuildRoom(geo, "PASARELA_ALTA", new Vector2(centre.x - 13f, centre.y),
                  new Vector2(14f, depth), omitWalls: Side.East, floorTopY: y);
    }

    /// <summary>Which wall of a room a doorway or an omission refers to.</summary>
    [System.Flags]
    private enum Side
    {
        None = 0,
        North = 1,
        South = 2,
        East = 4,
        West = 8,
    }

    /// <summary>
    /// A hole in one wall. <paramref name="offset"/> runs along that wall from its centre, so a
    /// doorway can be put where the room next door actually is instead of only in the middle.
    /// </summary>
    private readonly struct Doorway
    {
        public readonly Side Side;
        public readonly float Offset;
        public readonly float Width;

        public Doorway(Side side, float offset = 0f, float width = 3f)
        {
            Side = side;
            Offset = offset;
            Width = width;
        }
    }

    /// <summary>
    /// A room with a floor and up to four walls, any of which can carry doorways or be left out.
    ///
    /// THE DOORWAYS ARE THE WHOLE POINT AND THEY USED TO BE MISSING. This method built four solid
    /// walls, always — so the rooms in this scene were sealed boxes, the bake came out as several
    /// disconnected islands, and the two doors were sitting inside masonry. Nothing that needs the
    /// Nemesis to WALK somewhere could ever be observed here: no patrol between zones, no chase
    /// down the corridor, no route across floors. What the scene actually tested was the senses and
    /// the spawn rules, which is why it read as a straight line with a box in it.
    ///
    /// Omitting a wall is the other half. Where two rooms share a plane, only one of them builds
    /// the wall; the other leaves it out. Two coplanar walls would z-fight, double the colliders,
    /// and — worse — one of them could quietly seal the other's doorway.
    /// </summary>
    private static void BuildRoom(Transform parent, string name, Vector2 center, Vector2 size,
                                  Side omitWalls = Side.None, Doorway[] doorways = null,
                                  Material floorOverride = null, float floorTopY = 0f)
    {
        Transform room = NewContainer(parent, name);
        room.position = new Vector3(center.x, floorTopY, center.y);

        // The slab hangs BELOW the given height, so floorTopY is the surface you stand on rather
        // than the centre of a box. Every landing, waypoint and spawn in this scene is authored
        // against the walking surface, and having to subtract half a slab thickness by hand at
        // each of them is how one of them ends up embedded in the floor.
        BuildBox(room, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(size.x, 0.2f, size.y),
                 LayerGround, floorOverride != null ? floorOverride : floorMat);

        float hx = size.x * 0.5f;
        float hz = size.y * 0.5f;

        BuildWall(room, "Wall_N", Side.North, omitWalls, doorways, size.x,
                  new Vector3(0f, 0f, hz), true);
        BuildWall(room, "Wall_S", Side.South, omitWalls, doorways, size.x,
                  new Vector3(0f, 0f, -hz), true);
        BuildWall(room, "Wall_E", Side.East, omitWalls, doorways, size.y,
                  new Vector3(hx, 0f, 0f), false);
        BuildWall(room, "Wall_W", Side.West, omitWalls, doorways, size.y,
                  new Vector3(-hx, 0f, 0f), false);
    }

    /// <summary>
    /// One wall, cut into segments by whatever doorways land on it.
    ///
    /// Written against a span along one axis rather than as four special cases, so a doorway
    /// behaves identically wherever it is put — the north and the east wall differ only in which
    /// component the span maps to.
    /// </summary>
    private static void BuildWall(Transform room, string name, Side side, Side omitWalls,
                                  Doorway[] doorways, float length, Vector3 offset, bool alongX)
    {
        if ((omitWalls & side) != 0) return;

        float half = length * 0.5f;

        // Segment boundaries along the wall, walking from one end to the other. Starts as the
        // whole wall and gets cut by each doorway in turn.
        var cuts = new System.Collections.Generic.List<Vector2>();
        cuts.Add(new Vector2(-half, half));

        if (doorways != null)
        {
            foreach (Doorway door in doorways)
            {
                if (door.Side != side) continue;

                float from = door.Offset - door.Width * 0.5f;
                float to = door.Offset + door.Width * 0.5f;

                for (int i = cuts.Count - 1; i >= 0; i--)
                {
                    Vector2 segment = cuts[i];
                    if (to <= segment.x || from >= segment.y) continue;   // doorway misses it

                    cuts.RemoveAt(i);

                    if (from > segment.x) cuts.Insert(i, new Vector2(segment.x, from));
                    if (to < segment.y) cuts.Insert(i, new Vector2(to, segment.y));
                }
            }
        }

        int index = 0;
        foreach (Vector2 segment in cuts)
        {
            float span = segment.y - segment.x;
            if (span < 0.05f) continue;

            float centre = (segment.x + segment.y) * 0.5f;

            Vector3 position = offset + new Vector3(alongX ? centre : 0f,
                                                    WallHeight * 0.5f,
                                                    alongX ? 0f : centre);
            Vector3 scale = alongX
                ? new Vector3(span, WallHeight, WallThickness)
                : new Vector3(WallThickness, WallHeight, span);

            BuildBox(room, cuts.Count > 1 ? $"{name}_{index++}" : name, position, scale,
                     LayerWall, wallMat);
        }
    }

    private static GameObject BuildBox(Transform parent, string name, Vector3 localPos,
                                       Vector3 size, int layer, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.layer = layer;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPos;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material;

        return box;
    }

    // ── Navigation ──────────────────────────────────────────────────────────

    private static void BuildNavigation(Transform parent)
    {
        Transform nav = NewContainer(parent, "Navigation");

        GameObject surfaceGo = new GameObject("NavMesh Surface");
        surfaceGo.transform.SetParent(nav, false);

        NavMeshSurface surface = surfaceGo.AddComponent<NavMeshSurface>();
        surface.agentTypeID = 0;
        surface.collectObjects = CollectObjects.All;

        // Physics Colliders, like the real level: an invisible blocker then affects the bake, and a
        // renderer with no collider does not.
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = (1 << LayerGround) | (1 << LayerWall) | (1 << LayerProps);

        BuildSafeVolume(nav, "SafeVolume (CORRECT - on Props)",
                        new Vector3(14f, 1.75f, 0f), LayerProps);

        // The same volume, wrong layer. It looks identical in the inspector and the bake throws it
        // away without a word — see NemesisSetupValidator.ValidateModifierVolumes.
        BuildSafeVolume(nav, "SafeVolume (BROKEN - on Default, bake ignores it)",
                        new Vector3(-14f, 1.75f, 0f), LayerDefault);
    }

    private static void BuildSafeVolume(Transform parent, string name, Vector3 position, int layer)
    {
        GameObject go = new GameObject(name);
        go.layer = layer;
        go.transform.SetParent(parent, false);
        go.transform.position = position;

        NavMeshModifierVolume volume = go.AddComponent<NavMeshModifierVolume>();
        volume.size = new Vector3(9f, 3.5f, 7f);
        volume.center = Vector3.zero;
        volume.area = 1;   // Not Walkable
    }

    // ── Routes and spawn points ─────────────────────────────────────────────

    /// <summary>
    /// Two routes so the route roll has something to choose between, and OneNode gets a single
    /// waypoint on purpose: a room marked with one node is what WaypointSatellites exists to turn
    /// into a room the Nemesis actually prowls.
    /// </summary>
    private static void BuildRoutes(Transform parent, NemesisElevatorLink elevator)
    {
        Transform routes = NewContainer(parent, "Routes");

        // The spine. Long legs, so a patrol leg is long enough to be interrupted by a sighting
        // halfway down it.
        BuildRoute(routes, "Route_Spine", new[]
        {
            new Vector3(0f, 0f, -2f),
            new Vector3(0f, 0f, 12f),
            new Vector3(0f, 0f, 28f),
            new Vector3(0f, 0f, 40f),
        });

        // The loop, so the route roll has a genuine alternative that covers different ground. Two
        // routes down the same corridor are one route as far as the player can tell.
        BuildRoute(routes, "Route_Oeste", new[]
        {
            new Vector3(-8f, 0f, 12f),
            new Vector3(-16f, 0f, 20f),
            new Vector3(-8f, 0f, 28f),
        });

        // One waypoint on purpose: the case WaypointSatellites exists to turn into a room the
        // Nemesis actually prowls rather than a single spot it stands on.
        BuildRoute(routes, "Route_Alcove", new[]
        {
            new Vector3(0f, 0f, 50f),
        });

        // THE POINT OF THE UPPER STOREY. Without a reason to be up there the Nemesis never has a
        // belief across floors, the route verdict never crosses the lift, and Traversing is
        // unreachable no matter how well the elevator works.
        if (elevator != null && elevator.TopLanding != null)
        {
            Vector3 top = elevator.TopLanding.position;

            BuildRoute(routes, "Route_Alta", new[]
            {
                new Vector3(top.x, top.y, top.z + 3f),
                new Vector3(top.x - 10f, top.y, top.z + 5f),
            });
        }
    }

    private static void BuildRoute(Transform parent, string name, Vector3[] points)
    {
        GameObject routeGo = new GameObject(name);
        routeGo.transform.SetParent(parent, false);
        routeGo.AddComponent<NemesisRoute>();

        for (int i = 0; i < points.Length; i++)
        {
            GameObject waypoint = new GameObject($"WP_{i:00}");
            waypoint.tag = NemesisRoute.WaypointTag;
            waypoint.transform.SetParent(routeGo.transform, false);
            waypoint.transform.position = points[i];
        }
    }

    /// <summary>
    /// Three points, far apart and behind cover, because the spawn-in refuses anything that is
    /// close, in the player's view cone, or in the open. Placed so that at least one of them is
    /// always usable from the Start room, which is where the player begins.
    /// </summary>
    private static void BuildSpawnPoints(Transform parent, NemesisElevatorLink elevator)
    {
        Transform spawns = NewContainer(parent, "SpawnPoints");

        NewMarker(spawns, "Spawn_Carga", new Vector3(0f, 0f, 41f));
        NewMarker(spawns, "Spawn_Lateral", new Vector3(-16f, 0f, 20f));
        NewMarker(spawns, "Spawn_Alcove", new Vector3(0f, 0f, 50f));

        // Upstairs, which is the one spawn that forces the Nemesis to come DOWN by lift to reach
        // the player — the same crossing as Traversing, in the other direction.
        if (elevator != null && elevator.TopLanding != null)
        {
            Vector3 top = elevator.TopLanding.position;
            NewMarker(spawns, "Spawn_Alta", new Vector3(top.x - 8f, top.y, top.z + 5f));
        }
    }

    // ── Actors and props ────────────────────────────────────────────────────

    private static void BuildActors(Transform parent)
    {
        Transform actors = NewContainer(parent, "Actors");

        InstantiatePrefab("Assets/_Project/Prefabs/Player.prefab", actors, new Vector3(0f, 1f, -2f));

        // At the far end of the spine: far enough that the first thing you see it do is walk, and
        // out of the doorway so it does not start the scene wedged in one.
        GameObject nemesis = InstantiatePrefab("Assets/_Project/Prefabs/Nemesis.prefab", actors,
                                               new Vector3(0f, 1f, 41f));
        if (nemesis != null) nemesis.AddComponent<NemesisTestConsole>();

        BuildCamera(actors);
    }

    private static NemesisElevatorLink BuildProps(Transform parent)
    {
        Transform props = NewContainer(parent, "Props");

        // In the doorways now, rather than embedded in solid wall the way they were before this
        // scene had any. One door the Nemesis may open, one it may not — the second is the case
        // that needs the carving obstacle DoorInteractable adds on Awake to genuinely stop it.
        InstantiatePrefab("Assets/_Project/Prefabs/DoorWood.prefab", props, new Vector3(0f, 0f, 5f));
        InstantiatePrefab("Assets/_Project/Prefabs/DoorWood.prefab", props, new Vector3(0f, 0f, 35f));

        // Placed so the cabin and both landings land inside SALA_MONTACARGAS: the prefab's bottom
        // landing sits ~3.1 m north of its root, and the cabin straddles the root itself.
        GameObject elevator = InstantiatePrefab("Assets/_Project/Prefabs/MontacargasRoot.prefab",
                                                props, new Vector3(13f, 0f, 38f));

        InstantiatePrefab("Assets/_Project/Prefabs/Environment/Locker.prefab", props, new Vector3(-4f, 0f, 2f));

        // A prop excluded from the bake by ignoreFromBuild rather than by layer, so both ways of
        // keeping geometry out of the NavMesh are visible in one scene. See docs/CLAUDE.md.
        GameObject crate = BuildBox(props, "Crate (ignoreFromBuild)",
                                    new Vector3(-15f, 0.5f, 22f), Vector3.one, LayerProps, propMat);
        NavMeshModifier modifier = crate.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = true;
        modifier.overrideArea = false;

        // GetComponentInChildren and not GetComponent: in this project's prefab the link lives on
        // the CABIN rather than on the root it is documented to live on, so asking the root alone
        // comes back empty — and the upper storey then silently never gets built.
        return elevator != null ? elevator.GetComponentInChildren<NemesisElevatorLink>(true) : null;
    }

    // ── Director ────────────────────────────────────────────────────────────

    /// <summary>
    /// The Director plus one pressure zone per space worth leaning on.
    ///
    /// The zones are the argument for having built the level this way: pressure is only legible
    /// when the Nemesis had somewhere ELSE it could plausibly have been. On the old spine there was
    /// one path, so a director pulling it north was indistinguishable from it walking north.
    /// </summary>
    private static void BuildDirector(Transform parent, NemesisElevatorLink elevator)
    {
        Transform director = NewContainer(parent, "Director");
        director.gameObject.AddComponent<NemesisDirector>();

        BuildPressureZone(director, "entrada", new Vector3(0f, 0f, 0f), 8f);
        BuildPressureZone(director, "sala lateral", new Vector3(-16f, 0f, 20f), 10f);
        BuildPressureZone(director, "carga", new Vector3(0f, 0f, 40f), 9f);

        // Upstairs, so a pressure request can be answered only by taking the lift — the shortest
        // route to watching route weights, the anchor and Traversing interact.
        if (elevator != null && elevator.TopLanding != null)
        {
            Vector3 top = elevator.TopLanding.position;
            BuildPressureZone(director, "planta alta", new Vector3(top.x - 4f, top.y, top.z + 4f), 10f);
        }
    }

    private static void BuildPressureZone(Transform parent, string id, Vector3 position, float radius)
    {
        GameObject go = new GameObject($"Zone_{id}");
        go.transform.SetParent(parent, false);
        go.transform.position = position;

        NemesisPressureZone zone = go.AddComponent<NemesisPressureZone>();

        // Through SerializedObject because both fields are private, which is correct — a zone's id
        // and radius are authored, not set by whoever happens to hold a reference.
        SerializedObject so = new SerializedObject(zone);
        so.FindProperty("zoneId").stringValue = id;
        so.FindProperty("radius").floatValue = radius;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ── Scaffolding ─────────────────────────────────────────────────────────

    private static GameObject InstantiatePrefab(string path, Transform parent, Vector3 position)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            Debug.LogWarning($"[NemesisTestSceneBuilder] Prefab not found: {path}. The scene is " +
                             "built without it — place one by hand if you need it.");
            return null;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        instance.transform.position = position;
        return instance;
    }

    private static void BuildCamera(Transform parent)
    {
        // Only a fallback: the Player prefab brings its own rig. This keeps the scene viewable if
        // the prefab failed to load, rather than opening to a black game view.
        // FindAnyObjectByType and not FindFirstObjectByType: the latter is deprecated because it
        // orders by instance ID, and that ordering buys nothing here — the question is only
        // whether a camera exists at all. Same choice as NemesisChaseMusic.
        if (Object.FindAnyObjectByType<Camera>() != null) return;

        GameObject cam = new GameObject("Fallback Camera", typeof(Camera), typeof(AudioListener));
        cam.transform.SetParent(parent, false);
        cam.transform.position = new Vector3(0f, 12f, -10f);
        cam.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
    }

    private static void BuildLighting()
    {
        GameObject sun = new GameObject("Directional Light");
        Light light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private static void BuildMaterials()
    {
        EnsureFolder("Assets/_Project/Scenes/Dev/NemesisTestbedMaterials");
        const string dir = "Assets/_Project/Scenes/Dev/NemesisTestbedMaterials";

        floorMat = SaveMaterial(new Color(0.30f, 0.30f, 0.32f), $"{dir}/testbed_floor.mat");
        wallMat = SaveMaterial(new Color(0.22f, 0.22f, 0.24f), $"{dir}/testbed_wall.mat");
        safeMat = SaveMaterial(new Color(0.15f, 0.35f, 0.18f), $"{dir}/testbed_safe.mat");
        brokenMat = SaveMaterial(new Color(0.45f, 0.15f, 0.15f), $"{dir}/testbed_broken.mat");
        propMat = SaveMaterial(new Color(0.35f, 0.28f, 0.20f), $"{dir}/testbed_prop.mat");
    }

    private static Material SaveMaterial(Color color, string path)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader) { color = color };

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Transform NewContainer(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static void NewMarker(Transform parent, string name, Vector3 position)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        int split = path.LastIndexOf('/');
        string parent = path.Substring(0, split);
        string leaf = path.Substring(split + 1);

        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
#endif
