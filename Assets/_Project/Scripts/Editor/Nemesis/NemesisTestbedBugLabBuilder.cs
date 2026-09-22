using System;
using System.Collections.Generic;
using System.IO;
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
/// Builds the "Bug Lab" inside NemesisTestbed: one station per Nemesis QA bug that needs geometry to
/// be reproduced, joined to the testbed's own NavMesh by a corridor so the Nemesis walks in on
/// patrol and the player walks in from the spawn.
///
/// THE STATIONS (each has a sign saying what to look for):
///   S1  WIR-028, stairs with doors. Zona1's stairwell (ESCALERA_01) copied piece by piece at the
///       same relative positions: the two Bridges_stairs_2 flights, Stair_Divider and the column
///       that caps it, the Bridges_1 landing, and a DoorMetalRed at the foot and at the top where
///       Zona1 has DoorMetalRed (11) and (14). Turned 180° so both doors face the hall; a vestibule
///       and an upper room are what the doors open into.
///   S2  WIR-028, truss pillars. Four Bridges_support_2 paired like Zona1's crate room, with the
///       route running straight through both pairs. Three carry the NavMeshObstacle Zona1 added to
///       its crate-room pillars in the scene, which keeps their collider out of the bake: for those,
///       the prefab's Not Walkable volume is all that keeps the NavMesh out.
///   S3  WIR-018 / WIR-024, unreachable balcony. 2.6 m up, reached by a 0.8 m ramp: the player
///       (0.6 m) fits, the agent (1 m) does not, so the top is a NavMesh island of its own. A Not
///       Walkable volume fills the block, or the voxeliser bakes a second island inside it.
///   S4  WIR-020, trigger at a doorway. Two rooms joined by an opening; room B and the opening sit
///       inside a Default-layer trigger with no script, like Zona1's Amb_* ambience zones.
///   Route_BugLab walks S2, S4 and S1 and is added to the testbed Nemesis's route list.
///
/// LAYOUT (world metres, north = +z). West of SALA_LATERAL, which gets a 3 m doorway:
///
///        x -40        -33         -26   -22
///   42    +-----------+                        S1 stairwell (flights go north, landing at 39-42)
///   34    |  door  door |                      door wall: foot door x -31.6, top door x -34.7
///   28 +--+ vestibule / upper room (4.98 m) +--+
///      |BALCONY    [] []         |
///      |ramp        (S2)         +===corridor===+ SALA_LATERAL
///      |           [] []         |
///   12 +-------- opening --------+
///      |        room A (S4)      |
///  5.5 +-------- opening --------+   <- trigger edge
///      |   room B + trigger      |
/// -1.5 +-------------------------+
///
/// WHAT IT TOUCHES OUTSIDE ITS OWN ROOT, all of it on purpose:
///   - Testbed/Geometry/SALA_LATERAL/Wall_W is DEACTIVATED, never deleted; two segments leaving the
///     doorway replace it under Bug Lab. To take the lab out: delete "Bug Lab", re-enable Wall_W,
///     drop the missing entry from the Nemesis's routes and bake again.
///   - The testbed Nemesis's NemesisController.routes gets Route_BugLab appended. Nothing else in
///     that list is touched; the previous run's Route_BugLab is taken out before its root goes.
///   - The testbed's NavMeshSurface is baked again, and its NavMesh asset replaced like the
///     inspector's Bake button does.
///
/// WHY THE BAKE IS NOT THE BAKE BUTTON. In the editor, a surface that collects "All" gathers from
/// the whole main stage, which is every loaded scene: with Zona1 open, the Bake button (and
/// NavMeshAssetManager.StartBakingSurfaces, which is the same thing) bakes Zona1's floors, walls
/// and modifier volumes into the testbed's NavMesh, on top of it, at the same coordinates. So the
/// sources are collected root by root from this scene only, with the surface's own settings, and
/// built synchronously — which also means the scene is only saved once the NavMesh it references
/// exists.
///
/// THE TESTBED DOES NOT BAKE DEFAULT (its surface is Ground | Wall | Props, on purpose: the ceiling
/// and the "broken" SafeVolume rely on it), while Zona1 bakes Default too. So the pieces that are
/// Default in Zona1 and shape its NavMesh get a baked layer here, each saying why in its name:
/// the pillars are instanced as they are and switched to Props (the testbed's own convention, see
/// "SafeVolume (CORRECT - on Props)"), and the door frame posts and the divider's cap column get
/// Wall-layer stand-ins. The prefabs themselves are never modified.
///
/// WHAT IT NEVER DOES: change the active scene, open anything in Single mode, or save any scene
/// other than NemesisTestbed. The testbed is opened additively when it is not loaded, saved, and
/// closed again. Every object is created straight into the testbed and checked to be there; a run
/// that finds one of its objects anywhere else destroys it and says so.
///
/// Re-running is safe: the previous "Bug Lab" is removed first and everything is rebuilt.
/// </summary>
public static class NemesisTestbedBugLabBuilder
{
    private const string LogTag = "[NemesisBugLab]";
    private const string MenuPath = "Tools/Nemesis/Build Bug Lab (NemesisTestbed)";
    private const string ScenePath = "Assets/_Project/Scenes/Dev/NemesisTestbed.unity";

    private const string RootName = "Bug Lab";
    private const string RouteName = "Route_BugLab";
    private const string TestbedRootName = "Testbed";
    private const string SideRoomWallPath = "Geometry/SALA_LATERAL/Wall_W";

    // ── Assets ──────────────────────────────────────────────────────────────

    private const string StairsPrefabPath = "Assets/_Project/Prefabs/Environment/Bridges_stairs_2.prefab";
    private const string DeckPrefabPath = "Assets/_Project/Prefabs/Environment/Bridges_1.prefab";
    private const string PillarPrefabPath = "Assets/_Project/Prefabs/Environment/Bridges_support_2.prefab";
    private const string DoorPrefabPath = "Assets/_Project/Prefabs/Puzzle1/Doors/DoorMetalRed.prefab";

    private const string TestbedMaterials = "Assets/_Project/Scenes/Dev/NemesisTestbedMaterials";
    private const string FloorMaterialPath = TestbedMaterials + "/testbed_floor.mat";
    private const string WallMaterialPath = TestbedMaterials + "/testbed_wall.mat";
    private const string PropMaterialPath = TestbedMaterials + "/testbed_prop.mat";
    private const string AccentMaterialPath = "Assets/_Project/Scenes/Dev/Materials/Blockout_Accent.mat";

    // ── Shared dimensions ───────────────────────────────────────────────────

    /// <summary>The lab's spine. Every station straddles it and most of the route walks it.</summary>
    private const float SpineX = -33f;

    // The testbed's rooms: 3.5 m walls, 0.2 m thick, floors 0.2 m thick with their top at y = 0.
    private const float WallHeight = 3.5f;
    private const float WallThickness = 0.2f;
    private const float FloorThickness = 0.2f;

    // ── Connection ──────────────────────────────────────────────────────────

    // A 3 m doorway in the middle of SALA_LATERAL's west wall (x = -22, z 15..25): wide enough for
    // the agent to take it without brushing either side.
    private const float DoorwayZ = 20f;
    private const float DoorwayHalfWidth = 1.5f;
    private const float SideRoomWestX = -22f;

    // ── Hall (S2 and S3) ────────────────────────────────────────────────────

    private const float HallWestX = -40f;
    private const float HallEastX = -26f;
    private const float HallSouthZ = 12f;
    private const float HallNorthZ = 28f;

    // ── S1: stairwell ───────────────────────────────────────────────────────

    /// <summary>Where Zona1's ESCALERA_01 floor centre (29.5, 0, 9) lands. The copy is turned
    /// 180°, so the wall that holds both doors (Zona1 z = 13) faces south, at the hall.</summary>
    private static readonly Vector3 StairwellOrigin = new Vector3(SpineX, 0f, 38.1f);

    /// <summary>
    /// Walking surface of the top of Bridges_stairs_2 (1) as placed in Zona1: its mesh platform is
    /// at 3.21 m and the instance sits at y 1.77. The upper room is built flush with it, and Zona1's
    /// P1 walls stop and its P2 walls start at this height.
    /// </summary>
    private const float UpperFloorY = 4.98f;

    /// <summary>Top of Zona1's stairwell walls: P1 (0 to 5 m) plus P2 (5 to 8.5 m).</summary>
    private const float TallWallTop = 8.5f;

    /// <summary>
    /// Root scale of Zona1's two stair doors. They are DoorMetalWire, a variant of DoorMetalRed that
    /// only swaps the leaf, so a DoorMetalRed at this scale has exactly their frame.
    /// </summary>
    private static readonly Vector3 StairDoorScale = new Vector3(1.0277878f, 0.7557848f, 1.1502f);

    // The vestibule / upper room block between the hall and the stairwell's door wall.
    private const float BlockEastX = -29.4f;
    private const float BlockWestX = -36.6f;
    private const float FacadeOpeningHalfWidth = 2f;
    private const float FacadeOpeningHeight = 3f;

    // ── S2: pillars ─────────────────────────────────────────────────────────

    // Zona1's crate room: y and height scale of every pillar there, 1.9 m between the two of a pair
    // (Bridges_support_2 (8)/(9)), 6.62 m between rows ((8)/(9) to (6)/(7)).
    private const float PillarY = -0.070364f;
    private const float PillarScaleY = 0.97605f;
    private const float PillarPairHalfSpacing = 0.95f;
    private const float PillarRowHalfSpacing = 3.31f;

    // The NavMeshObstacle Zona1 added in the scene to nine of its pillars, (6), (7) and (8) among
    // them. The surface has Ignore NavMesh Obstacle on, so a pillar carrying it is left out of the
    // bake altogether and only the prefab's Not Walkable volume keeps it off the NavMesh.
    private static readonly Vector3 Zona1ObstacleCenter = new Vector3(0.035f, 2.573f, 0.019f);
    private static readonly Vector3 Zona1ObstacleSize = new Vector3(0.28f, 5.214f, 0.06f);

    // ── S3: balcony ─────────────────────────────────────────────────────────

    private const float BalconyTop = 2.6f;
    private const float BalconyEastX = -36.9f;
    private const float BalconySouthZ = 21f;

    /// <summary>Top of the Not Walkable volume that fills the balcony: clear of its walkable top by
    /// 0.4 m, so the top keeps its island.</summary>
    private const float BalconyInsideTop = 2.2f;

    /// <summary>
    /// Narrower than the agent (radius 0.5, so 1 m) and wider than the player (capsule radius 0.3).
    /// Against the wall on one side and a drop on the other, the voxeliser erodes it away entirely
    /// once it rises past the agent's 0.5 m step.
    /// </summary>
    private const float RampWidth = 0.8f;

    /// <summary>
    /// Where the ramp starts. With the top at the balcony's south face this makes it 17.2°: the
    /// player's own obstacle sweep (0.15 m ahead of a 0.25 m probe sphere) starts reading a ramp as
    /// a wall at about 19.5°, so this is as steep as it can be and still be walked normally.
    /// </summary>
    private const float RampFootZ = 12.6f;

    // ── S4: trigger at a doorway ────────────────────────────────────────────

    private const float RoomsEastX = -29.5f;
    private const float RoomsWestX = -36.5f;
    private const float RoomsSplitZ = 5.5f;
    private const float RoomBSouthZ = -1.5f;
    private const float OpeningHalfWidth = 1.25f;
    private const float OpeningHeight = 2.6f;

    // ── Route ───────────────────────────────────────────────────────────────

    // In the middle of the rooms, at least 2 m from any wall. Consecutive points are lined up so the
    // straight line between them crosses what is being tested: room B to the hall's north end runs
    // through the opening and then through the gap of both pillar pairs; the vestibule to the upper
    // room can only be walked up the stairs and through both doors.
    private static readonly string[] WaypointNames =
    {
        "WP_00 hall entrance",
        "WP_01 hall south, before the pillars (S2)",
        "WP_02 room A (S4)",
        "WP_03 room B, inside the trigger (S4)",
        "WP_04 hall north, past the pillars (S2)",
        "WP_05 vestibule, foot of the stairs (S1)",
        "WP_06 upper room, top of the stairs (S1)",
    };

    private static readonly Vector3[] WaypointPositions =
    {
        new Vector3(-29f, 0f, DoorwayZ),
        new Vector3(SpineX, 0f, 14f),
        new Vector3(SpineX, 0f, 8.75f),
        new Vector3(SpineX, 0f, 2f),
        new Vector3(SpineX, 0f, 26f),
        new Vector3(SpineX, 0f, 31f),
        new Vector3(SpineX, UpperFloorY, 31f),
    };

    // ── Verification ────────────────────────────────────────────────────────

    private const float StairsMaxPath = 30f;
    private const float DoorProbeOffset = 1.5f;
    private const float DoorProbeRadius = 1.2f;
    private const float DoorMaxPath = 4f;
    private const float WaypointTolerance = 0.5f;

    /// <summary>
    /// Where the checks run: a private copy of the new NavMesh, this far above the real one. Other
    /// open scenes cannot reach it up there — neither their NavMesh (Zona1 covers these same
    /// coordinates) nor their carving NavMeshObstacles, which cut holes into ANY NavMesh under them,
    /// the testbed's included, for as long as both scenes are open in the editor.
    /// </summary>
    private static readonly Vector3 VerifyLift = new Vector3(0f, 500f, 0f);

    /// <summary>The lab's footprint, for telling which foreign carving obstacles sit over it.</summary>
    private static readonly Rect LabFootprint = Rect.MinMaxRect(-41f, -3f, -21f, 43f);

    /// <summary>Inside SALA_LATERAL, in line with the new doorway and clear of its columns (its
    /// middle, (-16, 20), is inside Column_Lateral_1).</summary>
    private static readonly Vector3 SideRoomProbe = new Vector3(-18.5f, 0f, DoorwayZ);

    // ── Signs ───────────────────────────────────────────────────────────────

    private static readonly Color SignColor = new Color(1f, 0.85f, 0.4f);
    private const float SignFontSize = 2.6f;

    private const string EntranceSign =
        "<b>< BUG LAB</b>\n<size=55%>Nemesis QA stations: WIR-018 · WIR-020 · WIR-024 · WIR-028</size>";

    private const string StairsSign =
        "<b>S1 · WIR-028 · STAIRS WITH DOORS</b>\n<size=62%>Zona1's stairwell turned 180°: two " +
        "Bridges_stairs_2 flights, Stair_Divider and its cap, the Bridges_1 landing, and a DoorMetalRed " +
        "at the foot and at the top (Zona1's stair-door size).\n" +
        "PASS: the Nemesis goes up AND down without getting stuck, through both doors.</size>";

    private const string PillarsSign =
        "<b>S2 · WIR-028 · TRUSS PILLARS</b>\n<size=62%>Bridges_support_2 paired like Zona1's crate " +
        "room; the route runs straight through both pairs.\n" +
        "PASS: it walks AROUND them, never through a truss or through the 1 m gap of a pair.\n" +
        "The north pair and the south-east pillar carry Zona1's scene NavMeshObstacle: their collider " +
        "is left out of the bake and only the prefab's Not Walkable volume keeps them clear.</size>";

    private const string BalconySign =
        "<b>S3 · WIR-018 / WIR-024 · UNREACHABLE BALCONY</b>\n<size=62%>2.6 m up. The ramp is 0.8 m " +
        "wide: you fit, the Nemesis (1 m) does not. Stand up here where it can see you.\n" +
        "PASS: it goes to the closest point, stops running in place, searches and goes back to " +
        "patrol - it does not stay planted underneath, staring.</size>";

    private const string RampSign = "PLAYER-ONLY RAMP (0.8 m)";

    private const string TriggerSign =
        "<b>S4 · WIR-020 · TRIGGER AT A DOORWAY</b>\n<size=62%>Room B and its opening sit inside a " +
        "Default-layer trigger with no script, like Zona1's Amb_* zones. The yellow line on the floor " +
        "is its edge.\nPASS: it sees you and catches you across the edge, from either side.</size>";

    private const string TriggerEdgeLabel = "TRIGGER EDGE";

    // ── Entry point ─────────────────────────────────────────────────────────

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
    }

    private struct Outcome
    {
        public int Problems;
        public int FailedChecks;
    }

    private static Outcome Run(StringBuilder log)
    {
        BuildAssets assets = LoadAssets(log);
        if (assets == null) return new Outcome { Problems = 1 };

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
        {
            log.AppendLine($"ERROR: {ScenePath} does not exist. Nothing was built.");
            return new Outcome { Problems = 1 };
        }

        Scene active = SceneManager.GetActiveScene();
        bool activeWasDirty = active.isDirty;

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool wasListed = scene.IsValid();
        bool wasLoaded = wasListed && scene.isLoaded;

        if (!wasLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        else if (scene.isDirty)
            log.AppendLine("Note: NemesisTestbed already had unsaved changes; they are saved together with the lab.");

        try
        {
            var lab = new LabBuild(scene, assets, log);
            Outcome outcome = lab.Execute();

            // Creating objects can flag the active scene as modified even when nothing ends up in
            // it (the sweep in Execute makes sure of the second part). Said out loud so nobody goes
            // looking for a change that is not there.
            if (active != scene && active.IsValid() && active.isDirty && !activeWasDirty)
                log.AppendLine($"Note: '{active.name}' (the active scene) is now flagged as modified, but " +
                               "no Bug Lab object is in it (checked). It was not saved.");

            return outcome;
        }
        finally
        {
            // Unsaved changes are discarded here when something failed half way and the scene was
            // opened by this run: a half-built lab never reaches the scene file.
            if (!wasLoaded && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, !wasListed);
        }
    }

    // ── Assets ──────────────────────────────────────────────────────────────

    private sealed class BuildAssets
    {
        public GameObject Stairs;
        public GameObject Deck;
        public GameObject Pillar;
        public GameObject Door;
        public Material Floor;
        public Material Wall;
        public Material Prop;
        public Material Accent;
        public Mesh Cube;
    }

    private static BuildAssets LoadAssets(StringBuilder log)
    {
        var assets = new BuildAssets
        {
            Stairs = AssetDatabase.LoadAssetAtPath<GameObject>(StairsPrefabPath),
            Deck = AssetDatabase.LoadAssetAtPath<GameObject>(DeckPrefabPath),
            Pillar = AssetDatabase.LoadAssetAtPath<GameObject>(PillarPrefabPath),
            Door = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath),
            Floor = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath),
            Wall = AssetDatabase.LoadAssetAtPath<Material>(WallMaterialPath),
            Prop = AssetDatabase.LoadAssetAtPath<Material>(PropMaterialPath),
            Accent = AssetDatabase.LoadAssetAtPath<Material>(AccentMaterialPath),
            Cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx"),
        };

        bool missing = false;
        missing |= Require(assets.Stairs, StairsPrefabPath, log);
        missing |= Require(assets.Deck, DeckPrefabPath, log);
        missing |= Require(assets.Pillar, PillarPrefabPath, log);
        missing |= Require(assets.Door, DoorPrefabPath, log);
        missing |= Require(assets.Cube, "the built-in cube mesh", log);
        if (missing) return null;

        // Optional: without them the lab still works, it just renders plainer.
        Recommend(assets.Floor, FloorMaterialPath, log);
        Recommend(assets.Wall, WallMaterialPath, log);
        Recommend(assets.Prop, PropMaterialPath, log);
        Recommend(assets.Accent, AccentMaterialPath, log);
        return assets;
    }

    private static bool Require(Object asset, string what, StringBuilder log)
    {
        if (asset != null) return false;
        log.AppendLine($"ERROR: {what} is missing. Nothing was built.");
        return true;
    }

    private static void Recommend(Object asset, string path, StringBuilder log)
    {
        if (asset == null) log.AppendLine($"Warning: {path} is missing; those pieces render without a material.");
    }

    // ── The build ───────────────────────────────────────────────────────────

    private sealed class LabBuild
    {
        private readonly Scene scene;
        private readonly BuildAssets assets;
        private readonly StringBuilder log;

        /// <summary>Everything this run created, to prove at the end that all of it is in the
        /// testbed and nothing leaked into the active scene.</summary>
        private readonly HashSet<GameObject> created = new HashSet<GameObject>();

        private readonly int defaultLayer = LayerOrFallback("Default", 0);
        private readonly int groundLayer = LayerOrFallback("Ground", 3);
        private readonly int wallLayer = LayerOrFallback("Wall", 11);
        private readonly int propsLayer = LayerOrFallback("Props", 12);

        // Kept for the verification.
        private GameObject lowerDoor;
        private GameObject upperDoor;
        private readonly List<GameObject> pillars = new List<GameObject>();
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
                // Whatever failed, nothing of this run may stay behind in somebody else's scene.
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

            GameObject root = Create(RootName, null);
            BuildConnection(root.transform, testbed);
            BuildHall(root.transform);
            BuildStairs(root.transform);
            BuildPillars(root.transform);
            BuildBalcony(root.transform);
            BuildTriggerRooms(root.transform);
            NemesisRoute route = BuildRoute(root.transform);
            RegisterRoute(route, controllers);

            // Before the bake: it collects from this scene only, so anything that leaked out would
            // be missing from the NavMesh as well as sitting in somebody else's scene.
            problems += SweepStrays("after building");

            bool baked = Bake(surfaces);
            if (baked) Verify(controllers);

            problems += SweepStrays("before saving");

            if (!baked)
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
                log.AppendLine($"Saved {ScenePath}. The lab is reached from SALA_LATERAL through its west wall " +
                               $"(doorway at x {SideRoomWestX}, z {DoorwayZ}).");
            }

            return new Outcome { Problems = problems, FailedChecks = failedChecks };
        }

        // ── Previous run ────────────────────────────────────────────────────

        /// <summary>
        /// Takes the previous run's routes out of every controller BEFORE its root goes: once the
        /// objects are destroyed the list would only hold missing references, and nothing could
        /// tell those apart from somebody else's.
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
                log.AppendLine("Removed the previous Bug Lab.");
            }
        }

        // ── Connection ──────────────────────────────────────────────────────

        private void BuildConnection(Transform root, GameObject testbed)
        {
            Transform group = Group("Connection (SALA_LATERAL doorway + corridor)", root);

            // The corridor: 3 m clear, from SALA_LATERAL's west wall to the hall's east wall.
            float corridorMidX = (SideRoomWestX + HallEastX) * 0.5f;
            float corridorLength = SideRoomWestX - HallEastX;
            Box(group, "Corridor floor", new Vector3(corridorMidX, -FloorThickness * 0.5f, DoorwayZ),
                new Vector3(corridorLength, FloorThickness, (DoorwayHalfWidth + WallThickness) * 2f),
                groundLayer, assets.Floor);
            Box(group, "Corridor wall N", new Vector3(corridorMidX, WallHeight * 0.5f, DoorwayZ + DoorwayHalfWidth + WallThickness * 0.5f),
                new Vector3(corridorLength, WallHeight, WallThickness), wallLayer, assets.Wall);
            Box(group, "Corridor wall S", new Vector3(corridorMidX, WallHeight * 0.5f, DoorwayZ - DoorwayHalfWidth - WallThickness * 0.5f),
                new Vector3(corridorLength, WallHeight, WallThickness), wallLayer, assets.Wall);

            Transform original = testbed.transform.Find(SideRoomWallPath);
            if (original == null)
            {
                log.AppendLine($"ERROR: {TestbedRootName}/{SideRoomWallPath} not found. The lab is built but NOT " +
                               "connected to the testbed; reach it with the F10 teleports.");
                problems++;
                return;
            }

            BoxCollider originalCollider = original.GetComponent<BoxCollider>();
            Bounds wall = originalCollider != null
                ? WorldBounds(originalCollider)
                : new Bounds(original.position, original.lossyScale);

            float gapSouth = DoorwayZ - DoorwayHalfWidth;
            float gapNorth = DoorwayZ + DoorwayHalfWidth;
            if (wall.min.z >= gapSouth || wall.max.z <= gapNorth || Mathf.Abs(wall.center.x - SideRoomWestX) > 0.3f)
                log.AppendLine($"Warning: SALA_LATERAL's Wall_W is not where it used to be ({wall.center}, " +
                               $"{wall.size}); the doorway and the corridor may not line up.");

            // Deactivated, never deleted: re-enabling it is the whole undo.
            if (original.gameObject.activeSelf)
            {
                original.gameObject.SetActive(false);
                EditorUtility.SetDirty(original.gameObject);
                log.AppendLine($"Deactivated {TestbedRootName}/{SideRoomWallPath}; two segments with a 3 m doorway " +
                               "replace it under Bug Lab.");
            }
            else
            {
                log.AppendLine($"{TestbedRootName}/{SideRoomWallPath} was already inactive (previous build); left as it is.");
            }

            // Same look and layer as the wall they replace.
            MeshRenderer originalRenderer = original.GetComponent<MeshRenderer>();
            Material material = originalRenderer != null && originalRenderer.sharedMaterial != null
                ? originalRenderer.sharedMaterial
                : assets.Wall;
            int layer = original.gameObject.layer;

            BoxFromTo(group, "SALA_LATERAL Wall_W (south of the Bug Lab doorway)",
                      new Vector3(wall.min.x, wall.min.y, wall.min.z), new Vector3(wall.max.x, wall.max.y, gapSouth),
                      layer, material);
            GameObject north = BoxFromTo(group, "SALA_LATERAL Wall_W (north of the Bug Lab doorway)",
                                         new Vector3(wall.min.x, wall.min.y, gapNorth),
                                         new Vector3(wall.max.x, wall.max.y, wall.max.z), layer, material);

            // On SALA_LATERAL's side, next to the doorway, where anyone leaving the spawn passes.
            Vector3 face = new Vector3(wall.max.x + 0.02f, 2.2f, north.transform.position.z);
            Sign(group, "Sign (entrance)", EntranceSign, face, Vector3.right, new Vector2(3.2f, 1.6f));
        }

        // ── Hall ────────────────────────────────────────────────────────────

        private void BuildHall(Transform root)
        {
            Transform hall = Group("Hall (S2 pillars, S3 balcony)", root);
            float wallY = WallHeight * 0.5f;
            float midZ = (HallSouthZ + HallNorthZ) * 0.5f;

            Box(hall, "Floor", new Vector3(SpineX, -FloorThickness * 0.5f, midZ),
                new Vector3(HallEastX - HallWestX, FloorThickness, HallNorthZ - HallSouthZ), groundLayer, assets.Floor);

            Box(hall, "Wall_W", new Vector3(HallWestX, wallY, midZ),
                new Vector3(WallThickness, WallHeight, HallNorthZ - HallSouthZ + WallThickness), wallLayer, assets.Wall);

            // East: the corridor comes in through the middle.
            BoxFromTo(hall, "Wall_E (south of the corridor)",
                      new Vector3(HallEastX - WallThickness * 0.5f, 0f, HallSouthZ - WallThickness * 0.5f),
                      new Vector3(HallEastX + WallThickness * 0.5f, WallHeight, DoorwayZ - DoorwayHalfWidth),
                      wallLayer, assets.Wall);
            BoxFromTo(hall, "Wall_E (north of the corridor)",
                      new Vector3(HallEastX - WallThickness * 0.5f, 0f, DoorwayZ + DoorwayHalfWidth),
                      new Vector3(HallEastX + WallThickness * 0.5f, WallHeight, HallNorthZ + WallThickness * 0.5f),
                      wallLayer, assets.Wall);

            // North: only either side of S1's block, whose facade closes the middle.
            WallAlongX(hall, "Wall_N (west of S1)", HallWestX, BlockWestX, HallNorthZ, 0f, WallHeight, 0f, 0f, 0f);
            WallAlongX(hall, "Wall_N (east of S1)", BlockEastX, HallEastX, HallNorthZ, 0f, WallHeight, 0f, 0f, 0f);

            // South: the way into S4's room A, full height like every opening in the testbed.
            WallAlongX(hall, "Wall_S", HallWestX - WallThickness * 0.5f, HallEastX + WallThickness * 0.5f, HallSouthZ,
                       0f, WallHeight, SpineX - OpeningHalfWidth, SpineX + OpeningHalfWidth, WallHeight);
        }

        // ── S1: stairs with doors ───────────────────────────────────────────

        private void BuildStairs(Transform root)
        {
            Transform station = Group("S1 - WIR-028 stairs with doors", root);

            // Everything Zona1 has is placed in Zona1's own coordinates (Z1) under a frame that sits
            // where its stairwell floor centre goes, turned 180°. That keeps every number below
            // comparable, one to one, with the Zona1 scene it was read from.
            Transform frame = Group("Zona1 stairwell (copied, turned 180 deg)", station);
            frame.SetPositionAndRotation(StairwellOrigin, Quaternion.Euler(0f, 180f, 0f));

            Box(frame, "ESCALERA_01_Floor_0", Z1(29.5f, -0.1f, 9f), new Vector3(7f, FloorThickness, 8f), groundLayer, assets.Floor);
            Box(frame, "Wall_V26 (P1 + P2)", Z1(26.91f, TallWallTop * 0.5f, 9f), new Vector3(WallThickness, TallWallTop, 8f), wallLayer, assets.Wall);
            Box(frame, "Wall_V33 (P1 + P2)", Z1(32.162f, TallWallTop * 0.5f, 9f), new Vector3(WallThickness, TallWallTop, 8f), wallLayer, assets.Wall);
            Box(frame, "Wall_H5 (P1 + P2)", Z1(29.5f, TallWallTop * 0.5f, 5f), new Vector3(7f, TallWallTop, WallThickness), wallLayer, assets.Wall);
            Box(frame, "Stair_Divider", Z1(29.6f, 3f, 10.35f), new Vector3(1.2836545f, 9.909f, 5.104f), wallLayer, assets.Wall);

            // Zona1's Wall_column (29.506, 1.04, 8.375) caps the divider's landing end: a little
            // wider than the divider and 0.18 m longer, which is what makes the U-turn on the
            // landing as tight as it is. It is on Default there, and the testbed does not bake
            // Default, so it is a Wall-layer box of the same bounds here.
            Box(frame, "Stair_Divider cap (Zona1 Wall_column - Default there, Wall here)",
                Z1(29.506f, 4.546638f, 8.375f), new Vector3(1.40364f, 8.193524f, 1.523354f), wallLayer, assets.Wall);

            Prefab(assets.Stairs, frame, "Bridges_stairs_2 (lower flight)", Z1(27.97f, -1.331f, 10.77f),
                   Quaternion.Euler(0f, 180f, 0f), new Vector3(0.94139f, 1f, 1.1550341f));
            Prefab(assets.Stairs, frame, "Bridges_stairs_2 (1) (upper flight)", Z1(31.15f, 1.77f, 10.12f),
                   Quaternion.identity, new Vector3(0.89919686f, 1f, 1.0355312f));

            // The landing, and the two decks Zona1 slides under the flights. The latter are Ground
            // there as here, so they are part of what the voxeliser sees under the stairs.
            Prefab(assets.Deck, frame, "Bridges_1 (landing)", Z1(29.72f, 1.607f, 6.462f),
                   Quaternion.Euler(0f, -90f, 0f), new Vector3(1.4570816f, 1.0972f, 1.427567f));
            Prefab(assets.Deck, frame, "Bridges_1 (1) (under the upper flight)", Z1(31.160872f, 1.588f, 7.261733f),
                   Quaternion.Euler(-15f, 0f, 0f), Vector3.one);
            Prefab(assets.Deck, frame, "Bridges_1 (2) (under the lower flight)", Z1(27.998f, 0.27f, 10.293f),
                   Quaternion.Euler(15f, 0f, 0f), new Vector3(0.9623512f, 1f, 1f));

            // Zona1's DoorMetalRed (11) and (14), roots at their world pose (parent "---- DOORS ----"
            // included), with DoorMetalRed in place of the wire variant.
            lowerDoor = Prefab(assets.Door, frame, "DoorMetalRed (foot - Zona1's DoorMetalRed (11))",
                               Z1(25.81414f, 1.176f, 12.39636f), Quaternion.identity, StairDoorScale);
            upperDoor = Prefab(assets.Door, frame, "DoorMetalRed (top - Zona1's DoorMetalRed (14))",
                               Z1(33.43714f, 6.14f, 13.59236f), Quaternion.Euler(0f, 180f, 0f), StairDoorScale);

            // Zona1's H13 wall line, both storeys, with each hole cut to the frame that fills it —
            // measured rather than copied, so it follows the prefab if its frame ever changes. It
            // runs the full width of the vestibule block (x ±3.6) instead of Zona1's -3.585..3.5.
            float doorWallZ = Z1(0f, 0f, 13f).z;
            Bounds foot = LocalBounds(frame, FrameWorldBounds(lowerDoor));
            Bounds top = LocalBounds(frame, FrameWorldBounds(upperDoor));
            WallAlongX(frame, "Wall_H13 P1", -3.6f, 3.6f, doorWallZ, 0f, UpperFloorY, foot.min.x, foot.max.x, foot.max.y);
            WallAlongX(frame, "Wall_H13 P2", -3.6f, 3.6f, doorWallZ, UpperFloorY, TallWallTop, top.min.x, top.max.x, top.max.y);
            log.AppendLine($"S1 door holes (frame outer size): foot {foot.size.x:0.00} x {foot.max.y:0.00} m, " +
                           $"top {top.size.x:0.00} x {top.max.y - UpperFloorY:0.00} m.");
            CheckFrameSize(foot, lowerDoor.name);
            CheckFrameSize(top, upperDoor.name);

            AddJambs(station, lowerDoor);
            AddJambs(station, upperDoor);

            BuildStairsBlock(station);

            Sign(station, "Sign", StairsSign, new Vector3(SpineX, 4.4f, HallNorthZ - WallThickness * 0.5f - 0.02f),
                 Vector3.back, new Vector2(4f, 2.4f));
        }

        /// <summary>
        /// What the two doors open into: a vestibule on the ground floor, open to the hall, and the
        /// upper room on top of it, flush with the top of the upper flight. Lab-only, so in world
        /// coordinates rather than Zona1's.
        /// </summary>
        private void BuildStairsBlock(Transform station)
        {
            float doorWallSouthFace = StairwellOrigin.z - Z1(0f, 0f, 13f).z - WallThickness * 0.5f;   // 34.0
            float blockMidX = (BlockEastX + BlockWestX) * 0.5f;
            float blockWidth = BlockEastX - BlockWestX;

            // Both floors run under the door wall so the doorways have floor all the way through:
            // the vestibule up to where the stairwell floor starts, the upper slab up to the
            // stairwell side of the wall, where the upper flight's platform ends.
            BoxFromTo(station, "Vestibule floor", new Vector3(BlockWestX, -FloorThickness, HallNorthZ),
                      new Vector3(BlockEastX, 0f, doorWallSouthFace + WallThickness * 0.5f), groundLayer, assets.Floor);
            BoxFromTo(station, "Upper room floor (vestibule ceiling)", new Vector3(BlockWestX, UpperFloorY - FloorThickness, HallNorthZ),
                      new Vector3(BlockEastX, UpperFloorY, doorWallSouthFace + WallThickness), groundLayer, assets.Floor);

            float sideFrom = HallNorthZ - WallThickness * 0.5f;
            float sideTo = doorWallSouthFace + WallThickness;
            BoxFromTo(station, "Block wall E", new Vector3(BlockEastX - WallThickness * 0.5f, 0f, sideFrom),
                      new Vector3(BlockEastX + WallThickness * 0.5f, TallWallTop, sideTo), wallLayer, assets.Wall);
            BoxFromTo(station, "Block wall W", new Vector3(BlockWestX - WallThickness * 0.5f, 0f, sideFrom),
                      new Vector3(BlockWestX + WallThickness * 0.5f, TallWallTop, sideTo), wallLayer, assets.Wall);

            // The facade closes the middle of the hall's north side, with the way into the vestibule.
            WallAlongX(station, "Facade", BlockWestX, BlockEastX, HallNorthZ, 0f, TallWallTop,
                       SpineX - FacadeOpeningHalfWidth, SpineX + FacadeOpeningHalfWidth, FacadeOpeningHeight);

            log.AppendLine($"S1 block: vestibule and upper room x {BlockWestX}..{BlockEastX} ({blockWidth:0.0} m) " +
                           $"around x {blockMidX}, z {HallNorthZ}..{doorWallSouthFace:0.0}; upper floor at {UpperFloorY} m.");
        }

        private void CheckFrameSize(Bounds frame, string door)
        {
            // Zona1's stair doors measure 1.87 x 2.55 m with this scale. Anything far from that means
            // the prefab changed under this tool and the holes are worth a look.
            if (frame.size.x < 1.2f || frame.size.x > 2.6f || frame.size.y < 2.1f || frame.size.y > 3.2f)
                log.AppendLine($"Warning: {door}'s frame measures {frame.size}; the wall hole follows it, check it looks right.");
        }

        /// <summary>
        /// Wall-layer stand-ins for the frame's two posts. The posts are on Default: Zona1 bakes them
        /// (they are what narrows the doorway to the agent), the testbed does not. Collider only —
        /// the real posts are already there to see and to bump into.
        /// </summary>
        private void AddJambs(Transform station, GameObject door)
        {
            int count = 0;
            foreach (BoxCollider post in FramePosts(door))
            {
                Bounds bounds = WorldBounds(post);
                count++;
                Box(station, $"Jamb {count} of {door.name} (Wall stand-in for a Default frame post)",
                    station.InverseTransformPoint(bounds.center), bounds.size, wallLayer, null, solid: true, visible: false);
            }

            if (count != 2)
                log.AppendLine($"Warning: {door.name} has {count} frame post(s) instead of 2; its jambs may not match.");
        }

        // ── S2: pillars ─────────────────────────────────────────────────────

        private void BuildPillars(Transform root)
        {
            Transform station = Group("S2 - WIR-028 truss pillars", root);
            float north = (HallSouthZ + HallNorthZ) * 0.5f + PillarRowHalfSpacing;
            float south = (HallSouthZ + HallNorthZ) * 0.5f - PillarRowHalfSpacing;
            float east = SpineX + PillarPairHalfSpacing;
            float west = SpineX - PillarPairHalfSpacing;

            // Which of them carry Zona1's scene NavMeshObstacle follows Zona1: both of the (6)/(7)
            // pair, and (8) but not (9). That puts both cases side by side — a pillar whose own
            // collider is baked, and one where only the prefab's volume can keep the NavMesh out.
            AddPillar(station, "Bridges_support_2 (NE, obstacle - like Zona1 (6))", new Vector3(east, PillarY, north), true);
            AddPillar(station, "Bridges_support_2 (NW, obstacle - like Zona1 (7))", new Vector3(west, PillarY, north), true);
            AddPillar(station, "Bridges_support_2 (SE, obstacle - like Zona1 (8))", new Vector3(east, PillarY, south), true);
            AddPillar(station, "Bridges_support_2 (SW, plain - like Zona1 (9))", new Vector3(west, PillarY, south), false);

            Sign(station, "Sign", PillarsSign, new Vector3(HallEastX - WallThickness * 0.5f - 0.02f, 2f, 24.8f),
                 Vector3.left, new Vector2(5.6f, 2.6f));
        }

        private void AddPillar(Transform station, string name, Vector3 position, bool sceneObstacle)
        {
            GameObject pillar = Prefab(assets.Pillar, station, name, position, Quaternion.identity,
                                       new Vector3(1f, PillarScaleY, 1f));

            // Props on every object of the instance, the NavMesh Blocker volume included: a
            // NavMeshSurface skips volumes and colliders on layers it does not bake, and Default —
            // what the prefab uses, and what Zona1 bakes — is not one of the testbed's.
            foreach (Transform part in pillar.GetComponentsInChildren<Transform>(true))
            {
                part.gameObject.layer = propsLayer;
                MarkModified(part.gameObject);
            }

            if (sceneObstacle)
            {
                NavMeshObstacle obstacle = pillar.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = Zona1ObstacleCenter;
                obstacle.size = Zona1ObstacleSize;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
                obstacle.carvingMoveThreshold = 0.1f;
                obstacle.carvingTimeToStationary = 0.5f;
                EditorUtility.SetDirty(obstacle);
            }

            pillars.Add(pillar);
        }

        // ── S3: balcony ─────────────────────────────────────────────────────

        private void BuildBalcony(Transform root)
        {
            Transform station = Group("S3 - WIR-018 WIR-024 unreachable balcony", root);
            float westFace = HallWestX + WallThickness * 0.5f;
            float northFace = HallNorthZ - WallThickness * 0.5f;

            // Solid down to the floor and on Ground: its top is walkable for the player (whose
            // ground check reads Default and Ground) and becomes a NavMesh island of its own, which
            // is what makes a path to a player up here PARTIAL rather than impossible to query.
            BoxFromTo(station, "Balcony (walkable top, 2.6 m)", new Vector3(westFace, 0f, BalconySouthZ),
                      new Vector3(BalconyEastX, BalconyTop, northFace), groundLayer, assets.Prop);

            // A box is only its faces to the voxeliser, so inside one this size it finds a room 2.6 m
            // high and bakes an island on the floor in there — the first bake of this lab did. Not
            // Walkable from the floor to well under the top empties the inside and leaves the top,
            // the island that matters, alone. On Props for the same reason as the pillars' volume.
            GameObject inside = Create("NavMesh Blocker (inside the balcony)", station, typeof(NavMeshModifierVolume));
            inside.layer = propsLayer;
            inside.transform.localPosition = new Vector3((westFace + BalconyEastX) * 0.5f, BalconyInsideTop * 0.5f - 0.05f,
                                                         (BalconySouthZ + northFace) * 0.5f);
            NavMeshModifierVolume blocker = inside.GetComponent<NavMeshModifierVolume>();
            blocker.center = Vector3.zero;
            blocker.size = new Vector3(BalconyEastX - westFace, BalconyInsideTop + 0.1f, northFace - BalconySouthZ);
            blocker.area = NotWalkableArea();

            float rampX = westFace + RampWidth * 0.5f;
            Ramp(station, "Player-only ramp (0.8 m wide)", new Vector3(rampX, 0f, RampFootZ),
                 new Vector3(rampX, BalconyTop, BalconySouthZ), RampWidth, FloorThickness, groundLayer, assets.Prop);

            Sign(station, "Sign", BalconySign, new Vector3(BalconyEastX + 0.02f, 1.3f, (BalconySouthZ + northFace) * 0.5f),
                 Vector3.right, new Vector2(6f, 2.3f));
            Sign(station, "Sign (ramp)", RampSign, new Vector3(westFace + 0.02f, 2.2f, 15.5f),
                 Vector3.right, new Vector2(3f, 0.8f));
        }

        // ── S4: trigger at a doorway ────────────────────────────────────────

        private void BuildTriggerRooms(Transform root)
        {
            Transform station = Group("S4 - WIR-020 trigger at a doorway", root);
            float wallY = WallHeight * 0.5f;
            float width = RoomsEastX - RoomsWestX;
            float roomAMidZ = (RoomsSplitZ + HallSouthZ) * 0.5f;
            float roomBMidZ = (RoomBSouthZ + RoomsSplitZ) * 0.5f;

            Box(station, "Room A floor", new Vector3(SpineX, -FloorThickness * 0.5f, roomAMidZ),
                new Vector3(width, FloorThickness, HallSouthZ - RoomsSplitZ), groundLayer, assets.Floor);
            Box(station, "Room B floor", new Vector3(SpineX, -FloorThickness * 0.5f, roomBMidZ),
                new Vector3(width, FloorThickness, RoomsSplitZ - RoomBSouthZ), groundLayer, assets.Floor);

            float roomALength = HallSouthZ - RoomsSplitZ + WallThickness;
            float roomBLength = RoomsSplitZ - RoomBSouthZ + WallThickness;
            Box(station, "Room A wall W", new Vector3(RoomsWestX, wallY, roomAMidZ), new Vector3(WallThickness, WallHeight, roomALength), wallLayer, assets.Wall);
            Box(station, "Room A wall E", new Vector3(RoomsEastX, wallY, roomAMidZ), new Vector3(WallThickness, WallHeight, roomALength), wallLayer, assets.Wall);
            Box(station, "Room B wall W", new Vector3(RoomsWestX, wallY, roomBMidZ), new Vector3(WallThickness, WallHeight, roomBLength), wallLayer, assets.Wall);
            Box(station, "Room B wall E", new Vector3(RoomsEastX, wallY, roomBMidZ), new Vector3(WallThickness, WallHeight, roomBLength), wallLayer, assets.Wall);
            Box(station, "Room B wall S", new Vector3(SpineX, wallY, RoomBSouthZ), new Vector3(width + WallThickness, WallHeight, WallThickness), wallLayer, assets.Wall);

            // The opening between the two rooms, on the spine so the route and the Nemesis's view
            // both run straight through it.
            WallAlongX(station, "Wall A|B", RoomsWestX - WallThickness * 0.5f, RoomsEastX + WallThickness * 0.5f, RoomsSplitZ,
                       0f, WallHeight, SpineX - OpeningHalfWidth, SpineX + OpeningHalfWidth, OpeningHeight);

            // Room B's inside plus the opening, up to its room A face — so the trigger's edge is the
            // doorway itself, the place Zona1's Amb_* zones put one. Default layer and no script,
            // same as those: this is exactly what the project's "queries hit triggers" setting sees.
            float edgeZ = RoomsSplitZ + WallThickness * 0.5f;
            float insideSouth = RoomBSouthZ + WallThickness * 0.5f;
            GameObject trigger = Create("WIR-020 trigger (Default, no script)", station, typeof(BoxCollider));
            trigger.layer = defaultLayer;
            trigger.transform.localPosition = new Vector3(SpineX, wallY, (insideSouth + edgeZ) * 0.5f);
            BoxCollider box = trigger.GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(width - WallThickness, WallHeight, edgeZ - insideSouth);

            // A line on the floor where the edge crosses the opening. Renderer only: no collider, so
            // it is invisible to physics, to the Nemesis's senses and to the bake.
            Box(station, "Trigger edge (floor line)", new Vector3(SpineX, 0.005f, edgeZ),
                new Vector3(OpeningHalfWidth * 2f, 0.01f, 0.06f), defaultLayer, assets.Accent, solid: false);
            Label(station, "Trigger edge (floor label)", TriggerEdgeLabel, new Vector3(SpineX, 0.012f, edgeZ + 0.35f),
                  Quaternion.LookRotation(Vector3.down, Vector3.back), 2.4f, new Vector2(2.4f, 0.5f));

            Sign(station, "Sign", TriggerSign, new Vector3(RoomsEastX - WallThickness * 0.5f - 0.02f, 2f, roomAMidZ),
                 Vector3.left, new Vector2(5.6f, 2.4f));
        }

        // ── Route ───────────────────────────────────────────────────────────

        private NemesisRoute BuildRoute(Transform root)
        {
            GameObject routeObject = Create(RouteName, root, typeof(NemesisRoute));
            NemesisRoute route = routeObject.GetComponent<NemesisRoute>();

            // The defaults, written out: weight 1, open from the start, not gated by any puzzle.
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

                // Read, never written: waking it up is not this tool's call. But a gated Nemesis in
                // a scene with no puzzle to solve never walks into the lab, and that is worth a line.
                SerializedProperty gate = serialized.FindProperty("activatedByPuzzleId");
                if (gate != null && !string.IsNullOrWhiteSpace(gate.stringValue))
                    log.AppendLine($"Heads-up: {controller.name} only wakes up when puzzle '{gate.stringValue}' completes. " +
                                   "If it stays dormant in Play, clear 'Activated By Puzzle Id' on this instance (not " +
                                   "done here: that is not this tool's call).");
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
                EditorUtility.DisplayProgressBar("Bug Lab", $"Baking {surface.name} (NemesisTestbed only)...", 0.5f);
                NavMeshData data = BakeThisSceneOnly(surface);
                if (data == null)
                {
                    log.AppendLine($"ERROR: the bake of {surface.name} produced nothing; its old NavMesh was kept.");
                    problems++;
                    allBaked = false;
                    continue;
                }

                Install(surface, data);
                baked.Add(surface);
            }

            return allBaked;
        }

        /// <summary>
        /// NavMeshSurface's own build, collected from THIS scene's roots only. Same settings, same
        /// markups, same agent/obstacle filtering, same modifier volumes and the same bounds rule
        /// as the package — the one difference is that other loaded scenes are not part of it.
        /// </summary>
        private NavMeshData BakeThisSceneOnly(NavMeshSurface surface)
        {
            int mask = surface.layerMask.value;
            int agentType = surface.agentTypeID;

            // No public getter in this version of the package.
            SerializedProperty linksProperty = new SerializedObject(surface).FindProperty("m_GenerateLinks");
            bool generateLinks = linksProperty != null && linksProperty.boolValue;

            var markups = new List<NavMeshBuildMarkup>();
            foreach (NavMeshModifier modifier in NavMeshModifier.activeModifiers)
            {
                if (modifier == null || modifier.gameObject.scene != scene) continue;
                if ((mask & (1 << modifier.gameObject.layer)) == 0 || !modifier.AffectsAgentType(agentType)) continue;

                markups.Add(new NavMeshBuildMarkup
                {
                    root = modifier.transform,
                    overrideArea = modifier.overrideArea,
                    area = modifier.area,
                    ignoreFromBuild = modifier.ignoreFromBuild,
                    applyToChildren = modifier.applyToChildren,
                    overrideGenerateLinks = modifier.overrideGenerateLinks,
                    generateLinks = modifier.generateLinks,
                });
            }

            var sources = new List<NavMeshBuildSource>();
            var fromRoot = new List<NavMeshBuildSource>();
            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                if (!rootObject.activeInHierarchy) continue;

                // Clears its results list on every call, hence one list per root.
                UnityEditor.AI.NavMeshEditorHelpers.CollectSourcesInStage(
                    rootObject.transform, mask, surface.useGeometry, surface.defaultArea, generateLinks,
                    markups, false, scene, fromRoot);
                sources.AddRange(fromRoot);
            }

            if (surface.ignoreNavMeshAgent)
                sources.RemoveAll(source => source.component != null && source.component.GetComponent<NavMeshAgent>() != null);
            if (surface.ignoreNavMeshObstacle)
                sources.RemoveAll(source => source.component != null && source.component.GetComponent<NavMeshObstacle>() != null);

            // After the filters, like the package: a volume on a GameObject that also carries an
            // obstacle still counts. That is precisely what protects Zona1's obstacle pillars.
            int volumes = 0;
            foreach (NavMeshModifierVolume volume in NavMeshModifierVolume.activeModifiers)
            {
                if (volume == null || volume.gameObject.scene != scene) continue;
                if ((mask & (1 << volume.gameObject.layer)) == 0 || !volume.AffectsAgentType(agentType)) continue;

                Vector3 scale = volume.transform.lossyScale;
                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.ModifierBox,
                    transform = Matrix4x4.TRS(volume.transform.TransformPoint(volume.center), volume.transform.rotation, Vector3.one),
                    size = new Vector3(volume.size.x * Mathf.Abs(scale.x), volume.size.y * Mathf.Abs(scale.y),
                                       volume.size.z * Mathf.Abs(scale.z)),
                    area = volume.area,
                });
                volumes++;
            }

            Bounds bounds = SourceBounds(surface, sources);
            NavMeshData data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(
                surface.GetBuildSettings(), sources, bounds, surface.transform.position, surface.transform.rotation);

            log.AppendLine($"Baked {surface.name} from NemesisTestbed alone: {sources.Count} sources " +
                           $"({volumes} modifier volumes), layers {mask}.");
            return data;
        }

        /// <summary>The package's CalculateWorldBounds, which is private: every source in the
        /// surface's unscaled local space, grown by 0.1 m so coplanar sources are not clipped.</summary>
        private Bounds SourceBounds(NavMeshSurface surface, List<NavMeshBuildSource> sources)
        {
            Matrix4x4 worldToLocal = Matrix4x4.TRS(surface.transform.position, surface.transform.rotation, Vector3.one).inverse;
            var result = new Bounds();
            bool warned = false;

            foreach (NavMeshBuildSource source in sources)
            {
                switch (source.shape)
                {
                    case NavMeshBuildSourceShape.Mesh:
                        if (source.sourceObject is Mesh mesh)
                            result.Encapsulate(TransformBounds(worldToLocal * source.transform, mesh.bounds));
                        break;
                    case NavMeshBuildSourceShape.Box:
                    case NavMeshBuildSourceShape.Sphere:
                    case NavMeshBuildSourceShape.Capsule:
                    case NavMeshBuildSourceShape.ModifierBox:
                        result.Encapsulate(TransformBounds(worldToLocal * source.transform, new Bounds(Vector3.zero, source.size)));
                        break;
                    default:
                        // Terrain: the testbed has none, and the package needs the terrain module to size it.
                        if (!warned) log.AppendLine($"Warning: a {source.shape} source was left out of the bake bounds.");
                        warned = true;
                        break;
                }
            }

            result.Expand(0.1f);
            return result;
        }

        /// <summary>
        /// What the Bake button does with the result: the old asset — when it is this scene's own,
        /// in the folder named after it — is deleted and the new data is saved in its place.
        /// </summary>
        private void Install(NavMeshSurface surface, NavMeshData data)
        {
            string folder = Path.Combine(Path.GetDirectoryName(scene.path) ?? "Assets",
                                         Path.GetFileNameWithoutExtension(scene.path)).Replace('\\', '/');
            EnsureFolder(folder);

            NavMeshData old = surface.navMeshData;
            string oldPath = old != null ? AssetDatabase.GetAssetPath(old) : string.Empty;
            bool ownsOld = !string.IsNullOrEmpty(oldPath) && oldPath.StartsWith(folder + "/", StringComparison.Ordinal);
            string path = ownsOld ? oldPath : AssetDatabase.GenerateUniqueAssetPath($"{folder}/NavMesh-{surface.name}.asset");

            surface.RemoveData();
            var serialized = new SerializedObject(surface);
            serialized.FindProperty("m_NavMeshData").objectReferenceValue = data;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (surface.isActiveAndEnabled) surface.AddData();

            if (ownsOld) AssetDatabase.DeleteAsset(oldPath);
            AssetDatabase.CreateAsset(data, path);
            log.AppendLine($"NavMesh saved to {path}.");
        }

        // ── Verification ────────────────────────────────────────────────────

        /// <summary>
        /// Reports, never blocks: the scene is saved whatever this finds.
        ///
        /// Runs on a lifted copy of what was just baked (see <see cref="VerifyLift"/>), so what it
        /// measures is the baked data alone, and nothing in any open scene is touched to get it.
        /// </summary>
        private void Verify(List<NemesisController> controllers)
        {
            var copies = new List<NavMeshDataInstance>();
            foreach (NavMeshSurface surface in baked)
                copies.Add(NavMesh.AddNavMeshData(surface.navMeshData, surface.transform.position + VerifyLift,
                                                  surface.transform.rotation));

            try
            {
                log.AppendLine("NavMesh checks (on the baked data alone):");
                VerifyStairs();
                VerifyDoor(lowerDoor);
                VerifyDoor(upperDoor);
                foreach (GameObject pillar in pillars) VerifyPillar(pillar);
                VerifyBalcony();
                VerifyTriggerRooms();
                VerifyRoute(controllers);
                ReportForeignCarving();
            }
            finally
            {
                foreach (NavMeshDataInstance copy in copies) copy.Remove();
            }
        }

        /// <summary>
        /// Not a check: an explanation for holes someone WILL see in the editor. Zona1's props carry
        /// carving NavMeshObstacles, and with Zona1 open they cut into the testbed's NavMesh wherever
        /// they overlap it — the first run of this lab showed a pallet's diamond right in the new
        /// doorway. The baked data has none of it, and neither has Play in the testbed alone.
        /// </summary>
        private void ReportForeignCarving()
        {
            var names = new List<string>();
            string sceneName = null;
            foreach (NavMeshObstacle obstacle in Object.FindObjectsByType<NavMeshObstacle>(FindObjectsInactive.Exclude))
            {
                if (obstacle.gameObject.scene == scene || !obstacle.carving || !obstacle.isActiveAndEnabled) continue;
                Vector3 p = obstacle.transform.position;
                if (!LabFootprint.Contains(new Vector2(p.x, p.z))) continue;
                sceneName = obstacle.gameObject.scene.name;
                if (names.Count < 4) names.Add(obstacle.name);
                else if (names.Count == 4) names.Add("...");
            }

            if (names.Count > 0)
                log.AppendLine($"  note: carving obstacles of '{sceneName}' sit over the lab ({string.Join(", ", names)}). " +
                               "While that scene is open they cut holes into the testbed's NavMesh in the editor; " +
                               "the baked NavMesh and Play in the testbed alone are not affected.");
        }

        /// <summary>SamplePosition on the lifted copy. The hit comes back in the copy's space.</summary>
        private static bool SampleCopy(Vector3 point, float radius, out NavMeshHit hit) =>
            NavMesh.SamplePosition(point + VerifyLift, out hit, radius, NavMesh.AllAreas);

        private void VerifyStairs()
        {
            Vector3 foot = WaypointPositions[5];
            Vector3 top = WaypointPositions[6];
            ExpectComplete("S1 stairs, vestibule -> upper room", foot, top, 1f, StairsMaxPath);
            ExpectComplete("S1 stairs, upper room -> vestibule", top, foot, 1f, StairsMaxPath);
        }

        /// <summary>The same probe Zona1's doors were checked with: 1.5 m either side of the frame,
        /// across it, and a complete path shorter than 4 m means the doorway is open.</summary>
        private void VerifyDoor(GameObject door)
        {
            if (door == null) return;

            Bounds frame = FrameWorldBounds(door);
            Vector3 across = frame.size.x < frame.size.z ? Vector3.right : Vector3.forward;
            Vector3 centre = new Vector3(frame.center.x, frame.min.y + 0.1f, frame.center.z);
            ExpectComplete($"S1 door open: {door.name}", centre - across * DoorProbeOffset,
                           centre + across * DoorProbeOffset, DoorProbeRadius, DoorMaxPath);
        }

        /// <summary>
        /// No NavMesh inside the truss, on a 3 x 3 grid over its footprint — the walkable spot the
        /// fix closes sat between the legs, not necessarily at the centre. Then a softer note: NavMesh
        /// right against a face means nothing eroded the agent's radius there, and its body will
        /// clip into the pillar even though its centre never enters it.
        /// </summary>
        private void VerifyPillar(GameObject pillar)
        {
            Renderer body = pillar.GetComponent<Renderer>();
            if (body == null)
            {
                Fail($"S2 {pillar.name}: no renderer to measure it by.");
                return;
            }

            Bounds bounds = body.bounds;
            int inside = 0;
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    Vector3 probe = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, (i + 0.5f) / 3f), 0.1f,
                                                Mathf.Lerp(bounds.min.z, bounds.max.z, (j + 0.5f) / 3f));
                    if (SampleCopy(probe, 0.3f, out NavMeshHit hit) && InsideXZ(bounds, hit.position))
                        inside++;
                }
            }

            if (inside > 0)
            {
                Fail($"S2 {pillar.name}: NavMesh INSIDE the truss ({inside}/9 probes).");
                return;
            }

            int againstFace = 0;
            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;
            Vector3[] faces =
            {
                new Vector3(c.x + e.x + 0.2f, 0.1f, c.z), new Vector3(c.x - e.x - 0.2f, 0.1f, c.z),
                new Vector3(c.x, 0.1f, c.z + e.z + 0.2f), new Vector3(c.x, 0.1f, c.z - e.z - 0.2f),
            };
            foreach (Vector3 probe in faces)
                if (SampleCopy(probe, 0.12f, out NavMeshHit _)) againstFace++;

            Pass($"S2 {pillar.name}: no NavMesh inside.");
            if (againstFace > 0)
                log.AppendLine($"  note: NavMesh reaches within ~0.3 m of {againstFace} face(s) of {pillar.name}; " +
                               "with the agent's 0.5 m radius its body can clip into this pillar.");
        }

        private void VerifyBalcony()
        {
            float midZ = (BalconySouthZ + HallNorthZ - WallThickness * 0.5f) * 0.5f;
            Vector3 below = new Vector3(BalconyEastX + 1.3f, 0f, midZ);
            Vector3 onTop = new Vector3((HallWestX + BalconyEastX) * 0.5f, BalconyTop + 0.05f, midZ);

            if (!SampleCopy(onTop, 1f, out NavMeshHit top) || Mathf.Abs(top.position.y - VerifyLift.y - BalconyTop) > 0.3f)
            {
                Fail("S3 balcony: no NavMesh on top. A player up there cannot be sampled, so the Nemesis never " +
                     "reads the belief as unreachable (NemesisDecision.IsBeliefUnreachable).");
                return;
            }

            // The block is hollow to the voxeliser; its Not Walkable volume is what keeps a trapped
            // island off the floor in there.
            Vector3 insideFloor = new Vector3(onTop.x, 0.1f, midZ);
            if (SampleCopy(insideFloor, 0.3f, out NavMeshHit trapped))
                Fail($"S3 balcony: NavMesh on the floor INSIDE the block, at {Format(trapped.position - VerifyLift)}.");

            if (!SampleCopy(below, 1f, out NavMeshHit start))
            {
                Fail("S3 balcony: the floor in front of it is not on the NavMesh.");
                return;
            }

            var path = new NavMeshPath();
            NavMesh.CalculatePath(start.position, top.position, NavMesh.AllAreas, path);
            Vector3 end = path.corners.Length > 0 ? path.corners[path.corners.Length - 1] : start.position;

            if (path.status == NavMeshPathStatus.PathPartial)
                Pass($"S3 balcony unreachable: partial path, ends at {Format(end - VerifyLift)} " +
                     $"({Vector3.Distance(end, top.position):0.0} m from the top).");
            else
                Fail($"S3 balcony: path from below is {path.status}, expected PathPartial.");
        }

        private void VerifyTriggerRooms()
        {
            ExpectComplete("S4 room A -> room B through the opening", WaypointPositions[2], WaypointPositions[3], 1f, 12f);
        }

        private void VerifyRoute(List<NemesisController> controllers)
        {
            foreach (Transform waypoint in waypoints)
            {
                if (SampleCopy(waypoint.position, WaypointTolerance, out NavMeshHit _)) continue;
                Fail($"{RouteName}: '{waypoint.name}' is not on the NavMesh (within {WaypointTolerance} m).");
            }

            for (int i = 0; i < waypoints.Count; i++)
            {
                Transform from = waypoints[i];
                Transform to = waypoints[(i + 1) % waypoints.Count];
                ExpectComplete($"{RouteName} {from.name.Substring(0, 5)} -> {to.name.Substring(0, 5)}",
                               from.position, to.position, 1f, float.PositiveInfinity);
            }

            ExpectComplete("Connection: SALA_LATERAL -> hall entrance", SideRoomProbe, WaypointPositions[0], 1f, float.PositiveInfinity);
            foreach (NemesisController controller in controllers)
                ExpectComplete($"Connection: {controller.name} (where it stands) -> hall entrance",
                               controller.transform.position, WaypointPositions[0], NemesisNav.DefaultSampleRadius,
                               float.PositiveInfinity);
        }

        private void ExpectComplete(string what, Vector3 from, Vector3 to, float sampleRadius, float maxLength)
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

            if (path.status != NavMeshPathStatus.PathComplete)
                Fail($"{what}: {path.status} ({length:0.0} m).");
            else if (length > maxLength)
                Fail($"{what}: complete but {length:0.0} m, over the {maxLength:0} m expected.");
            else
                Pass($"{what}: complete, {length:0.0} m.");
        }

        private void Pass(string line) => log.AppendLine($"  ok    {line}");

        private void Fail(string line)
        {
            log.AppendLine($"  FAIL  {line}");
            failedChecks++;
        }

        // ── Scene hygiene ───────────────────────────────────────────────────

        /// <summary>
        /// Everything created here must be in NemesisTestbed. Object creation lands in the ACTIVE
        /// scene first — Zona1, maybe with somebody's unsaved work — so anything found outside is
        /// destroyed there and reported, and the active scene's roots are checked on top of that.
        /// </summary>
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

        /// <summary>
        /// Every object goes through here. Moved into the testbed before anything else happens to
        /// it, whatever scene it was born in, and checked: an object that cannot be put there is a
        /// hard stop rather than a silent leak into somebody else's scene.
        /// </summary>
        private GameObject Create(string name, Transform parent, params Type[] components)
        {
            GameObject go = ObjectFactory.CreateGameObject(scene, HideFlags.None, name, components);
            Adopt(go, parent);
            return go;
        }

        private void Adopt(GameObject go, Transform parent)
        {
            created.Add(go);
            if (go.transform.parent == null && go.scene != scene) SceneManager.MoveGameObjectToScene(go, scene);
            if (parent != null) go.transform.SetParent(parent, false);

            if (go.scene != scene)
                throw new InvalidOperationException($"'{go.name}' could not be put into NemesisTestbed " +
                                                    $"(it is in '{go.scene.name}').");
        }

        private Transform Group(string name, Transform parent)
        {
            Transform group = Create(name, parent).transform;
            group.localPosition = Vector3.zero;
            group.localRotation = Quaternion.identity;
            group.localScale = Vector3.one;
            return group;
        }

        private GameObject Prefab(GameObject prefab, Transform parent, string name, Vector3 localPosition,
                                  Quaternion localRotation, Vector3 localScale)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Adopt(instance, parent);
            instance.name = name;

            Transform t = instance.transform;
            t.localPosition = localPosition;
            t.localRotation = localRotation;
            t.localScale = localScale;
            MarkModified(t);
            MarkModified(instance);
            return instance;
        }

        /// <summary>A cube of the given size centred at <paramref name="localCenter"/>. Collider on
        /// by default; <paramref name="visible"/> off gives a collider-only stand-in.</summary>
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

        /// <summary>
        /// A wall along X at <paramref name="z"/>, from <paramref name="y0"/> to <paramref name="y1"/>,
        /// with an opening from the bottom up to <paramref name="holeTop"/> between the two hole x's.
        /// Pass a zero-width hole for a plain wall. Pieces thinner than a centimetre are skipped.
        /// </summary>
        private void WallAlongX(Transform parent, string name, float x0, float x1, float z, float y0, float y1,
                                float holeX0, float holeX1, float holeTop)
        {
            float lo = Mathf.Min(x0, x1), hi = Mathf.Max(x0, x1);
            float holeLo = Mathf.Clamp(Mathf.Min(holeX0, holeX1), lo, hi);
            float holeHi = Mathf.Clamp(Mathf.Max(holeX0, holeX1), lo, hi);
            float z0 = z - WallThickness * 0.5f, z1 = z + WallThickness * 0.5f;
            const float MinPiece = 0.01f;

            if (holeHi - holeLo < MinPiece)
            {
                BoxFromTo(parent, name, new Vector3(lo, y0, z0), new Vector3(hi, y1, z1), wallLayer, assets.Wall);
                return;
            }

            if (holeLo - lo >= MinPiece)
                BoxFromTo(parent, $"{name} (1)", new Vector3(lo, y0, z0), new Vector3(holeLo, y1, z1), wallLayer, assets.Wall);
            if (hi - holeHi >= MinPiece)
                BoxFromTo(parent, $"{name} (2)", new Vector3(holeHi, y0, z0), new Vector3(hi, y1, z1), wallLayer, assets.Wall);
            if (y1 - holeTop >= MinPiece)
                BoxFromTo(parent, $"{name} (over the opening)", new Vector3(holeLo, Mathf.Max(y0, holeTop), z0),
                          new Vector3(holeHi, y1, z1), wallLayer, assets.Wall);
        }

        /// <summary>A slab whose top face runs from <paramref name="bottom"/> to <paramref name="top"/>,
        /// with its thickness below that line. Parent must be world-aligned.</summary>
        private void Ramp(Transform parent, string name, Vector3 bottom, Vector3 top, float width, float thickness,
                          int layer, Material material)
        {
            Vector3 along = top - bottom;
            Quaternion rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
            Vector3 normal = rotation * Vector3.up;
            GameObject ramp = Box(parent, name, (bottom + top) * 0.5f - normal * (thickness * 0.5f),
                                  new Vector3(width, thickness, along.magnitude), layer, material);
            ramp.transform.localRotation = rotation;
        }

        /// <summary>A sign on a wall face. <paramref name="towardsReader"/> points from the wall to
        /// whoever reads it; TextMeshPro reads from its -Z side, so +Z faces the other way.</summary>
        private void Sign(Transform parent, string name, string text, Vector3 position, Vector3 towardsReader, Vector2 box)
        {
            Label(parent, name, text, position, Quaternion.LookRotation(-towardsReader), SignFontSize, box);
        }

        private void Label(Transform parent, string name, string text, Vector3 position, Quaternion rotation,
                           float fontSize, Vector2 box)
        {
            GameObject labelObject = Create(name, parent, typeof(TextMeshPro));
            labelObject.transform.localPosition = parent.InverseTransformPoint(position);
            labelObject.transform.rotation = rotation;

            TextMeshPro label = labelObject.GetComponent<TextMeshPro>();
            label.text = text;
            label.fontSize = fontSize;
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

    /// <summary>A Zona1 world position, relative to its stairwell's floor centre (29.5, 0, 9):
    /// local coordinates for the S1 frame.</summary>
    private static Vector3 Z1(float x, float y, float z) => new Vector3(x - 29.5f, y, z - 9f);

    private static IEnumerable<BoxCollider> FrameColliders(GameObject door)
    {
        Transform frame = door != null ? door.transform.Find("Frame") : null;
        return frame != null ? frame.GetComponents<BoxCollider>() : Array.Empty<BoxCollider>();
    }

    /// <summary>The frame's posts: its colliders taller than a metre (the third one is the lintel).</summary>
    private static IEnumerable<BoxCollider> FramePosts(GameObject door)
    {
        foreach (BoxCollider collider in FrameColliders(door))
            if (WorldBounds(collider).size.y > 1f) yield return collider;
    }

    /// <summary>Outer bounds of a door frame, from its colliders — what physics and the NavMesh
    /// see, not the mesh. Falls back to the renderers when the frame is not where it is expected.</summary>
    private static Bounds FrameWorldBounds(GameObject door)
    {
        bool any = false;
        var bounds = new Bounds();
        foreach (BoxCollider collider in FrameColliders(door))
        {
            Bounds b = WorldBounds(collider);
            if (!any) bounds = b;
            else bounds.Encapsulate(b);
            any = true;
        }

        if (any || door == null) return bounds;

        foreach (Renderer renderer in door.GetComponentsInChildren<Renderer>())
        {
            if (!any) bounds = renderer.bounds;
            else bounds.Encapsulate(renderer.bounds);
            any = true;
        }

        return bounds;
    }

    /// <summary>World AABB of a box collider, from its own centre and size: no physics query, so it
    /// is right before any physics update and on inactive objects too.</summary>
    private static Bounds WorldBounds(BoxCollider box)
    {
        return TransformBounds(box.transform.localToWorldMatrix, new Bounds(box.center, box.size));
    }

    private static Bounds LocalBounds(Transform space, Bounds world)
    {
        return TransformBounds(space.worldToLocalMatrix, world);
    }

    private static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
    {
        Vector3 c = bounds.center, e = bounds.extents;
        var result = new Bounds(matrix.MultiplyPoint3x4(c - e), Vector3.zero);
        for (int i = 1; i < 8; i++)
        {
            Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            result.Encapsulate(matrix.MultiplyPoint3x4(corner));
        }
        return result;
    }

    private static bool InsideXZ(Bounds bounds, Vector3 point) =>
        point.x > bounds.min.x && point.x < bounds.max.x && point.z > bounds.min.z && point.z < bounds.max.z;

    private static float PathLength(NavMeshPath path)
    {
        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
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
    /// object reference only nulls it the first time, which is a trap for exactly this job.</summary>
    private static void WriteList(SerializedProperty list, List<Object> values)
    {
        list.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    /// <summary>A plain field write on a prefab instance is only kept as an override once recorded;
    /// otherwise the next reload quietly puts the source's value back.</summary>
    private static void MarkModified(Object target)
    {
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        EditorUtility.SetDirty(target);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
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
