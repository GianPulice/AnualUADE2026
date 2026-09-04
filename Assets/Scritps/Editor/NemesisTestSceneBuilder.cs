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
    private const string ScenePath = "Assets/Scenes/TestScenes/NemesisTestbed.unity";

    // Matches the real level. Ground + Wall + Props = 6152, and Default stays out so ceilings do
    // not bake as walkable roofs — see docs/CLAUDE.md § Layers.
    private const int LayerDefault = 0;
    private const int LayerGround = 3;
    private const int LayerWall = 11;
    private const int LayerProps = 12;

    private const float WallHeight = 4f;
    private const float WallThickness = 0.3f;

    private static Material floorMat, wallMat, safeMat, brokenMat, propMat;

    [MenuItem("Tools/Nemesis/Build Nemesis Test Scene")]
    public static void Build()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildMaterials();
        BuildLighting();

        GameObject root = new GameObject("Testbed");

        BuildGeometry(root.transform);
        BuildNavigation(root.transform);
        BuildRoutes(root.transform);
        BuildSpawnPoints(root.transform);
        BuildActors(root.transform);
        BuildProps(root.transform);

        EnsureFolder("Assets/Scenes/TestScenes");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);

        Debug.Log(
            "[NemesisTestSceneBuilder] Built " + ScenePath + ".\n" +
            "NEXT, BY HAND:\n" +
            "  1. Navigation window > Bake. Nothing here is baked yet.\n" +
            "  2. Confirm the Safe Room has NO navmesh and the 'BROKEN' room still has one — that " +
            "is the layer bug from docs/CLAUDE.md § Safe zones, reproduced on purpose.\n" +
            "  3. Run Tools/Nemesis/Validate Navigation Setup: it should report exactly one " +
            "problem, the broken volume.\n" +
            "  4. Press Play and F9 for the Nemesis debug HUD.");
    }

    // ── Geometry ────────────────────────────────────────────────────────────

    /// <summary>
    /// Five spaces, each there to exercise something specific:
    ///   Start      — where the player begins, with the long sight line down the corridor.
    ///   Corridor   — 30m of clear line of sight, for the vision cone and the spawn view test.
    ///   OneNode    — a room with a single waypoint, for cluster satellites and free roam.
    ///   SafeRoom   — Not Walkable volume, correctly on Props.
    ///   BrokenRoom — the same volume on Default, which the bake silently ignores.
    /// </summary>
    private static void BuildGeometry(Transform parent)
    {
        Transform geo = NewContainer(parent, "Geometry");

        BuildRoom(geo, "Start", new Vector2(0f, 0f), new Vector2(12f, 12f));
        BuildRoom(geo, "Corridor", new Vector2(0f, 21f), new Vector2(4f, 30f));
        BuildRoom(geo, "OneNode", new Vector2(0f, 42f), new Vector2(14f, 12f));
        BuildRoom(geo, "SafeRoom", new Vector2(16f, 0f), new Vector2(10f, 10f), safeMat);
        BuildRoom(geo, "BrokenRoom", new Vector2(-16f, 0f), new Vector2(10f, 10f), brokenMat);

        // Cover in the middle of the corridor: without something to break line of sight there is
        // no way to watch the Nemesis lose the player, and no occluded spawn point either.
        BuildBox(geo, "Cover_Corridor", new Vector3(0f, WallHeight * 0.5f, 21f),
                 new Vector3(2f, WallHeight, 2f), LayerWall, wallMat);

        // A ceiling on Default, which the surface excludes. It is here so that anyone who "fixes"
        // the layer mask by adding Default immediately sees a walkable roof appear in the bake.
        BuildBox(geo, "Ceiling (Default - excluded from bake ON PURPOSE)",
                 new Vector3(0f, WallHeight, 21f), new Vector3(14f, 0.3f, 60f),
                 LayerDefault, propMat);
    }

    private static void BuildRoom(Transform parent, string name, Vector2 center, Vector2 size,
                                  Material floorOverride = null)
    {
        Transform room = NewContainer(parent, name);
        room.position = new Vector3(center.x, 0f, center.y);

        BuildBox(room, "Floor", Vector3.zero, new Vector3(size.x, 0.2f, size.y), LayerGround,
                 floorOverride != null ? floorOverride : floorMat);

        float hx = size.x * 0.5f;
        float hz = size.y * 0.5f;

        BuildBox(room, "Wall_N", new Vector3(0f, WallHeight * 0.5f, hz),
                 new Vector3(size.x, WallHeight, WallThickness), LayerWall, wallMat);
        BuildBox(room, "Wall_S", new Vector3(0f, WallHeight * 0.5f, -hz),
                 new Vector3(size.x, WallHeight, WallThickness), LayerWall, wallMat);
        BuildBox(room, "Wall_E", new Vector3(hx, WallHeight * 0.5f, 0f),
                 new Vector3(WallThickness, WallHeight, size.y), LayerWall, wallMat);
        BuildBox(room, "Wall_W", new Vector3(-hx, WallHeight * 0.5f, 0f),
                 new Vector3(WallThickness, WallHeight, size.y), LayerWall, wallMat);
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
                        new Vector3(16f, 2f, 0f), LayerProps);

        // The same volume, wrong layer. It looks identical in the inspector and the bake throws it
        // away without a word — see NemesisSetupValidator.ValidateModifierVolumes.
        BuildSafeVolume(nav, "SafeVolume (BROKEN - on Default, bake ignores it)",
                        new Vector3(-16f, 2f, 0f), LayerDefault);
    }

    private static void BuildSafeVolume(Transform parent, string name, Vector3 position, int layer)
    {
        GameObject go = new GameObject(name);
        go.layer = layer;
        go.transform.SetParent(parent, false);
        go.transform.position = position;

        NavMeshModifierVolume volume = go.AddComponent<NavMeshModifierVolume>();
        volume.size = new Vector3(9f, 4f, 9f);
        volume.center = Vector3.zero;
        volume.area = 1;   // Not Walkable
    }

    // ── Routes and spawn points ─────────────────────────────────────────────

    /// <summary>
    /// Two routes so the route roll has something to choose between, and OneNode gets a single
    /// waypoint on purpose: a room marked with one node is what WaypointSatellites exists to turn
    /// into a room the Nemesis actually prowls.
    /// </summary>
    private static void BuildRoutes(Transform parent)
    {
        Transform routes = NewContainer(parent, "Routes");

        BuildRoute(routes, "Route_Main", new[]
        {
            new Vector3(0f, 0f, -3f),
            new Vector3(0f, 0f, 10f),
            new Vector3(0f, 0f, 30f),
        });

        BuildRoute(routes, "Route_Far", new[]
        {
            new Vector3(0f, 0f, 42f),
        });
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
    private static void BuildSpawnPoints(Transform parent)
    {
        Transform spawns = NewContainer(parent, "SpawnPoints");

        NewMarker(spawns, "Spawn_Far", new Vector3(0f, 0f, 44f));
        NewMarker(spawns, "Spawn_BehindCover", new Vector3(0f, 0f, 26f));
        NewMarker(spawns, "Spawn_Side", new Vector3(-5f, 0f, 40f));
    }

    // ── Actors and props ────────────────────────────────────────────────────

    private static void BuildActors(Transform parent)
    {
        Transform actors = NewContainer(parent, "Actors");

        InstantiatePrefab("Assets/Prefabs/Player.prefab", actors, new Vector3(0f, 1f, -4f));

        GameObject nemesis = InstantiatePrefab("Assets/Prefabs/Nemesis.prefab", actors,
                                               new Vector3(0f, 1f, 44f));
        if (nemesis != null) nemesis.AddComponent<NemesisTestConsole>();

        BuildCamera(actors);
    }

    private static void BuildProps(Transform parent)
    {
        Transform props = NewContainer(parent, "Props");

        // One door the Nemesis may open, one it may not. The second is the case that needs the
        // carving obstacle DoorInteractable adds on Awake to genuinely stop it.
        InstantiatePrefab("Assets/Prefabs/DoorWood.prefab", props, new Vector3(0f, 0f, 6f));
        InstantiatePrefab("Assets/Prefabs/DoorWood.prefab", props, new Vector3(0f, 0f, 36f));

        InstantiatePrefab("Assets/Prefabs/MontacargasRoot.prefab", props, new Vector3(6f, 0f, 42f));
        InstantiatePrefab("Assets/Prefabs/Environment/Locker.prefab", props, new Vector3(-5f, 0f, 3f));

        // A prop excluded from the bake by ignoreFromBuild rather than by layer, so both ways of
        // keeping geometry out of the NavMesh are visible in one scene. See docs/CLAUDE.md.
        GameObject crate = BuildBox(props, "Crate (ignoreFromBuild)",
                                    new Vector3(4f, 0.5f, 8f), Vector3.one, LayerProps, propMat);
        NavMeshModifier modifier = crate.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = true;
        modifier.overrideArea = false;
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
        if (Object.FindFirstObjectByType<Camera>() != null) return;

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
        EnsureFolder("Assets/Scenes/TestScenes/NemesisTestbedMaterials");
        const string dir = "Assets/Scenes/TestScenes/NemesisTestbedMaterials";

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
