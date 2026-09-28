using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the "Drop Lab" inside NemesisTestbed: the mezzanines with a Hop and a Hang drop that plan
/// §15.6 step 5 asks for, so cases 12–16 and 55 can be played without loading a level (Fase 8).
///
/// WHAT IS IN IT. A hall south of ENTRADA, reached through a 3 m doorway in ENTRADA's south wall:
///   HANG DECK  (west)  3.6 m up. NemesisDropLink "Drop_Hang" on its north edge.
///   HOP DECK   (east)  2.0 m up. NemesisDropLink "Drop_Hop" on its north edge.
///   Each deck has its own ramp against the side wall: the way back up the validator demands, and
///   the way the Nemesis goes down on patrol, when a drop costs more than the walk (D11).
///   Route_DropLab visits the hall and both decks, and is added to the testbed Nemesis's routes.
///
/// LAYOUT (world metres, north = +z):
///
///            x -9            -2   2             9
///   -5   ENTRADA ======== doorway (x -1.5..1.5) ========
///   -9   +-------------------- hall -------------------+
///        |ramp                                         |
///        |up to                                        |
///        |3.6 m                                  ramp  |
///        |                                      up to  |
///        |       Drop_Hang           Drop_Hop   2.0 m  |
///  -22   |===== HANG DECK =====|   |===== HOP DECK ====|
///        |       3.6 m          |   |       2.0 m      |
///  -29   +---------------------------------------------+
///
/// WHAT KEEPS THE PLAYER OFF THE DROPS (D9): invisible rails on the Ignore Raycast layer along every
/// open edge of the decks and the ramps. The player's capsule collides with every layer; the bake
/// (Ground | Wall | Props), the Nemesis's sight and the validator's arc and landing checks (Default |
/// Ground | Wall | Props) do not see that one, and the Nemesis's body never touches physics. So the
/// Nemesis drops straight through a rail the player cannot cross.
///
/// WHAT IT TOUCHES OUTSIDE ITS OWN ROOT, all of it on purpose:
///   - Testbed/Geometry/ENTRADA/Wall_S is DEACTIVATED, never deleted; two segments leaving the
///     doorway replace it under Drop Lab. To take the lab out: delete "Drop Lab", re-enable Wall_S,
///     drop the missing entry from the Nemesis's routes and bake again (either builder bakes).
///   - The testbed Nemesis's NemesisController.routes gets Route_DropLab appended.
///   - The testbed's NavMeshSurface is baked again (TestbedNavMeshBake: this scene alone).
///
/// Same rules as NemesisTestbedBugLabBuilder, whose helpers this follows: it never changes the active
/// scene or saves any scene other than NemesisTestbed, and re-running it rebuilds the lab from scratch.
///
/// Also runnable without the editor open:
///   Unity.exe -batchmode -projectPath &lt;repo&gt; -executeMethod NemesisTestbedDropLabBuilder.BuildFromCommandLine
/// which also runs Tools/Nemesis/Validate Navigation Setup on the result and exits with 1 on a problem.
/// </summary>
public static class NemesisTestbedDropLabBuilder
{
    private const string LogTag = "[NemesisDropLab]";
    private const string MenuPath = "Tools/Nemesis/Build Drop Lab (NemesisTestbed)";
    private const string ValidatorMenuPath = "Tools/Nemesis/Validate Navigation Setup";
    private const string ScenePath = "Assets/_Project/Scenes/Dev/NemesisTestbed.unity";

    private const string RootName = "Drop Lab";
    private const string RouteName = "Route_DropLab";
    private const string TestbedRootName = "Testbed";
    private const string EntranceWallPath = "Geometry/ENTRADA/Wall_S";

    private const string TestbedMaterials = "Assets/_Project/Scenes/Dev/NemesisTestbedMaterials";
    private const string FloorMaterialPath = TestbedMaterials + "/testbed_floor.mat";
    private const string WallMaterialPath = TestbedMaterials + "/testbed_wall.mat";
    private const string PropMaterialPath = TestbedMaterials + "/testbed_prop.mat";
    private const string AccentMaterialPath = "Assets/_Project/Scenes/Dev/Materials/Blockout_Accent.mat";

    // ── Dimensions ──────────────────────────────────────────────────────────

    private const float WallThickness = 0.2f;
    private const float FloorThickness = 0.2f;

    /// <summary>The testbed's own rooms and corridors: 3.5 m.</summary>
    private const float CorridorWallHeight = 3.5f;

    /// <summary>Tall enough that a player standing on the high deck (3.6 + 1.86 m) is still inside
    /// the room.</summary>
    private const float HallWallHeight = 6.5f;

    // ENTRADA's south wall is at z -5; a 3 m doorway in its middle, like the Bug Lab's.
    private const float EntranceZ = -5f;
    private const float DoorwayHalfWidth = 1.5f;

    private const float HallWestX = -9f;
    private const float HallEastX = 9f;
    private const float HallNorthZ = -9f;
    private const float HallSouthZ = -29f;

    /// <summary>The decks' north face, where both drops are.</summary>
    private const float DeckNorthZ = -22f;

    private const float HangDeckEastX = -2f;
    private const float HopDeckWestX = 2f;

    /// <summary>Above FloorHeightThreshold (2.5 m): a Hang. Under MaxHeight (5 m).</summary>
    private const float HangDeckTop = 3.6f;

    /// <summary>Between MinHeight (1.5 m) and FloorHeightThreshold (2.5 m): a Hop.</summary>
    private const float HopDeckTop = 2f;

    /// <summary>Top of the Not Walkable volume that fills a deck: clear of its walkable top by 0.4 m,
    /// so the top keeps its NavMesh. Without it the voxeliser finds a room inside the box and bakes
    /// an island on the floor in there (the Bug Lab's balcony did).</summary>
    private const float DeckInsideClearance = 0.4f;

    // Ramps, 2 m wide against the side walls: 0.6 m of agent plus erosion on the open side, and
    // room for the player. Slopes of 16.7° and 15.9°, under the ~19.5° at which the player's own
    // obstacle sweep starts reading a ramp as a wall (see the Bug Lab's balcony ramp).
    private const float RampWidth = 2f;
    private const float HangRampFootZ = -10f;
    private const float HopRampFootZ = -15f;

    // ── Drops ───────────────────────────────────────────────────────────────

    /// <summary>TopEdge this far back from the edge: the plan asks for 0.3–0.5 m, and the NavMesh is
    /// eroded one agent radius (0.3 m) from any drop-off, so the far end of that range is the one
    /// that is certainly on it.</summary>
    private const float TopEdgeSetback = 0.5f;

    /// <summary>BottomLanding this far out from below the edge: the plan asks for 0.8–1.5 m.</summary>
    private const float LandingReach = 1.2f;

    private const float HangDropX = -4.5f;
    private const float HopDropX = 4.5f;

    // ── Rails ───────────────────────────────────────────────────────────────

    private const float RailHeight = 1.1f;
    private const float RailThickness = 0.1f;

    // ── Route ───────────────────────────────────────────────────────────────

    private static readonly string[] WaypointNames =
    {
        "WP_00 hall, by the doorway",
        "WP_01 hang deck (3.6 m)",
        "WP_02 hall, between the landings",
        "WP_03 hop deck (2.0 m)",
    };

    private static readonly Vector3[] WaypointPositions =
    {
        new Vector3(0f, 0f, -12f),
        new Vector3(-5f, HangDeckTop, -26f),
        new Vector3(0f, 0f, -18f),
        new Vector3(5.5f, HopDeckTop, -26f),
    };

    /// <summary>Where the player starts, in ENTRADA: the lab has to be reachable from there.</summary>
    private static readonly Vector3 EntranceProbe = new Vector3(0f, 0f, -2f);

    // ── Verification ────────────────────────────────────────────────────────

    /// <summary>The checks run on a private copy of the new NavMesh this far up, like the Bug Lab's:
    /// nothing in another open scene can reach it there.</summary>
    private static readonly Vector3 VerifyLift = new Vector3(0f, 500f, 0f);

    /// <summary>How close to the NavMesh each end of a drop has to be. The validator's number
    /// (DropEndSampleRadius).</summary>
    private const float DropEndSampleRadius = 0.3f;

    // ── Signs ───────────────────────────────────────────────────────────────

    private static readonly Color SignColor = new Color(1f, 0.85f, 0.4f);
    private const float SignFontSize = 2.6f;

    private const string EntranceSign =
        "<b>v DROP LAB</b>\n<size=55%>Fase 8 · Hop 2.0 m · Hang 3.6 m · cases 12-16, 55</size>";

    private const string HangSign =
        "<b>HANG · 3.6 m</b>\n<size=62%>It stops at the edge, looks down and growls, turns its back, " +
        "hangs and lets go, and stays down a moment on landing: your window.\n" +
        "Way back up: the ramp on the west wall. On patrol it takes the ramp, not the drop.</size>";

    private const string HopSign =
        "<b>HOP · 2.0 m</b>\n<size=62%>It stops at the edge, looks down and growls, and jumps.\n" +
        "Way back up: the ramp on the east wall.\n" +
        "The rails are for you only: it drops straight through them.</size>";

    // ── Entry points ────────────────────────────────────────────────────────

    [MenuItem(MenuPath, true)]
    private static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(MenuPath)]
    private static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{LogTag} Leave Play mode first: this edits and saves NemesisTestbed.");
            return;
        }

        RunAndReport();
    }

    /// <summary>
    /// For -batchmode -executeMethod. Opens the testbed on its own (nobody's unsaved work can be in
    /// a batchmode editor), builds, runs the navigation validator on the result, and exits: 0 when
    /// the lab was built, baked and saved, 1 otherwise. The NavMesh checks and the validator's report
    /// are in the log either way.
    /// </summary>
    public static void BuildFromCommandLine()
    {
        int exitCode = 1;
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Outcome outcome = RunAndReport();

            // After the save, on what was saved: the drops, the ramps as their way back up, the
            // landings' clearance, the arcs, the route.
            EditorApplication.ExecuteMenuItem(ValidatorMenuPath);

            exitCode = outcome.Problems > 0 ? 1 : 0;
        }
        catch (Exception exception)
        {
            Debug.LogError($"{LogTag} {exception}");
        }
        finally
        {
            EditorApplication.Exit(exitCode);
        }
    }

    /// <summary>For -batchmode -executeMethod: Tools/Nemesis/Validate Navigation Setup on the testbed as
    /// it is saved, without building anything. Exits with 0; the report is in the log.</summary>
    public static void ValidateFromCommandLine()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.ExecuteMenuItem(ValidatorMenuPath);
        }
        catch (Exception exception)
        {
            Debug.LogError($"{LogTag} {exception}");
        }
        finally
        {
            EditorApplication.Exit(0);
        }
    }

    private static Outcome RunAndReport()
    {
        var log = new StringBuilder();
        Outcome outcome;

        try
        {
            outcome = Run(log);
        }
        catch (Exception exception)
        {
            log.AppendLine($"ERROR: {exception}");
            outcome = new Outcome { Problems = 1 };
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        string verdict = outcome.Problems > 0
            ? $"FAILED with {outcome.Problems} problem(s)."
            : outcome.FailedChecks > 0
                ? $"Built and saved; {outcome.FailedChecks} NavMesh check(s) did not pass (listed below)."
                : "Built, baked, verified and saved.";

        string report = $"{LogTag} {verdict}\n{log}";
        if (outcome.Problems > 0) Debug.LogError(report);
        else if (outcome.FailedChecks > 0) Debug.LogWarning(report);
        else Debug.Log(report);

        return outcome;
    }

    private struct Outcome
    {
        public int Problems;
        public int FailedChecks;
    }

    private static Outcome Run(StringBuilder log)
    {
        var assets = new BuildAssets
        {
            Floor = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath),
            Wall = AssetDatabase.LoadAssetAtPath<Material>(WallMaterialPath),
            Prop = AssetDatabase.LoadAssetAtPath<Material>(PropMaterialPath),
            Accent = AssetDatabase.LoadAssetAtPath<Material>(AccentMaterialPath),
            Cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx"),
        };

        if (assets.Cube == null)
        {
            log.AppendLine("ERROR: the built-in cube mesh is missing. Nothing was built.");
            return new Outcome { Problems = 1 };
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
        {
            log.AppendLine($"ERROR: {ScenePath} does not exist. Nothing was built.");
            return new Outcome { Problems = 1 };
        }

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool wasListed = scene.IsValid();
        bool wasLoaded = wasListed && scene.isLoaded;

        if (!wasLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        else if (scene.isDirty)
            log.AppendLine("Note: NemesisTestbed already had unsaved changes; they are saved together with the lab.");

        try
        {
            return new LabBuild(scene, assets, log).Execute();
        }
        finally
        {
            // A half-built lab never reaches the scene file: unsaved changes go with the scene when
            // this run opened it.
            if (!wasLoaded && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, !wasListed);
        }
    }

    private sealed class BuildAssets
    {
        public Material Floor;
        public Material Wall;
        public Material Prop;
        public Material Accent;
        public Mesh Cube;
    }

    // ── The build ───────────────────────────────────────────────────────────

    private sealed class LabBuild
    {
        private readonly Scene scene;
        private readonly BuildAssets assets;
        private readonly StringBuilder log;

        private readonly HashSet<GameObject> created = new HashSet<GameObject>();

        private readonly int groundLayer = LayerOrFallback("Ground", 3);
        private readonly int wallLayer = LayerOrFallback("Wall", 11);
        private readonly int propsLayer = LayerOrFallback("Props", 12);
        private readonly int railLayer = LayerOrFallback("Ignore Raycast", 2);
        private readonly int defaultLayer = LayerOrFallback("Default", 0);

        private readonly List<NemesisDropLink> drops = new List<NemesisDropLink>();
        private readonly List<Transform> waypoints = new List<Transform>();
        private readonly List<NavMeshSurface> baked = new List<NavMeshSurface>();

        private int problems;
        private int failedChecks;

        public LabBuild(Scene scene, BuildAssets assets, StringBuilder log)
        {
            this.scene = scene;
            this.assets = assets;
            this.log = log;
        }

        public Outcome Execute()
        {
            try
            {
                return BuildBakeAndSave();
            }
            catch
            {
                SweepStrays("after an error");
                throw;
            }
        }

        private Outcome BuildBakeAndSave()
        {
            GameObject testbed = FindRoot(TestbedRootName);
            if (testbed == null)
            {
                log.AppendLine($"ERROR: NemesisTestbed has no '{TestbedRootName}' root. Nothing was built.");
                return new Outcome { Problems = 1 };
            }

            List<NemesisController> controllers = FindInScene<NemesisController>();
            List<NavMeshSurface> surfaces = FindInScene<NavMeshSurface>();

            RemovePreviousLab(controllers);

            Transform root = Create(RootName, null).transform;
            BuildConnection(root, testbed);
            BuildHall(root);
            BuildDeck(root, "Hang deck (3.6 m)", HallWestX, HangDeckEastX, HangDeckTop);
            BuildDeck(root, "Hop deck (2.0 m)", HopDeckWestX, HallEastX, HopDeckTop);
            BuildRamps(root);
            BuildRails(root);
            BuildDrops(root);
            BuildSigns(root);
            NemesisRoute route = BuildRoute(root);
            RegisterRoute(route, controllers);

            problems += SweepStrays("after building");

            bool bakedAll = Bake(surfaces);
            if (bakedAll) Verify(controllers);

            problems += SweepStrays("before saving");

            if (!bakedAll)
            {
                log.AppendLine("NemesisTestbed was NOT saved: its NavMesh does not match the lab.");
                return new Outcome { Problems = Math.Max(1, problems), FailedChecks = failedChecks };
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                log.AppendLine($"ERROR: could not save {ScenePath}.");
                problems++;
            }
            else
            {
                log.AppendLine($"Saved {ScenePath}. The lab is reached from ENTRADA through its south wall " +
                               $"(doorway at x 0, z {EntranceZ}).");
            }

            return new Outcome { Problems = problems, FailedChecks = failedChecks };
        }

        // ── Previous run ────────────────────────────────────────────────────

        /// <summary>The previous run's route comes out of every controller BEFORE its root goes:
        /// once destroyed it would only be a missing reference, indistinguishable from anyone else's.
        /// </summary>
        private void RemovePreviousLab(List<NemesisController> controllers)
        {
            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name != RootName) continue;

                var oldRoutes = new HashSet<Object>(rootObject.GetComponentsInChildren<NemesisRoute>(true));
                foreach (NemesisController controller in controllers)
                {
                    var serialized = new SerializedObject(controller);
                    SerializedProperty list = serialized.FindProperty("routes");
                    if (list == null || !list.isArray) continue;

                    List<Object> values = ReadList(list);
                    if (values.RemoveAll(value => value != null && oldRoutes.Contains(value)) == 0) continue;

                    WriteList(list, values);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                Object.DestroyImmediate(rootObject);
                log.AppendLine("Removed the previous Drop Lab.");
            }
        }

        // ── Connection ──────────────────────────────────────────────────────

        private void BuildConnection(Transform root, GameObject testbed)
        {
            Transform group = Group("Connection (ENTRADA doorway + corridor)", root);

            float corridorMidZ = (EntranceZ + HallNorthZ) * 0.5f;
            float corridorLength = EntranceZ - HallNorthZ;
            float sideX = DoorwayHalfWidth + WallThickness * 0.5f;

            Box(group, "Corridor floor", new Vector3(0f, -FloorThickness * 0.5f, corridorMidZ),
                new Vector3((DoorwayHalfWidth + WallThickness) * 2f, FloorThickness, corridorLength), groundLayer, assets.Floor);
            Box(group, "Corridor wall W", new Vector3(-sideX, CorridorWallHeight * 0.5f, corridorMidZ),
                new Vector3(WallThickness, CorridorWallHeight, corridorLength), wallLayer, assets.Wall);
            Box(group, "Corridor wall E", new Vector3(sideX, CorridorWallHeight * 0.5f, corridorMidZ),
                new Vector3(WallThickness, CorridorWallHeight, corridorLength), wallLayer, assets.Wall);

            Transform original = testbed.transform.Find(EntranceWallPath);
            if (original == null)
            {
                log.AppendLine($"ERROR: {TestbedRootName}/{EntranceWallPath} not found. The lab is built but NOT " +
                               "connected to the testbed; reach it with the F10 teleports.");
                problems++;
                return;
            }

            BoxCollider originalCollider = original.GetComponent<BoxCollider>();
            Bounds wall = originalCollider != null
                ? WorldBounds(originalCollider)
                : new Bounds(original.position, original.lossyScale);

            if (Mathf.Abs(wall.center.z - EntranceZ) > 0.3f || wall.min.x >= -DoorwayHalfWidth || wall.max.x <= DoorwayHalfWidth)
                log.AppendLine($"Warning: ENTRADA's Wall_S is not where it used to be ({wall.center}, {wall.size}); " +
                               "the doorway and the corridor may not line up.");

            // Deactivated, never deleted: re-enabling it is the whole undo.
            if (original.gameObject.activeSelf)
            {
                original.gameObject.SetActive(false);
                EditorUtility.SetDirty(original.gameObject);
                log.AppendLine($"Deactivated {TestbedRootName}/{EntranceWallPath}; two segments with a 3 m doorway " +
                               "replace it under Drop Lab.");
            }
            else
            {
                log.AppendLine($"{TestbedRootName}/{EntranceWallPath} was already inactive (previous build); left as it is.");
            }

            MeshRenderer originalRenderer = original.GetComponent<MeshRenderer>();
            Material material = originalRenderer != null && originalRenderer.sharedMaterial != null
                ? originalRenderer.sharedMaterial
                : assets.Wall;
            int layer = original.gameObject.layer;

            BoxFromTo(group, "ENTRADA Wall_S (west of the Drop Lab doorway)",
                      new Vector3(wall.min.x, wall.min.y, wall.min.z), new Vector3(-DoorwayHalfWidth, wall.max.y, wall.max.z),
                      layer, material);
            BoxFromTo(group, "ENTRADA Wall_S (east of the Drop Lab doorway)",
                      new Vector3(DoorwayHalfWidth, wall.min.y, wall.min.z), new Vector3(wall.max.x, wall.max.y, wall.max.z),
                      layer, material);
        }

        // ── Hall ────────────────────────────────────────────────────────────

        private void BuildHall(Transform root)
        {
            Transform hall = Group("Hall", root);
            float midX = (HallWestX + HallEastX) * 0.5f;
            float midZ = (HallNorthZ + HallSouthZ) * 0.5f;
            float width = HallEastX - HallWestX;
            float depth = HallNorthZ - HallSouthZ;
            float wallY = HallWallHeight * 0.5f;

            Box(hall, "Floor", new Vector3(midX, -FloorThickness * 0.5f, midZ),
                new Vector3(width, FloorThickness, depth), groundLayer, assets.Floor);

            Box(hall, "Wall_W", new Vector3(HallWestX, wallY, midZ),
                new Vector3(WallThickness, HallWallHeight, depth + WallThickness), wallLayer, assets.Wall);
            Box(hall, "Wall_E", new Vector3(HallEastX, wallY, midZ),
                new Vector3(WallThickness, HallWallHeight, depth + WallThickness), wallLayer, assets.Wall);
            Box(hall, "Wall_S", new Vector3(midX, wallY, HallSouthZ),
                new Vector3(width + WallThickness, HallWallHeight, WallThickness), wallLayer, assets.Wall);

            // North: the corridor comes in through the middle, full height like every opening in the
            // testbed.
            float half = DoorwayHalfWidth + WallThickness;
            BoxFromTo(hall, "Wall_N (west of the corridor)",
                      new Vector3(HallWestX - WallThickness * 0.5f, 0f, HallNorthZ - WallThickness * 0.5f),
                      new Vector3(-half, HallWallHeight, HallNorthZ + WallThickness * 0.5f), wallLayer, assets.Wall);
            BoxFromTo(hall, "Wall_N (east of the corridor)",
                      new Vector3(half, 0f, HallNorthZ - WallThickness * 0.5f),
                      new Vector3(HallEastX + WallThickness * 0.5f, HallWallHeight, HallNorthZ + WallThickness * 0.5f),
                      wallLayer, assets.Wall);
        }

        // ── Decks ───────────────────────────────────────────────────────────

        /// <summary>
        /// A solid block from the floor to <paramref name="top"/>, on Ground: its top is walkable for
        /// the player and baked for the Nemesis. Filled with a Not Walkable volume so the voxeliser
        /// does not bake a trapped island on the floor inside it (the Bug Lab balcony's lesson).
        /// </summary>
        private void BuildDeck(Transform root, string name, float westX, float eastX, float top)
        {
            Transform deck = Group(name, root);
            float inWest = Mathf.Max(westX, HallWestX + WallThickness * 0.5f);
            float inEast = Mathf.Min(eastX, HallEastX - WallThickness * 0.5f);
            float inSouth = HallSouthZ + WallThickness * 0.5f;

            BoxFromTo(deck, "Deck (walkable top)", new Vector3(inWest, 0f, inSouth), new Vector3(inEast, top, DeckNorthZ),
                      groundLayer, assets.Prop);

            float insideTop = top - DeckInsideClearance;
            GameObject inside = Create("NavMesh Blocker (inside the deck)", deck, typeof(NavMeshModifierVolume));
            inside.layer = propsLayer;
            inside.transform.localPosition = new Vector3((inWest + inEast) * 0.5f, insideTop * 0.5f - 0.05f,
                                                         (inSouth + DeckNorthZ) * 0.5f);
            NavMeshModifierVolume blocker = inside.GetComponent<NavMeshModifierVolume>();
            blocker.center = Vector3.zero;
            blocker.size = new Vector3(inEast - inWest, insideTop + 0.1f, DeckNorthZ - inSouth);
            blocker.area = NotWalkableArea();

            // The edge, painted: where the rail is and where it drops from. Renderer only.
            Box(deck, "Edge stripe", new Vector3((inWest + inEast) * 0.5f, top + 0.005f, DeckNorthZ - 0.15f),
                new Vector3(inEast - inWest, 0.01f, 0.3f), defaultLayer, assets.Accent, solid: false);
        }

        // ── Ramps ───────────────────────────────────────────────────────────

        private void BuildRamps(Transform root)
        {
            Transform group = Group("Ramps (the way back up)", root);

            float hangX = HallWestX + WallThickness * 0.5f + RampWidth * 0.5f;
            float hopX = HallEastX - WallThickness * 0.5f - RampWidth * 0.5f;

            Ramp(group, "Ramp to the hang deck", new Vector3(hangX, 0f, HangRampFootZ),
                 new Vector3(hangX, HangDeckTop, DeckNorthZ));
            Ramp(group, "Ramp to the hop deck", new Vector3(hopX, 0f, HopRampFootZ),
                 new Vector3(hopX, HopDeckTop, DeckNorthZ));
        }

        /// <summary>
        /// A slab whose top face runs from <paramref name="bottom"/> to <paramref name="top"/>, thick
        /// enough to reach the floor under its high end: solid, so there is no pocket under it for
        /// the NavMesh (or a player) to get into. What sticks out below the floor is underground.
        /// </summary>
        private void Ramp(Transform parent, string name, Vector3 bottom, Vector3 top)
        {
            Vector3 along = top - bottom;
            Quaternion rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
            Vector3 normal = rotation * Vector3.up;
            float thickness = (top.y - bottom.y) * normal.y + FloorThickness;

            GameObject ramp = Box(parent, name, (bottom + top) * 0.5f - normal * (thickness * 0.5f),
                                  new Vector3(RampWidth, thickness, along.magnitude), groundLayer, assets.Prop);
            ramp.transform.localRotation = rotation;
        }

        // ── Rails (D9) ──────────────────────────────────────────────────────

        /// <summary>Invisible, on Ignore Raycast, along every edge the player could walk off: see the
        /// class comment for why the Nemesis goes through them and the player does not.</summary>
        private void BuildRails(Transform root)
        {
            Transform group = Group("Player-only rails (Ignore Raycast, invisible)", root);
            float rampInnerWest = HallWestX + WallThickness * 0.5f + RampWidth;
            float rampInnerEast = HallEastX - WallThickness * 0.5f - RampWidth;
            float inSouth = HallSouthZ + WallThickness * 0.5f;

            // Hang deck: its north edge east of where the ramp arrives, and its east edge.
            Rail(group, "Hang deck, north edge", new Vector3(rampInnerWest, HangDeckTop, DeckNorthZ),
                 new Vector3(HangDeckEastX, HangDeckTop, DeckNorthZ));
            Rail(group, "Hang deck, east edge", new Vector3(HangDeckEastX, HangDeckTop, DeckNorthZ),
                 new Vector3(HangDeckEastX, HangDeckTop, inSouth));

            // Hop deck: its north edge west of where the ramp arrives, and its west edge.
            Rail(group, "Hop deck, north edge", new Vector3(HopDeckWestX, HopDeckTop, DeckNorthZ),
                 new Vector3(rampInnerEast, HopDeckTop, DeckNorthZ));
            Rail(group, "Hop deck, west edge", new Vector3(HopDeckWestX, HopDeckTop, DeckNorthZ),
                 new Vector3(HopDeckWestX, HopDeckTop, inSouth));

            // The ramps' open sides, following the slope.
            Rail(group, "Hang ramp, open side", new Vector3(rampInnerWest, 0f, HangRampFootZ),
                 new Vector3(rampInnerWest, HangDeckTop, DeckNorthZ));
            Rail(group, "Hop ramp, open side", new Vector3(rampInnerEast, 0f, HopRampFootZ),
                 new Vector3(rampInnerEast, HopDeckTop, DeckNorthZ));
        }

        /// <summary>A collider-only rail standing on the line from <paramref name="from"/> to
        /// <paramref name="to"/>, sloped with it when the two ends are at different heights.</summary>
        private void Rail(Transform parent, string name, Vector3 from, Vector3 to)
        {
            Vector3 along = to - from;
            Quaternion rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
            Vector3 up = rotation * Vector3.up;

            GameObject rail = Box(parent, name, (from + to) * 0.5f + up * (RailHeight * 0.5f),
                                  new Vector3(RailThickness, RailHeight, along.magnitude), railLayer, null, visible: false);
            rail.transform.localRotation = rotation;
        }

        // ── Drops ───────────────────────────────────────────────────────────

        private void BuildDrops(Transform root)
        {
            Transform group = Group("Drop Links", root);

            drops.Add(Drop(group, "Drop_Hang", HangDropX, HangDeckTop));
            drops.Add(Drop(group, "Drop_Hop", HopDropX, HopDeckTop));
        }

        /// <summary>
        /// A NemesisDropLink over the decks' north edge, set up as plan §15.6 describes: TopEdge on
        /// the deck, BottomLanding on the hall floor. The ends are created BEFORE the component, with
        /// the names its Reset() looks for, so an editor that runs Reset on AddComponent reuses them
        /// instead of adding two more; the fields are then written explicitly either way. The
        /// NavMeshLink is configured here only so the scene reads sensibly: NemesisDropLink redoes it
        /// in Awake, one way, on NemesisDrop, with no cost override.
        /// </summary>
        private NemesisDropLink Drop(Transform parent, string name, float x, float top)
        {
            GameObject dropObject = Create(name, parent);
            dropObject.transform.localPosition = new Vector3(x, top, DeckNorthZ);

            Transform topEdge = Create("TopEdge", dropObject.transform).transform;
            topEdge.position = new Vector3(x, top, DeckNorthZ - TopEdgeSetback);
            topEdge.rotation = Quaternion.LookRotation(Vector3.forward);

            Transform landing = Create("BottomLanding", dropObject.transform).transform;
            landing.position = new Vector3(x, 0f, DeckNorthZ + LandingReach);

            NemesisDropLink drop = dropObject.AddComponent<NemesisDropLink>();

            var serialized = new SerializedObject(drop);
            serialized.FindProperty("topEdge").objectReferenceValue = topEdge;
            serialized.FindProperty("bottomLanding").objectReferenceValue = landing;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Reset() may have run on AddComponent: anything it made besides the two ends goes.
            var extras = new List<GameObject>();
            foreach (Transform child in dropObject.transform)
            {
                if (child != topEdge && child != landing) extras.Add(child.gameObject);
            }
            foreach (GameObject extra in extras) Object.DestroyImmediate(extra);

            NavMeshLink link = dropObject.GetComponent<NavMeshLink>();
            if (link != null)
            {
                link.startTransform = topEdge;
                link.endTransform = landing;
                link.bidirectional = false;
                link.area = NemesisDropLink.Area;
                link.costModifier = -1f;
                link.autoUpdate = false;
                EditorUtility.SetDirty(link);
            }

            log.AppendLine($"{name}: {drop.Height:0.0} m ({drop.KindFor(DropTuning.DefaultHangThreshold)}), " +
                           $"top {Format(topEdge.position)}, landing {Format(landing.position)}.");
            return drop;
        }

        // ── Signs ───────────────────────────────────────────────────────────

        private void BuildSigns(Transform root)
        {
            Transform group = Group("Signs", root);

            // On ENTRADA's side of its south wall, beside the doorway, where the player starts.
            Sign(group, "Sign (entrance)", EntranceSign, new Vector3(-3.6f, 2.2f, EntranceZ + WallThickness * 0.5f + 0.02f),
                 Vector3.forward, new Vector2(3.8f, 1.2f));

            // On each deck's north face, under its drop, read from the hall.
            Sign(group, "Sign (hang)", HangSign, new Vector3(HangDropX, 1.6f, DeckNorthZ + 0.02f),
                 Vector3.forward, new Vector2(5.2f, 2.6f));
            Sign(group, "Sign (hop)", HopSign, new Vector3(HopDropX, 1.0f, DeckNorthZ + 0.02f),
                 Vector3.forward, new Vector2(5.2f, 1.8f));
        }

        // ── Route ───────────────────────────────────────────────────────────

        /// <summary>
        /// The hall and both decks, in an order that makes every leg cross between floors: up a ramp,
        /// back down (by the ramp on patrol, D11), up the other ramp, back down. That is case 16 on
        /// its own, with nobody chasing anything.
        /// </summary>
        private NemesisRoute BuildRoute(Transform root)
        {
            GameObject routeObject = Create(RouteName, root, typeof(NemesisRoute));
            NemesisRoute route = routeObject.GetComponent<NemesisRoute>();

            var serialized = new SerializedObject(route);
            serialized.FindProperty("weight").floatValue = 1f;
            serialized.FindProperty("startUnlocked").boolValue = true;
            serialized.FindProperty("unlockedByPuzzleId").stringValue = string.Empty;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            bool tagExists = Array.IndexOf(UnityEditorInternal.InternalEditorUtility.tags, NemesisRoute.WaypointTag) >= 0;
            if (!tagExists)
            {
                log.AppendLine($"ERROR: the '{NemesisRoute.WaypointTag}' tag does not exist; {RouteName} has no usable waypoints.");
                problems++;
            }

            for (int i = 0; i < WaypointPositions.Length; i++)
            {
                GameObject waypoint = Create(WaypointNames[i], routeObject.transform);
                waypoint.transform.localPosition = WaypointPositions[i];
                if (tagExists) waypoint.tag = NemesisRoute.WaypointTag;
                waypoints.Add(waypoint.transform);
            }

            return route;
        }

        private void RegisterRoute(NemesisRoute route, List<NemesisController> controllers)
        {
            if (controllers.Count == 0)
            {
                log.AppendLine($"ERROR: no NemesisController in NemesisTestbed; {RouteName} is built but nobody patrols it.");
                problems++;
                return;
            }

            foreach (NemesisController controller in controllers)
            {
                var serialized = new SerializedObject(controller);
                SerializedProperty list = serialized.FindProperty("routes");
                if (list == null || !list.isArray)
                {
                    log.AppendLine($"ERROR: {controller.name}'s NemesisController has no 'routes' list.");
                    problems++;
                    continue;
                }

                List<Object> values = ReadList(list);
                if (!values.Contains(route)) values.Add(route);
                WriteList(list, values);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine($"{RouteName} added to {controller.name}'s routes (now {values.Count}).");
            }
        }

        // ── NavMesh ─────────────────────────────────────────────────────────

        private bool Bake(List<NavMeshSurface> surfaces)
        {
            var toBake = surfaces.FindAll(surface => surface.isActiveAndEnabled);
            if (toBake.Count == 0)
            {
                log.AppendLine("ERROR: NemesisTestbed has no active NavMeshSurface to bake.");
                problems++;
                return false;
            }

            bool allBaked = true;
            foreach (NavMeshSurface surface in toBake)
            {
                EditorUtility.DisplayProgressBar("Drop Lab", $"Baking {surface.name} (NemesisTestbed only)...", 0.5f);
                NavMeshData data = TestbedNavMeshBake.BakeSceneOnly(scene, surface, log);
                if (data == null)
                {
                    log.AppendLine($"ERROR: the bake of {surface.name} produced nothing; its old NavMesh was kept.");
                    problems++;
                    allBaked = false;
                    continue;
                }

                TestbedNavMeshBake.Install(scene, surface, data, log);
                baked.Add(surface);
            }

            return allBaked;
        }

        // ── Verification ────────────────────────────────────────────────────

        /// <summary>Reports, never blocks: the scene is saved whatever this finds. On a lifted copy of
        /// what was just baked, so it measures the baked data alone — and the drops' own links, which
        /// sit at the real height, are not part of any path it asks about.</summary>
        private void Verify(List<NemesisController> controllers)
        {
            var copies = new List<NavMeshDataInstance>();
            foreach (NavMeshSurface surface in baked)
                copies.Add(NavMesh.AddNavMeshData(surface.navMeshData, surface.transform.position + VerifyLift,
                                                  surface.transform.rotation));

            try
            {
                log.AppendLine("NavMesh checks (on the baked data alone):");
                ExpectComplete("ENTRADA -> hall", EntranceProbe, WaypointPositions[0], 1f);
                foreach (NemesisDropLink drop in drops) VerifyDrop(drop);
                VerifyDeckInside("Hang deck", HallWestX, HangDeckEastX);
                VerifyDeckInside("Hop deck", HopDeckWestX, HallEastX);
                VerifyRoute(controllers);
            }
            finally
            {
                foreach (NavMeshDataInstance copy in copies) copy.Remove();
            }
        }

        private void VerifyDrop(NemesisDropLink drop)
        {
            Vector3 top = drop.TopEdge.position;
            Vector3 bottom = drop.BottomLanding.position;

            bool topOk = SampleCopy(top, DropEndSampleRadius, out NavMeshHit topHit) &&
                         Mathf.Abs(topHit.position.y - VerifyLift.y - top.y) < 0.3f;
            bool bottomOk = SampleCopy(bottom, DropEndSampleRadius, out NavMeshHit bottomHit) &&
                            Mathf.Abs(bottomHit.position.y - VerifyLift.y - bottom.y) < 0.3f;

            if (topOk) Pass($"{drop.name}: TopEdge on the deck's NavMesh.");
            else Fail($"{drop.name}: TopEdge {Format(top)} is not on the deck's NavMesh within {DropEndSampleRadius} m.");

            if (bottomOk) Pass($"{drop.name}: BottomLanding on the hall's NavMesh.");
            else Fail($"{drop.name}: BottomLanding {Format(bottom)} is not on the hall's NavMesh within {DropEndSampleRadius} m.");

            float height = drop.Height;
            if (height >= NemesisDropLink.MinHeight && height <= NemesisDropLink.MaxHeight)
                Pass($"{drop.name}: {height:0.0} m, a {drop.KindFor(DropTuning.DefaultHangThreshold)}.");
            else
                Fail($"{drop.name}: {height:0.0} m, outside {NemesisDropLink.MinHeight}-{NemesisDropLink.MaxHeight} m.");

            if (topOk && bottomOk)
                ExpectComplete($"{drop.name}: way back up (landing -> top, by the ramp)", bottom, top, DropEndSampleRadius);
        }

        /// <summary>No NavMesh on the floor inside a deck: that would be an island only the drop's
        /// landing leads anywhere near, and the route graph would count it.</summary>
        private void VerifyDeckInside(string what, float westX, float eastX)
        {
            Vector3 inside = new Vector3((westX + eastX) * 0.5f, 0.1f, (HallSouthZ + DeckNorthZ) * 0.5f);
            if (SampleCopy(inside, 0.5f, out NavMeshHit hit) && hit.position.y - VerifyLift.y < 1f)
                Fail($"{what}: NavMesh on the floor INSIDE the block, at {Format(hit.position - VerifyLift)}.");
            else
                Pass($"{what}: nothing baked inside the block.");
        }

        private void VerifyRoute(List<NemesisController> controllers)
        {
            foreach (Transform waypoint in waypoints)
            {
                if (SampleCopy(waypoint.position, 0.5f, out NavMeshHit _)) continue;
                Fail($"{RouteName}: '{waypoint.name}' is not on the NavMesh (within 0.5 m).");
            }

            for (int i = 0; i < waypoints.Count; i++)
            {
                Transform from = waypoints[i];
                Transform to = waypoints[(i + 1) % waypoints.Count];
                ExpectComplete($"{RouteName} {from.name.Substring(0, 5)} -> {to.name.Substring(0, 5)}",
                               from.position, to.position, 1f);
            }

            foreach (NemesisController controller in controllers)
                ExpectComplete($"Connection: {controller.name} (where it stands) -> the lab",
                               controller.transform.position, WaypointPositions[0], NemesisNav.DefaultSampleRadius);
        }

        private static bool SampleCopy(Vector3 point, float radius, out NavMeshHit hit) =>
            NavMesh.SamplePosition(point + VerifyLift, out hit, radius, NavMesh.AllAreas);

        private void ExpectComplete(string what, Vector3 from, Vector3 to, float sampleRadius)
        {
            if (!SampleCopy(from, sampleRadius, out NavMeshHit a))
            {
                Fail($"{what}: the start {Format(from)} is not on the NavMesh.");
                return;
            }

            if (!SampleCopy(to, sampleRadius, out NavMeshHit b))
            {
                Fail($"{what}: the end {Format(to)} is not on the NavMesh.");
                return;
            }

            var path = new NavMeshPath();
            NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path);
            float length = PathLength(path);

            if (path.status != NavMeshPathStatus.PathComplete) Fail($"{what}: {path.status} ({length:0.0} m).");
            else Pass($"{what}: complete, {length:0.0} m.");
        }

        private void Pass(string line) => log.AppendLine($"  ok    {line}");

        private void Fail(string line)
        {
            log.AppendLine($"  FAIL  {line}");
            failedChecks++;
        }

        // ── Scene hygiene ───────────────────────────────────────────────────

        /// <summary>Everything created here must be in NemesisTestbed: object creation lands in the
        /// active scene first, so anything found outside is destroyed there and reported.</summary>
        private int SweepStrays(string when)
        {
            int strays = 0;
            foreach (GameObject go in created)
            {
                if (go == null || go.scene == scene) continue;
                log.AppendLine($"ERROR ({when}): '{go.name}' was in '{go.scene.name}' instead of NemesisTestbed. Destroyed.");
                Object.DestroyImmediate(go);
                strays++;
            }

            Scene active = SceneManager.GetActiveScene();
            if (active != scene && active.IsValid() && active.isLoaded)
            {
                foreach (GameObject rootObject in active.GetRootGameObjects())
                {
                    if (!created.Contains(rootObject) && rootObject.name != RootName) continue;
                    log.AppendLine($"ERROR ({when}): '{rootObject.name}' from this tool was a root of '{active.name}'. Destroyed.");
                    Object.DestroyImmediate(rootObject);
                    strays++;
                }
            }

            return strays;
        }

        // ── Object helpers ──────────────────────────────────────────────────

        private GameObject Create(string name, Transform parent, params Type[] components)
        {
            GameObject go = ObjectFactory.CreateGameObject(scene, HideFlags.None, name, components);
            created.Add(go);
            if (go.transform.parent == null && go.scene != scene) SceneManager.MoveGameObjectToScene(go, scene);
            if (parent != null) go.transform.SetParent(parent, false);

            if (go.scene != scene)
                throw new InvalidOperationException($"'{go.name}' could not be put into NemesisTestbed " +
                                                    $"(it is in '{go.scene.name}').");
            return go;
        }

        private Transform Group(string name, Transform parent)
        {
            Transform group = Create(name, parent).transform;
            group.localPosition = Vector3.zero;
            group.localRotation = Quaternion.identity;
            group.localScale = Vector3.one;
            return group;
        }

        /// <summary>A cube of the given size centred at <paramref name="localCenter"/>. Collider on
        /// by default; <paramref name="visible"/> off gives a collider-only piece.</summary>
        private GameObject Box(Transform parent, string name, Vector3 localCenter, Vector3 size, int layer,
                               Material material, bool solid = true, bool visible = true)
        {
            var components = new List<Type>();
            if (visible)
            {
                components.Add(typeof(MeshFilter));
                components.Add(typeof(MeshRenderer));
            }
            if (solid) components.Add(typeof(BoxCollider));

            GameObject box = Create(name, parent, components.ToArray());
            box.layer = layer;
            box.transform.localPosition = localCenter;
            box.transform.localRotation = Quaternion.identity;
            box.transform.localScale = size;

            if (visible)
            {
                box.GetComponent<MeshFilter>().sharedMesh = assets.Cube;
                if (material != null) box.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            return box;
        }

        private GameObject BoxFromTo(Transform parent, string name, Vector3 localMin, Vector3 localMax, int layer, Material material)
        {
            Vector3 min = Vector3.Min(localMin, localMax);
            Vector3 max = Vector3.Max(localMin, localMax);
            return Box(parent, name, (min + max) * 0.5f, max - min, layer, material);
        }

        /// <summary>A sign on a wall face. <paramref name="towardsReader"/> points from the wall to
        /// whoever reads it; TextMeshPro reads from its -Z side.</summary>
        private void Sign(Transform parent, string name, string text, Vector3 position, Vector3 towardsReader, Vector2 box)
        {
            GameObject labelObject = Create(name, parent, typeof(TextMeshPro));
            labelObject.transform.localPosition = parent.InverseTransformPoint(position);
            labelObject.transform.rotation = Quaternion.LookRotation(-towardsReader);

            TextMeshPro label = labelObject.GetComponent<TextMeshPro>();
            label.text = text;
            label.fontSize = SignFontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.color = SignColor;
            label.rectTransform.sizeDelta = box;
        }

        private GameObject FindRoot(string name)
        {
            foreach (GameObject rootObject in scene.GetRootGameObjects())
                if (rootObject.name == name) return rootObject;
            return null;
        }

        private List<T> FindInScene<T>() where T : Component
        {
            var found = new List<T>();
            foreach (GameObject rootObject in scene.GetRootGameObjects())
                found.AddRange(rootObject.GetComponentsInChildren<T>(true));
            return found;
        }
    }

    // ── Static helpers ──────────────────────────────────────────────────────

    private static Bounds WorldBounds(BoxCollider box)
    {
        Matrix4x4 matrix = box.transform.localToWorldMatrix;
        Vector3 c = box.center, e = box.size * 0.5f;
        var result = new Bounds(matrix.MultiplyPoint3x4(c - e), Vector3.zero);
        for (int i = 1; i < 8; i++)
        {
            Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            result.Encapsulate(matrix.MultiplyPoint3x4(corner));
        }
        return result;
    }

    private static float PathLength(NavMeshPath path)
    {
        float length = 0f;
        Vector3[] corners = path.corners;
        for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
        return length;
    }

    private static string Format(Vector3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";

    private static List<Object> ReadList(SerializedProperty list)
    {
        var values = new List<Object>(list.arraySize);
        for (int i = 0; i < list.arraySize; i++) values.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
        return values;
    }

    /// <summary>Rewritten whole rather than element by element: DeleteArrayElementAtIndex on an
    /// object reference only nulls it the first time.</summary>
    private static void WriteList(SerializedProperty list, List<Object> values)
    {
        list.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static int NotWalkableArea()
    {
        int area = NavMesh.GetAreaFromName("Not Walkable");
        return area >= 0 ? area : 1;
    }

    private static int LayerOrFallback(string name, int fallback)
    {
        int layer = LayerMask.NameToLayer(name);
        return layer >= 0 ? layer : fallback;
    }
}
