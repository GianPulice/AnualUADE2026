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
/// Builds the "Hiding Lab" inside NemesisTestbed: the Hiding Test Area of TestIñaki (a locker in
/// sight, a locker round the corner, a table, a container), spread over corridors and closed rooms
/// so the Fase 2 hiding cases (checklist H-1..H-10) can be played next to the rest of the testbed,
/// and so NemesisRooms has real rooms to tell apart (§3.4: "seen entering a room").
///
/// LAYOUT (world metres, north = +z). East of PASILLO, whose east wall gets two 4 m openings:
///
///          x 2               11                20     24      28
///   z 34   +=================================================+
///     PASILLO   PASILLO NORTE           WP_09 behind the row  |
///   z 30 --+---------------------------------+      |         |
///          | [F1][F2][F3]  VESTUARIO         |      | PASILLO |
///          |                                 | door | ESTE    +------+
///          |              door  DEPOSITO  [container] |       CUARTITO|
///   z 19   +------ door --+     [crate]      |      |  door  [locker]|
///          |[mesa 2]      |                  |      |       +------+
///          |[mesa 1]  SALA DE MESAS  [crate] |      |[B] locker round the corner
///   z 10 --+------- door -+------------------+      |         |
///     PASILLO   PASILLO SUR     [A] locker in sight           |
///   z 6    +=================================================+
///
///   A  tb_locker_vista      Pasillo Sur, south wall. Chase down a straight corridor: H-1, H-9.
///   B  tb_locker_vuelta     Pasillo Este, just past the corner: H-2 (break sight, then hide) and
///                           H-3 (WP_02 passes ~1.4 m from it).
///   C  tb_locker_callejon   Cuartito, a dead end off Pasillo Este: cornered, D34's warning.
///   D  tb_mesa_1, tb_mesa_2 Sala de mesas. WP_08 stops ~5 m in front of mesa 1: H-4.
///   E  tb_fila_1..3         Vestuario, three lockers side by side: does it open the right one
///                           (H-8)? WP_09 passes behind them with the wall in between: H-10.
///   F  tb_container         Depósito, with two crates to break sight. WP_05 stops ~5 m in front.
///   The inner rooms chain Pasillo Sur -> Sala de mesas -> Vestuario -> Depósito -> Pasillo Este,
///   and the three corridors ring them: every room has two ways out except the Cuartito.
///   Route_HidingLab walks all of it and is added to the testbed Nemesis's routes.
///
/// ROOMS. Each room and corridor has its own floor named "HL_&lt;ROOM&gt;_Floor", which is how
/// NemesisRooms keys rooms (the part before "_Floor").
///
/// THE SPOTS' OWN COLLIDERS. The testbed bakes Ground | Wall | Props, not Default, and a locker's
/// and the container's solid collider stay on Default by design (plan §14.4). So each spot gets a
/// Not Walkable volume the size of its solid bounds, on Props: the NavMesh around it ends up where
/// Zona1's (which bakes Default) does. The prefabs are never modified; ids are instance overrides.
///
/// WHAT IT TOUCHES OUTSIDE ITS OWN ROOT, all of it on purpose:
///   - Testbed/Geometry/PASILLO/Wall_E is DEACTIVATED, never deleted; three segments leaving the two
///     openings replace it under Hiding Lab. To take the lab out: delete "Hiding Lab", re-enable
///     Wall_E, drop the missing entry from the Nemesis's routes and bake again.
///   - The testbed Nemesis's NemesisController.routes gets Route_HidingLab appended.
///   - The testbed's NavMeshSurface is baked again (TestbedNavMeshBake: this scene alone).
///
/// Same rules as the Drop Lab's builder, which this follows: it never changes the active scene or
/// saves any scene other than NemesisTestbed, and re-running it rebuilds the lab from scratch. After
/// saving it runs Tools/Player/Validate Hiding Spots, whose report lands in the console next to this
/// one.
///
/// Also runnable without the editor open:
///   Unity.exe -batchmode -projectPath &lt;repo&gt; -executeMethod NemesisTestbedHidingLabBuilder.BuildFromCommandLine
///
/// TEMPORARY. Delete this file once the lab is built and NemesisTestbed is committed, like the Bug
/// Lab, Drop Lab and decoy builders were on 2026-09-29; it stays in git.
/// </summary>
public static class NemesisTestbedHidingLabBuilder
{
    private const string LogTag = "[NemesisHidingLab]";
    private const string MenuPath = "Tools/Nemesis/Build Hiding Lab (NemesisTestbed)";
    private const string HidingValidatorMenuPath = "Tools/Player/Validate Hiding Spots";
    private const string NavigationValidatorMenuPath = "Tools/Nemesis/Validate Navigation Setup";
    private const string ScenePath = "Assets/_Project/Scenes/Dev/NemesisTestbed.unity";

    private const string RootName = "Hiding Lab";
    private const string RouteName = "Route_HidingLab";
    private const string TestbedRootName = "Testbed";
    private const string PasilloWallPath = "Geometry/PASILLO/Wall_E";

    private const string TestbedMaterials = "Assets/_Project/Scenes/Dev/NemesisTestbedMaterials";
    private const string FloorMaterialPath = TestbedMaterials + "/testbed_floor.mat";
    private const string WallMaterialPath = TestbedMaterials + "/testbed_wall.mat";
    private const string PropMaterialPath = TestbedMaterials + "/testbed_prop.mat";
    private const string AccentMaterialPath = "Assets/_Project/Scenes/Dev/Materials/Blockout_Accent.mat";

    private const string SpotPrefabs = "Assets/_Project/Prefabs/HidingSpotFather";
    private const string LockerPrefabPath = SpotPrefabs + "/HidingSpot_Locker.prefab";
    private const string TablePrefabPath = SpotPrefabs + "/HidingSpot_UnderTable.prefab";
    private const string ContainerPrefabPath = SpotPrefabs + "/HidingSpot_Container.prefab";

    // ── Dimensions ──────────────────────────────────────────────────────────

    private const float WallThickness = 0.2f;
    private const float FloorThickness = 0.2f;

    /// <summary>The testbed's own rooms and corridors: 3.5 m.</summary>
    private const float WallHeight = 3.5f;

    /// <summary>PASILLO's east wall, which the lab's west side leans on.</summary>
    private const float WestX = 2f;
    private const float InnerSplitX = 11f;
    private const float InnerEastX = 20f;
    private const float EastX = 24f;
    private const float CuartitoEastX = 28f;

    private const float SouthZ = 6f;
    private const float InnerSouthZ = 10f;
    private const float MesasNorthZ = 19f;
    private const float InnerNorthZ = 30f;
    private const float NorthZ = 34f;
    private const float CuartitoSouthZ = 17f;
    private const float CuartitoNorthZ = 23f;

    // Openings: PASILLO's two are as wide as the corridors they lead into; the rooms' doors are 2 m.
    private static readonly Vector2 MesasDoorX = new Vector2(8f, 10f);          // in z 10
    private static readonly Vector2 VestuarioDoorX = new Vector2(8.5f, 10.5f);  // in z 19
    private static readonly Vector2 DepositoWestDoorZ = new Vector2(21f, 23f);  // in x 11
    private static readonly Vector2 DepositoEastDoorZ = new Vector2(24f, 26f);  // in x 20
    private static readonly Vector2 CuartitoDoorZ = new Vector2(19f, 21f);      // in x 24

    /// <summary>Between a spot's back and the wall it stands against.</summary>
    private const float WallGap = 0.03f;

    // ── Verification ────────────────────────────────────────────────────────

    /// <summary>The checks run on a private copy of the new NavMesh this far up, like the other
    /// labs': nothing in another open scene can reach it there.</summary>
    private static readonly Vector3 VerifyLift = new Vector3(0f, 500f, 0f);

    /// <summary>HidingSpotValidator's tolerance for an approach point on the NavMesh.</summary>
    private const float ApproachTolerance = 0.3f;

    /// <summary>In PASILLO, level with the south opening: the lab has to be reachable from there.</summary>
    private static readonly Vector3 PasilloProbe = new Vector3(0f, 0f, 8f);

    // ── Signs ───────────────────────────────────────────────────────────────

    private static readonly Color SignColor = new Color(1f, 0.85f, 0.4f);
    private const float SignFontSize = 2.2f;
    private const float MarkFontSize = 1.6f;

    private const string EntranceSign =
        "<b>-> ZONA DE ESCONDITES</b>\n<size=60%>Casos H-1..H-10 del checklist. E entra y sale, mantener F " +
        "aguanta la respiración. F9: fila 'escondite'. H-6: dejate capturar adentro -> al reaparecer, nada " +
        "pegado. H-7: 'escondite' se limpia al revisar vacío, al verte afuera o a los 15 s (8 s si sospechaba).</size>";

    private const string LockerASign =
        "<b>A · LOCKER A LA VISTA</b> (tb_locker_vista)\n<size=60%>H-1: que te persiga por el pasillo y metete " +
        "mientras te ve -> 'sabe (lo vio entrar)', va a la puerta y te saca a la ExitPose. H-9: adentro, WP_01 " +
        "pasa de frente a ~2 m -> 'lo distingue por las rendijas' -> sospecha -> te saca.</size>";

    private const string LockerBSign =
        "<b>B · LOCKER A LA VUELTA</b> (tb_locker_vuelta)\n<size=60%>H-2: doblá la esquina, cortale la vista y " +
        "metete a los >0.75 s -> 'escondite: —', barre y se va. H-3: quedate adentro; WP_02 pasa a ~1.4 m -> " +
        "'sabe (lo tiene encima)' sin Chasing.</size>";

    private const string CuartitoSign =
        "<b>C · CALLEJÓN</b> (tb_locker_callejon)\n<size=60%>Sin salida. Metete con él viéndote entrar al " +
        "cuartito: aviso de voz 'sabe tu escondite' (D34), va derecho a la puerta. Salí antes de que llegue: " +
        "no hay por dónde escaparse.</size>";

    private const string MesasSign =
        "<b>D · MESAS</b> (tb_mesa_1, tb_mesa_2)\n<size=60%>H-4: bajo la mesa 1, WP_08 para a ~5 m de frente -> " +
        "nada (bajo la mesa ve 3.5 m). A ≤3.5 m 'lo distingue' -> sospecha -> te saca. Bug: Chasing " +
        "directo desde la mesa.</size>";

    private const string VestuarioSign =
        "<b>E · FILA DE LOCKERS</b> (tb_fila_1..3)\n<size=60%>H-8: metete en uno con él de reojo -> sospecha " +
        "ESE, no el de al lado. H-10: WP_09 pasa del otro lado de la pared, a ~1.4 m de la fila -> nada. " +
        "Por delante, WP_07 a ~2.8 m.</size>";

    private const string DepositoSign =
        "<b>F · CONTAINER</b> (tb_container)\n<size=60%>H-9: nada salvo que pase a ≤1.5 m. WP_05 para a ~5 m " +
        "de frente. H-5: adentro, mantené F y soltala con él a ~4 m -> 'escucha un ruido' -> Investigating, " +
        "no Chasing. Las cajas cortan la vista para meterte sin que te vea.</size>";

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

        Outcome outcome = RunAndReport();
        if (outcome.Problems == 0) EditorApplication.ExecuteMenuItem(HidingValidatorMenuPath);
    }

    /// <summary>
    /// For -batchmode -executeMethod. Opens the testbed on its own, builds, runs the hiding-spot and
    /// navigation validators on the result, and exits: 0 when the lab was built, baked and saved, 1
    /// otherwise.
    /// </summary>
    public static void BuildFromCommandLine()
    {
        int exitCode = 1;
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Outcome outcome = RunAndReport();
            EditorApplication.ExecuteMenuItem(HidingValidatorMenuPath);
            EditorApplication.ExecuteMenuItem(NavigationValidatorMenuPath);
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
            Locker = AssetDatabase.LoadAssetAtPath<GameObject>(LockerPrefabPath),
            Table = AssetDatabase.LoadAssetAtPath<GameObject>(TablePrefabPath),
            Container = AssetDatabase.LoadAssetAtPath<GameObject>(ContainerPrefabPath),
        };

        if (assets.Cube == null)
        {
            log.AppendLine("ERROR: the built-in cube mesh is missing. Nothing was built.");
            return new Outcome { Problems = 1 };
        }

        if (assets.Locker == null || assets.Table == null || assets.Container == null)
        {
            log.AppendLine($"ERROR: a hiding spot prefab is missing under {SpotPrefabs}. Nothing was built.");
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
        public GameObject Locker;
        public GameObject Table;
        public GameObject Container;
    }

    /// <summary>A spot as placed: where its door is and which way it faces.</summary>
    private sealed class PlacedSpot
    {
        public HidingSpot Spot;
        public Vector3 Facing;
        public Vector3 Door;

        /// <summary>A floor point <paramref name="distance"/> metres out from the door.</summary>
        public Vector3 InFront(float distance) => Flat(Door + Facing * distance);
    }

    // ── The build ───────────────────────────────────────────────────────────

    private sealed class LabBuild
    {
        private readonly Scene scene;
        private readonly BuildAssets assets;
        private readonly StringBuilder log;

        private readonly HashSet<GameObject> created = new HashSet<GameObject>();
        private readonly Dictionary<string, PlacedSpot> spots = new Dictionary<string, PlacedSpot>();

        private readonly int groundLayer = LayerOrFallback("Ground", 3);
        private readonly int wallLayer = LayerOrFallback("Wall", 11);
        private readonly int propsLayer = LayerOrFallback("Props", 12);
        private readonly int defaultLayer = LayerOrFallback("Default", 0);

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
            BuildFloors(root);
            BuildWalls(root);
            BuildCover(root);
            BuildSpots(root);
            BuildMarks(root);
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
                log.AppendLine($"Saved {ScenePath}. The lab is reached from PASILLO through its east wall " +
                               $"(openings at z {SouthZ}-{InnerSouthZ} and z {InnerNorthZ}-{NorthZ}).");
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
                log.AppendLine("Removed the previous Hiding Lab.");
            }
        }

        // ── Connection ──────────────────────────────────────────────────────

        /// <summary>PASILLO's east wall is the lab's west wall: deactivated and rebuilt with an
        /// opening onto Pasillo Sur and another onto Pasillo Norte.</summary>
        private void BuildConnection(Transform root, GameObject testbed)
        {
            Transform group = Group("Connection (PASILLO east wall, two openings)", root);

            Transform original = testbed.transform.Find(PasilloWallPath);
            if (original == null)
            {
                log.AppendLine($"ERROR: {TestbedRootName}/{PasilloWallPath} not found. The lab is built but NOT " +
                               "connected to the testbed; reach it with the F10 teleports.");
                problems++;
                WallAlongZ(group, "West wall (stand-in for PASILLO Wall_E)", WestX, SouthZ, NorthZ,
                           new Vector2(SouthZ, InnerSouthZ), new Vector2(InnerNorthZ, NorthZ));
                return;
            }

            BoxCollider originalCollider = original.GetComponent<BoxCollider>();
            Bounds wall = originalCollider != null
                ? WorldBounds(originalCollider)
                : new Bounds(original.position, original.lossyScale);

            if (Mathf.Abs(wall.center.x - WestX) > 0.3f || wall.min.z > SouthZ || wall.max.z < NorthZ)
                log.AppendLine($"Warning: PASILLO's Wall_E is not where it used to be ({wall.center}, {wall.size}); " +
                               "the openings and the corridors may not line up.");

            // Deactivated, never deleted: re-enabling it is the whole undo.
            if (original.gameObject.activeSelf)
            {
                original.gameObject.SetActive(false);
                EditorUtility.SetDirty(original.gameObject);
                log.AppendLine($"Deactivated {TestbedRootName}/{PasilloWallPath}; three segments with two 4 m " +
                               "openings replace it under Hiding Lab.");
            }
            else
            {
                log.AppendLine($"{TestbedRootName}/{PasilloWallPath} was already inactive (previous build); left as it is.");
            }

            MeshRenderer originalRenderer = original.GetComponent<MeshRenderer>();
            Material material = originalRenderer != null && originalRenderer.sharedMaterial != null
                ? originalRenderer.sharedMaterial
                : assets.Wall;
            int layer = original.gameObject.layer;

            BoxFromTo(group, "PASILLO Wall_E (south of the Pasillo Sur opening)",
                      new Vector3(wall.min.x, wall.min.y, wall.min.z), new Vector3(wall.max.x, wall.max.y, SouthZ),
                      layer, material);
            BoxFromTo(group, "PASILLO Wall_E (between the openings)",
                      new Vector3(wall.min.x, wall.min.y, InnerSouthZ), new Vector3(wall.max.x, wall.max.y, InnerNorthZ),
                      layer, material);
            BoxFromTo(group, "PASILLO Wall_E (north of the Pasillo Norte opening)",
                      new Vector3(wall.min.x, wall.min.y, NorthZ), new Vector3(wall.max.x, wall.max.y, wall.max.z),
                      layer, material);
        }

        // ── Floors ──────────────────────────────────────────────────────────

        /// <summary>One floor per room, named so NemesisRooms keys each one apart.</summary>
        private void BuildFloors(Transform root)
        {
            Transform group = Group("Floors (one per room)", root);

            Floor(group, "HL_PASILLO_SUR", WestX, SouthZ, InnerEastX, InnerSouthZ);
            Floor(group, "HL_PASILLO_ESTE", InnerEastX, SouthZ, EastX, NorthZ);
            Floor(group, "HL_PASILLO_NORTE", WestX, InnerNorthZ, InnerEastX, NorthZ);
            Floor(group, "HL_SALA_MESAS", WestX, InnerSouthZ, InnerSplitX, MesasNorthZ);
            Floor(group, "HL_VESTUARIO", WestX, MesasNorthZ, InnerSplitX, InnerNorthZ);
            Floor(group, "HL_DEPOSITO", InnerSplitX, InnerSouthZ, InnerEastX, InnerNorthZ);
            Floor(group, "HL_CUARTITO", EastX, CuartitoSouthZ, CuartitoEastX, CuartitoNorthZ);
        }

        private void Floor(Transform parent, string room, float x0, float z0, float x1, float z1)
        {
            BoxFromTo(parent, room + "_Floor", new Vector3(x0, -FloorThickness, z0), new Vector3(x1, 0f, z1),
                      groundLayer, assets.Floor);
        }

        // ── Walls ───────────────────────────────────────────────────────────

        private void BuildWalls(Transform root)
        {
            Transform outer = Group("Walls (outer)", root);
            WallAlongX(outer, "Wall_S (Pasillo Sur)", SouthZ, WestX, EastX);
            WallAlongX(outer, "Wall_N (Pasillo Norte)", NorthZ, WestX, EastX);
            WallAlongZ(outer, "Wall_E (Pasillo Este)", EastX, SouthZ, NorthZ, CuartitoDoorZ);

            Transform cuartito = Group("Walls (Cuartito)", root);
            WallAlongX(cuartito, "Wall_S", CuartitoSouthZ, EastX, CuartitoEastX);
            WallAlongX(cuartito, "Wall_N", CuartitoNorthZ, EastX, CuartitoEastX);
            WallAlongZ(cuartito, "Wall_E", CuartitoEastX, CuartitoSouthZ, CuartitoNorthZ);

            Transform inner = Group("Walls (inner rooms)", root);
            WallAlongX(inner, "Pasillo Sur | rooms", InnerSouthZ, WestX, InnerEastX, MesasDoorX);
            WallAlongX(inner, "Pasillo Norte | rooms", InnerNorthZ, WestX, InnerEastX);
            WallAlongZ(inner, "Rooms | Pasillo Este", InnerEastX, InnerSouthZ, InnerNorthZ, DepositoEastDoorZ);
            WallAlongZ(inner, "Mesas + Vestuario | Deposito", InnerSplitX, InnerSouthZ, InnerNorthZ, DepositoWestDoorZ);
            WallAlongX(inner, "Mesas | Vestuario", MesasNorthZ, WestX, InnerSplitX, VestuarioDoorX);
        }

        /// <summary>A wall on the line z = <paramref name="z"/> from x0 to x1, leaving the given
        /// openings (x ranges). The ends reach half a thickness past x0 and x1 to close the corners.</summary>
        private void WallAlongX(Transform parent, string name, float z, float x0, float x1, params Vector2[] openings)
        {
            float half = WallThickness * 0.5f;
            List<Vector2> pieces = Pieces(x0 - half, x1 + half, openings);
            for (int i = 0; i < pieces.Count; i++)
            {
                BoxFromTo(parent, pieces.Count > 1 ? $"{name} ({i + 1})" : name,
                          new Vector3(pieces[i].x, 0f, z - half), new Vector3(pieces[i].y, WallHeight, z + half),
                          wallLayer, assets.Wall);
            }
        }

        /// <summary>The same along x = <paramref name="x"/>, from z0 to z1.</summary>
        private void WallAlongZ(Transform parent, string name, float x, float z0, float z1, params Vector2[] openings)
        {
            float half = WallThickness * 0.5f;
            List<Vector2> pieces = Pieces(z0 - half, z1 + half, openings);
            for (int i = 0; i < pieces.Count; i++)
            {
                BoxFromTo(parent, pieces.Count > 1 ? $"{name} ({i + 1})" : name,
                          new Vector3(x - half, 0f, pieces[i].x), new Vector3(x + half, WallHeight, pieces[i].y),
                          wallLayer, assets.Wall);
            }
        }

        private static List<Vector2> Pieces(float from, float to, Vector2[] openings)
        {
            var sorted = new List<Vector2>(openings);
            sorted.Sort((a, b) => a.x.CompareTo(b.x));

            var pieces = new List<Vector2>();
            float cursor = from;
            foreach (Vector2 opening in sorted)
            {
                if (opening.x > cursor) pieces.Add(new Vector2(cursor, opening.x));
                cursor = Mathf.Max(cursor, opening.y);
            }
            if (to > cursor) pieces.Add(new Vector2(cursor, to));
            return pieces;
        }

        // ── Cover ───────────────────────────────────────────────────────────

        /// <summary>Two crates in the Depósito, waist-high, on Props so they are baked: something to
        /// break its sight behind before climbing into the container.</summary>
        private void BuildCover(Transform root)
        {
            Transform group = Group("Cover (Deposito crates)", root);
            Box(group, "Crate (1.2 m)", new Vector3(13f, 0.6f, 13.5f), Vector3.one * 1.2f, propsLayer, assets.Prop);
            Box(group, "Crate (1.2 m) (1)", new Vector3(18.2f, 0.6f, 16f), Vector3.one * 1.2f, propsLayer, assets.Prop);
        }

        // ── Spots ───────────────────────────────────────────────────────────

        private void BuildSpots(Transform root)
        {
            Transform group = Group("Hiding Spots", root);
            Transform blockers = Group("NavMesh Blockers (the spots' solid bounds; Default is not baked here)", root);
            float half = WallThickness * 0.5f;

            // A: against Pasillo Sur's south wall, facing up the corridor's width.
            Spot(group, blockers, assets.Locker, "tb_locker_vista", "A - Locker a la vista",
                 new Vector3(13f, 0f, SouthZ + half), Vector3.forward);

            // B: on Pasillo Este's east wall, 2.5 m past the corner.
            Spot(group, blockers, assets.Locker, "tb_locker_vuelta", "B - Locker a la vuelta",
                 new Vector3(EastX - half, 0f, 12.5f), Vector3.left);

            // C: at the back of the Cuartito, facing its door.
            Spot(group, blockers, assets.Locker, "tb_locker_callejon", "C - Locker del callejon",
                 new Vector3(CuartitoEastX - half, 0f, (CuartitoDoorZ.x + CuartitoDoorZ.y) * 0.5f), Vector3.left);

            // D: mesa 1 against the west wall (facing the room's length), mesa 2 against the north one.
            Spot(group, blockers, assets.Table, "tb_mesa_1", "D - Mesa 1",
                 new Vector3(WestX + half, 0f, 14.5f), Vector3.right);
            Spot(group, blockers, assets.Table, "tb_mesa_2", "D - Mesa 2",
                 new Vector3(4.5f, 0f, MesasNorthZ - half), Vector3.back);

            // E: three lockers side by side on the Vestuario's north wall; Pasillo Norte behind it.
            for (int i = 0; i < 3; i++)
            {
                Spot(group, blockers, assets.Locker, $"tb_fila_{i + 1}", $"E - Fila {i + 1}",
                     new Vector3(4.2f + 1.4f * i, 0f, InnerNorthZ - half), Vector3.back);
            }

            // F: the container's back against the Depósito's north wall, its door facing the room.
            Spot(group, blockers, assets.Container, "tb_container", "F - Container",
                 new Vector3(15.5f, 0f, InnerNorthZ - half), Vector3.back);
        }

        /// <summary>
        /// Instances <paramref name="prefab"/> facing <paramref name="facing"/>, with its back
        /// against the wall face at <paramref name="wallFace"/>: how far its back reaches is measured
        /// on the instance, so the layout does not depend on each model's size.
        /// </summary>
        private void Spot(Transform parent, Transform blockers, GameObject prefab, string id, string label,
                          Vector3 wallFace, Vector3 facing)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            created.Add(instance);
            if (instance.scene != scene) SceneManager.MoveGameObjectToScene(instance, scene);
            instance.transform.SetParent(parent, false);
            instance.name = $"{prefab.name} ({id}) - {label}";
            instance.transform.SetPositionAndRotation(wallFace, Quaternion.LookRotation(facing, Vector3.up));
            Physics.SyncTransforms();

            if (!SolidBounds(instance, out Bounds bounds))
            {
                log.AppendLine($"ERROR: {instance.name} has no renderer or solid collider to measure; left at the wall line.");
                problems++;
            }
            else
            {
                float back = Reach(bounds, wallFace, -facing);
                instance.transform.position = wallFace + facing * (back + WallGap);
                Physics.SyncTransforms();
                SolidBounds(instance, out bounds);
            }

            HidingSpot spot = instance.GetComponent<HidingSpot>();
            if (spot == null)
            {
                log.AppendLine($"ERROR: {prefab.name} has no HidingSpot on its root.");
                problems++;
                return;
            }

            var serialized = new SerializedObject(spot);
            serialized.FindProperty("spotId").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);

            Vector3 position = instance.transform.position;
            var placed = new PlacedSpot
            {
                Spot = spot,
                Facing = facing,
                Door = position + facing * Reach(bounds, position, facing),
            };
            spots[id] = placed;

            Blocker(blockers, instance.name, bounds);
            log.AppendLine($"{id}: {prefab.name} at {Format(position)}, facing {Format(facing)}, door at " +
                           $"{Format(placed.Door)}, {bounds.size.x:0.0} x {bounds.size.y:0.0} x {bounds.size.z:0.0} m.");
        }

        /// <summary>Not Walkable over a spot's whole solid bounds, on Props so the bake reads it.
        /// Also keeps a table's top from becoming a NavMesh island.</summary>
        private void Blocker(Transform parent, string spotName, Bounds bounds)
        {
            GameObject blocker = Create($"NavMesh Blocker ({spotName})", parent, typeof(NavMeshModifierVolume));
            blocker.layer = propsLayer;
            float bottom = -0.1f;
            float top = bounds.max.y + 0.1f;
            blocker.transform.position = new Vector3(bounds.center.x, (bottom + top) * 0.5f, bounds.center.z);

            NavMeshModifierVolume volume = blocker.GetComponent<NavMeshModifierVolume>();
            volume.center = Vector3.zero;
            volume.size = new Vector3(bounds.size.x, top - bottom, bounds.size.z);
            volume.area = NotWalkableArea();
        }

        /// <summary>The spot's renderers and non-trigger colliders together: what the player sees
        /// and what blocks. Triggers (the crosshair's) and the interior camera do not count.</summary>
        private static bool SolidBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer) continue;
                if (any) bounds.Encapsulate(renderer.bounds);
                else bounds = renderer.bounds;
                any = true;
            }

            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled || collider.isTrigger) continue;
                if (any) bounds.Encapsulate(collider.bounds);
                else bounds = collider.bounds;
                any = true;
            }

            return any;
        }

        /// <summary>How far <paramref name="bounds"/> reaches from <paramref name="origin"/> along
        /// <paramref name="direction"/>.</summary>
        private static float Reach(Bounds bounds, Vector3 origin, Vector3 direction)
        {
            float reach = float.MinValue;
            Vector3 c = bounds.center, e = bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                reach = Mathf.Max(reach, Vector3.Dot(corner - origin, direction));
            }
            return reach;
        }

        // ── Distance marks ──────────────────────────────────────────────────

        /// <summary>Floor stripes every metre out from the doors the checklist measures from, like
        /// the Hiding Test Area's: to read "~5 m in front" and "≤3.5 m" off the floor.</summary>
        private void BuildMarks(Transform root)
        {
            Transform group = Group("Distance Marks", root);
            Marks(group, "tb_locker_vista", 3);
            Marks(group, "tb_mesa_1", 6);
            Marks(group, "tb_fila_2", 4);
            Marks(group, "tb_container", 6);
        }

        private void Marks(Transform parent, string id, int count)
        {
            if (!spots.TryGetValue(id, out PlacedSpot placed)) return;

            Transform group = Group($"Marks ({id})", parent);
            Vector3 side = Vector3.Cross(Vector3.up, placed.Facing);
            for (int metre = 1; metre <= count; metre++)
            {
                Vector3 at = placed.InFront(metre);
                GameObject stripe = Box(group, $"Mark {metre} m", at + Vector3.up * 0.005f, Vector3.one, defaultLayer,
                                        assets.Accent, solid: false);
                stripe.transform.rotation = Quaternion.LookRotation(placed.Facing, Vector3.up);
                stripe.transform.localScale = new Vector3(1f, 0.01f, 0.06f);

                GameObject labelObject = Create($"Label {metre} m", group, typeof(TextMeshPro));
                labelObject.transform.position = at + side * 0.75f + Vector3.up * 0.01f;
                labelObject.transform.rotation = Quaternion.LookRotation(Vector3.down, placed.Facing);

                TextMeshPro label = labelObject.GetComponent<TextMeshPro>();
                label.text = $"{metre} m";
                label.fontSize = MarkFontSize;
                label.alignment = TextAlignmentOptions.Center;
                label.color = SignColor;
                label.rectTransform.sizeDelta = new Vector2(0.8f, 0.3f);
            }
        }

        // ── Signs ───────────────────────────────────────────────────────────

        private void BuildSigns(Transform root)
        {
            Transform group = Group("Signs", root);
            float face = WallThickness * 0.5f + 0.02f;

            // In PASILLO, on its east wall between the openings, read from the corridor.
            Sign(group, "Sign (entrance)", EntranceSign, new Vector3(WestX - face, 2.2f, 12.6f),
                 Vector3.left, new Vector2(4.4f, 1.6f));

            Sign(group, "Sign (A)", LockerASign, new Vector3(16.8f, 2.2f, SouthZ + face),
                 Vector3.forward, new Vector2(4.8f, 1.5f));
            // At the end of Pasillo Sur, so it is read walking up to the corner.
            Sign(group, "Sign (B)", LockerBSign, new Vector3(EastX - face, 2.2f, 8f),
                 Vector3.left, new Vector2(3.6f, 1.6f));
            Sign(group, "Sign (C)", CuartitoSign, new Vector3(26f, 2.3f, CuartitoSouthZ + face),
                 Vector3.forward, new Vector2(3.6f, 1.4f));
            Sign(group, "Sign (D)", MesasSign, new Vector3(InnerSplitX - face, 2.2f, 14.5f),
                 Vector3.left, new Vector2(4.2f, 1.5f));
            Sign(group, "Sign (E)", VestuarioSign, new Vector3(5f, 2.3f, MesasNorthZ + face),
                 Vector3.forward, new Vector2(4.8f, 1.5f));
            Sign(group, "Sign (F)", DepositoSign, new Vector3(InnerEastX - face, 2.2f, 18.5f),
                 Vector3.left, new Vector2(4.4f, 1.7f));
        }

        // ── Route ───────────────────────────────────────────────────────────

        /// <summary>
        /// Every station in turn, with the stops the checklist measures from: ~2 m in front of A,
        /// ~1.4 m from B, ~5 m in front of mesa 1 and of the container, and behind the row of lockers
        /// with the wall in between. From WP_09 it closes the loop through PASILLO.
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

            float rowX = spots.TryGetValue("tb_fila_2", out PlacedSpot row) ? row.Door.x : 5.6f;

            var points = new List<KeyValuePair<string, Vector3>>
            {
                Point("WP_00 Pasillo Sur, west end", new Vector3(4f, 0f, 8.8f)),
                Point("WP_01 Pasillo Sur, ~2 m in front of A", new Vector3(17f, 0f, 8.8f)),
                Point("WP_02 Pasillo Este, ~1.4 m from B", InFrontOr("tb_locker_vuelta", 1.1f, new Vector3(22.3f, 0f, 12.5f))),
                Point("WP_03 Cuartito (dead end)", InFrontOr("tb_locker_callejon", 1.8f, new Vector3(25.5f, 0f, 20f))),
                Point("WP_04 Pasillo Este, Deposito door", new Vector3(22f, 0f, 25f)),
                Point("WP_05 Deposito, ~5 m in front of the container", InFrontOr("tb_container", 5f, new Vector3(15.5f, 0f, 18f))),
                Point("WP_06 Deposito, Vestuario door", new Vector3(12.5f, 0f, 22f)),
                Point("WP_07 Vestuario, ~2.8 m in front of the row", InFrontOr("tb_fila_2", 2.8f, new Vector3(5.6f, 0f, 26.5f))),
                Point("WP_08 Sala de mesas, ~5 m in front of mesa 1", InFrontOr("tb_mesa_1", 5f, new Vector3(8.5f, 0f, 14.5f))),
                Point("WP_09 Pasillo Norte, behind the row (wall between)", new Vector3(rowX, 0f, InnerNorthZ + 1f)),
            };

            foreach (KeyValuePair<string, Vector3> point in points)
            {
                GameObject waypoint = Create(point.Key, routeObject.transform);
                waypoint.transform.position = point.Value;
                if (tagExists) waypoint.tag = NemesisRoute.WaypointTag;
                waypoints.Add(waypoint.transform);
            }

            return route;
        }

        private static KeyValuePair<string, Vector3> Point(string name, Vector3 position) =>
            new KeyValuePair<string, Vector3>(name, position);

        private Vector3 InFrontOr(string id, float distance, Vector3 fallback) =>
            spots.TryGetValue(id, out PlacedSpot placed) ? placed.InFront(distance) : fallback;

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
                EditorUtility.DisplayProgressBar("Hiding Lab", $"Baking {surface.name} (NemesisTestbed only)...", 0.5f);
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
        /// what was just baked, so it measures the baked data alone.</summary>
        private void Verify(List<NemesisController> controllers)
        {
            var copies = new List<NavMeshDataInstance>();
            foreach (NavMeshSurface surface in baked)
                copies.Add(NavMesh.AddNavMeshData(surface.navMeshData, surface.transform.position + VerifyLift,
                                                  surface.transform.rotation));

            try
            {
                log.AppendLine("NavMesh checks (on the baked data alone):");
                ExpectComplete("PASILLO -> Pasillo Sur", PasilloProbe, waypoints[0].position, 1f);
                foreach (PlacedSpot placed in spots.Values) VerifySpot(placed);
                VerifyRoute(controllers);
            }
            finally
            {
                foreach (NavMeshDataInstance copy in copies) copy.Remove();
            }
        }

        /// <summary>What HidingSpotValidator asks of the approach point, on the new bake, plus the
        /// walk to it from the lab's entrance.</summary>
        private void VerifySpot(PlacedSpot placed)
        {
            HidingSpot spot = placed.Spot;
            Transform approach = new SerializedObject(spot).FindProperty("approachPoint").objectReferenceValue as Transform;
            if (approach == null)
            {
                Fail($"{spot.SpotId}: no ApproachPoint assigned.");
                return;
            }

            Vector3 point = approach.position;
            if (!SampleCopy(point, ApproachTolerance, out NavMeshHit _))
            {
                Fail($"{spot.SpotId}: ApproachPoint {Format(point)} is not on the NavMesh within {ApproachTolerance} m.");
                return;
            }

            Pass($"{spot.SpotId}: ApproachPoint on the NavMesh.");
            ExpectComplete($"{spot.SpotId}: walk from WP_00 to its ApproachPoint", waypoints[0].position, point, ApproachTolerance);
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
                               controller.transform.position, waypoints[0].position, NemesisNav.DefaultSampleRadius);
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

        /// <summary>A cube of the given size centred at <paramref name="localCenter"/>, collider on
        /// unless <paramref name="solid"/> is off.</summary>
        private GameObject Box(Transform parent, string name, Vector3 localCenter, Vector3 size, int layer,
                               Material material, bool solid = true)
        {
            var components = new List<Type> { typeof(MeshFilter), typeof(MeshRenderer) };
            if (solid) components.Add(typeof(BoxCollider));

            GameObject box = Create(name, parent, components.ToArray());
            box.layer = layer;
            box.transform.localPosition = localCenter;
            box.transform.localRotation = Quaternion.identity;
            box.transform.localScale = size;

            box.GetComponent<MeshFilter>().sharedMesh = assets.Cube;
            if (material != null) box.GetComponent<MeshRenderer>().sharedMaterial = material;
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

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

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
