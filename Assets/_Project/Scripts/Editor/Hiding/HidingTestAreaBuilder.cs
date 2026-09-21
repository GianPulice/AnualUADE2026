using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.AI.Navigation;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the hiding-spot test environment in the TestIñaki dev scene: the HidingSpot prefab family
/// (a Father and three variants) and a walled area laid out for the hiding cases of
/// docs/Plan-IA-Stalker.md §13 (1 to 5) and the phase 1 checks of §9 — how far the breathing
/// carries, and the emitter being left as it was on the way out.
///
/// THE AREA ("Hiding Test Area", rebuilt from scratch on every run):
///   - A corridor from the door to a corner, and a hall past it. Locker A stands in the corridor, in
///     plain view of anything chasing you down it (case 1). Locker B stands past the corner, behind
///     the divider, where the Nemesis cannot see it until it has rounded the corner too (case 2).
///     The table and the container stand in the hall with open floor in front of them.
///   - A mark on the floor every metre in front of each spot, measured from where the hidden player
///     ends up, so "it is 5 m away" (case 4) and "4 m away" (case 5) are read off the floor.
///   - A Nemesis of its own, active from the first frame, with F10 added to the F9 it already has.
///     Its route runs along the front of every spot, over the approach point, which takes it to
///     about a metre from the player hidden inside (case 3), and has it stop about 5 m in front of
///     the table and 4 m in front of the container.
///   - A checkpoint at the door, so a capture puts the player back in the corridor instead of at
///     the scene's spawn, 40 m away.
///   - A NavMesh baked from the area alone and saved next to the scene.
///
/// WHAT IT NEVER TOUCHES. The active scene: the editor may have Zona1 open with somebody's unsaved
/// work in it, so TestIñaki is opened additively when it is not loaded, saved, and closed again, and
/// the prefabs are built in preview scenes. The models it uses (Locker, CargoContainer, Nemesis,
/// Player) are nested or read, never modified.
///
/// Re-running is safe: the area is deleted and rebuilt, and the four prefabs are updated in place —
/// same GUIDs and file IDs, so instances placed anywhere else keep their references and overrides.
/// Hand edits to those four prefabs ARE overwritten, though: tune the numbers below, or stop running
/// this once the prefabs have been tuned by hand.
/// </summary>
public static class HidingTestAreaBuilder
{
    private const string LogTag = "[HidingTestAreaBuilder]";

    // ── Paths ───────────────────────────────────────────────────────────────

    // The scene name carries an ñ; spelled as an escape so no editor's encoding can break the path.
    private const string SceneName = "TestI\u00F1aki";
    private const string DevScenesFolder = "Assets/_Project/Scenes/Dev";
    private const string ScenePath = DevScenesFolder + "/" + SceneName + ".unity";
    private const string MenuPath = "Tools/Hiding/Build Test Area (" + SceneName + ")";

    // Where NavMeshAssetManager would put it: a folder named after the scene, next to the scene.
    private const string NavMeshFolder = DevScenesFolder + "/" + SceneName;
    private const string NavMeshAssetPath = NavMeshFolder + "/NavMesh-HidingTestArea.asset";

    private const string PrefabsFolder = "Assets/_Project/Prefabs";
    private const string SpotFolderName = "HidingSpotFather";
    private const string SpotFolder = PrefabsFolder + "/" + SpotFolderName;
    private const string FatherPath = SpotFolder + "/HidingSpot.prefab";

    private const string LockerModelPath = PrefabsFolder + "/Environment/Locker.prefab";
    private const string ContainerModelPath = PrefabsFolder + "/Environment/CargoContainer.prefab";
    private const string PlayerPrefabPath = PrefabsFolder + "/Player.prefab";
    private const string NemesisPrefabPath = PrefabsFolder + "/Nemesis.prefab";
    private const string HidingDataPath = "Assets/_Project/ScriptableObjects/Hiding/SO_HidingData.asset";
    private const string HighlightProfilePath = "Assets/_Project/ScriptableObjects/Highlight/SO_Highlight_Interactables.asset";

    private const string FloorMaterialPath = DevScenesFolder + "/Materials/Blockout_Floor.mat";
    private const string WallMaterialPath = DevScenesFolder + "/Materials/Blockout_Wall.mat";
    private const string MarkMaterialPath = DevScenesFolder + "/Materials/Blockout_Accent.mat";
    // PSXIndustrial, which the crosshair highlight can light up — unlike the URP/Lit blockouts.
    private const string TableMaterialPath = "Assets/_Project/Art/Materials/Environment/New FBXs/Materials/M_MetalOscuro.mat";

    // Fallback for the look action when Player.prefab's rig cannot be read. It is the same asset the
    // rig itself points at.
    private const string DefaultInputActionsPath = "Packages/com.unity.cinemachine/Runtime/Input/CinemachineDefaultInputActions.inputactions";
    private const string DefaultLookActionName = "CM Default/Look";

    // ── Names ───────────────────────────────────────────────────────────────

    private const string AreaRootName = "Hiding Test Area";
    private const string ModelName = "Model";
    private const string InteriorPoseName = "InteriorPose";
    private const string ApproachPointName = "ApproachPoint";
    private const string ExitPoseName = "ExitPose";
    private const string InteriorCameraName = "Interior Camera";

    // The name CinemachinePanTilt gives its tilt axis. The input controllers are matched by it.
    private const string TiltAxisName = "Look Y (Tilt)";

    // ── Spots ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Everything that differs between the three kinds of spot, in spot-local metres. The root sits
    /// on the floor and +Z is the open side: where the player looks out, and where the Nemesis walks
    /// up from. Tilt is in Cinemachine's sign, positive looking DOWN.
    /// </summary>
    private sealed class SpotSpec
    {
        public string Path;
        public EHidingSpotType SpotType;
        public Vector3 InteriorPose;
        public Vector3 ApproachPoint;
        public Vector3 ExitPose;
        public Vector3 CameraPosition;
        public Vector2 PanRange;
        public Vector2 TiltRange;
        public Vector3 TriggerCenter;
        public Vector3 TriggerSize;
    }

    // Locker: 1 m wide, 0.4 m deep, 1.9 m tall, door on +Z at z = 0.2.
    //  - The player stands in the middle. The 0.3 m capsule is wider than the locker is deep, so the
    //    body pokes 0.1 m out front and back; it is kinematic in there and the camera sits in front
    //    of the face, so the player never sees it.
    //  - The NavMesh stops ~0.5 m (the agent radius) out from the door. The approach point goes
    //    0.6 m out, which keeps it 0.8 m from the player, inside the 1 m catch reach.
    //  - Camera just behind the door, at eye height.
    private static readonly SpotSpec LockerSpec = new SpotSpec
    {
        Path = SpotFolder + "/HidingSpot_Locker.prefab",
        SpotType = EHidingSpotType.Locker,
        InteriorPose = new Vector3(0f, 0f, 0f),
        ApproachPoint = new Vector3(0f, 0f, 0.8f),
        ExitPose = new Vector3(0f, 0f, 0.7f),
        CameraPosition = new Vector3(0f, 1.62f, 0.15f),
        PanRange = new Vector2(-15f, 15f),
        TiltRange = new Vector2(-10f, 10f),
        TriggerCenter = new Vector3(0f, 0.95f, 0f),
        TriggerSize = new Vector3(1.1f, 1.95f, 0.5f),
    };

    // Under the table: a 1.8 x 0.9 m top at 0.9 m, front edge at z = 0.45.
    //  - The NavMesh stops ~0.5 m past that edge, so the approach point cannot be closer than ~0.95 m
    //    to the middle of the table. The player is pulled forward to z = 0.05 to keep the gap at 0.9.
    //  - Camera low (0.6 m) and just inside the front edge, ahead of the body.
    //  - Tilt: the spec's "-5..+15" counts UP as positive — under a table there is nothing to look
    //    down at, and a 2 m tall monster 5 m away is ~15 degrees up from 0.6 m. Cinemachine counts
    //    down as positive, hence (-15, 5).
    private static readonly SpotSpec TableSpec = new SpotSpec
    {
        Path = SpotFolder + "/HidingSpot_UnderTable.prefab",
        SpotType = EHidingSpotType.UnderTable,
        InteriorPose = new Vector3(0f, 0f, 0.05f),
        ApproachPoint = new Vector3(0f, 0f, 0.95f),
        ExitPose = new Vector3(0f, 0f, 1.15f),
        CameraPosition = new Vector3(0f, 0.6f, 0.38f),
        PanRange = new Vector2(-45f, 45f),
        TiltRange = new Vector2(-15f, 5f),
        TriggerCenter = new Vector3(0f, 0.45f, 0f),
        TriggerSize = new Vector3(1.9f, 0.95f, 1f),
    };

    // Container: doors on +Z at z = 0, the 6.5 m body running back along -Z.
    //  - The player stands 0.35 m inside the doors and the approach point 0.55 m outside them.
    //  - Camera just inside the doors, at eye height.
    private static readonly SpotSpec ContainerSpec = new SpotSpec
    {
        Path = SpotFolder + "/HidingSpot_Container.prefab",
        SpotType = EHidingSpotType.Container,
        InteriorPose = new Vector3(0f, 0f, -0.35f),
        ApproachPoint = new Vector3(0f, 0f, 0.55f),
        ExitPose = new Vector3(0f, 0f, 0.8f),
        CameraPosition = new Vector3(0f, 1.62f, -0.1f),
        PanRange = new Vector2(-10f, 10f),
        TiltRange = new Vector2(-10f, 10f),
        TriggerCenter = new Vector3(0f, 1.25f, -ContainerLength * 0.5f),
        TriggerSize = new Vector3(2.15f, 2.6f, 6.65f),
    };

    // The Locker mesh is a plain box with the door painted on its FBX -Y face (checked against the
    // UVs on T_Industrial2). Under the prefab's own -90 degrees on X, that face looks down +Z — the
    // spot's front — so the model goes in with that same rotation and nothing else. The box is also
    // 2.26 cm off centre in X (the prefab's own collider shows it), which the offset takes out.
    private static readonly Vector3 LockerModelOffset = new Vector3(0.0226f, 0f, 0f);
    private static readonly Quaternion ModelRotation = Quaternion.Euler(-90f, 0f, 0f);
    private const float LockerDepth = 0.4f;

    // CargoContainer rather than "Container Cerrado" (8 x 3 x 3 m): closer to a real 20 ft box, which
    // this one is — centred in X and along its length, pivot on the floor, doors at both ends. It
    // ships without a collider, so the variant brings its own.
    private const float ContainerWidth = 2.025f;
    private const float ContainerHeight = 2.475f;
    private const float ContainerLength = 6.5f;

    // A work table built from boxes: there is no table prefab to nest (CoffeTable/ only holds a
    // cafeteria table model with its benches attached and no collider).
    private const float TableLength = 1.8f;
    private const float TableDepth = 0.9f;
    private const float TableHeight = 0.9f;
    private const float TableTopThickness = 0.05f;
    private const float TableLegSize = 0.06f;

    // Recentering, and its real job is the RESET. The input controller keeps turning a spot's axes
    // whenever the mouse moves, even while that spot's camera is switched off — it has no idea the
    // camera is not live — and Cinemachine only snaps an axis back to centre when the camera goes
    // live if recentering is on. Without it every entry would start looking wherever the mouse last
    // pushed the clamp. The slow settle back to centre after RecenterWait seconds without input is
    // the side effect; 4 s keeps it out of the way of someone watching the monster.
    private const float RecenterWait = 4f;
    private const float RecenterTime = 1f;

    // ── Area layout ─────────────────────────────────────────────────────────
    //
    // Area-local metres. The root sits at AreaOrigin, y = 0 is the top of the area's own floor.
    // Across, one character is about half a metre; up and down it is not to scale.
    //
    //   z = +10  +----------------------------------------+
    //            |                            +---+       |
    //            |  HALL                      | D |       |   D  container, doors facing south
    //            |      C >                   +-v-+       |   C  table, open side facing east
    //            |                                        |
    //            |              B ^                       |   B  locker, past the corner
    //   z = -5.4 |       +--------------------------------+   divider
    //            | corner       CORRIDOR          A ^         door: the east wall is open here
    //   z = -10  +----------------------------------------+   A  locker, in sight
    //          x = -10                                  x = +10

    // Surveyed in TestIñaki: the top of the big floor Cube. The free block is x -22..-2, z -6..14,
    // and the player starts at (30, 2.6, 10.7), east of it — hence the door on the east side.
    private const float SceneFloorTop = 2.56f;
    // Thin enough to step onto without noticing, thick enough not to z-fight the Cube underneath.
    private const float FloorThickness = 0.02f;
    private static readonly Vector3 AreaOrigin = new Vector3(-12f, SceneFloorTop + FloorThickness, 4f);

    private const float HalfSize = 10f;
    // Higher than the orbit camera ever gets (~3.7 m), so the player cannot peek over (cheese C8).
    private const float WallHeight = 4f;
    private const float WallThickness = 0.3f;
    private const float InnerFace = HalfSize - WallThickness;
    private const float CorridorNorthFace = -5.7f;
    private const float CorridorWidth = CorridorNorthFace + InnerFace;
    private const float DividerNorthFace = CorridorNorthFace + WallThickness;
    // The divider stops here; west of it the corridor turns into the hall — the corner.
    private const float CornerEdgeX = -6.5f;
    // Gap left between a locker's back and the wall behind it.
    private const float BackGap = 0.02f;

    private sealed class StationSpec
    {
        public string Name;
        public string SpotId;
        public SpotSpec Spot;
        public Vector3 Position;
        public float Yaw;
        public string Sign;
        public float SignHeight;
        public int DistanceMarks;
    }

    // At least 4 m between any two, and in this order: the route below indexes them.
    private static readonly StationSpec[] Stations =
    {
        new StationSpec
        {
            Name = "A - Locker in sight", SpotId = "test_locker_a", Spot = LockerSpec,
            // Back to the south wall, door to the corridor, 6 m in from the door.
            Position = new Vector3(3f, 0f, -InnerFace + BackGap + LockerDepth * 0.5f), Yaw = 0f,
            Sign = "A  LOCKER IN SIGHT\n<size=70%>1: get in while it chases you\n3: it walks past a metre away</size>",
            SignHeight = 2.4f, DistanceMarks = 3,
        },
        new StationSpec
        {
            Name = "B - Locker round the corner", SpotId = "test_locker_b", Spot = LockerSpec,
            // Back to the divider, facing the hall. 3.5 m east of the divider's end: far enough that
            // the divider still hides it from a Nemesis standing in the mouth of the corner.
            Position = new Vector3(-3f, 0f, DividerNorthFace + BackGap + LockerDepth * 0.5f), Yaw = 0f,
            Sign = "B  LOCKER ROUND THE CORNER\n<size=70%>2: turn the corner and get in before it rounds it</size>",
            SignHeight = 2.4f, DistanceMarks = 5,
        },
        new StationSpec
        {
            Name = "C - Under the table", SpotId = "test_table", Spot = TableSpec,
            // Open side to the east, with the whole hall in front of it.
            Position = new Vector3(-6.5f, 0f, 4.5f), Yaw = 90f,
            Sign = "C  UNDER THE TABLE\n<size=70%>4: it stops about 5 m in front and stares</size>",
            SignHeight = 1.9f, DistanceMarks = 6,
        },
        new StationSpec
        {
            Name = "D - Container", SpotId = "test_container", Spot = ContainerSpec,
            // Doors to the south, body running north to 0.2 m short of the north wall.
            Position = new Vector3(6f, 0f, 3f), Yaw = 180f,
            Sign = "D  CONTAINER\n<size=70%>breathing and exhale are muffled in here</size>",
            SignHeight = 3f, DistanceMarks = 5,
        },
    };

    private const string EntranceSign =
        "HIDING SPOTS\n<size=55%>F9 debug HUD   |   F10 Nemesis console\n" +
        "E hide / get out   |   hold F: hold your breath\n" +
        "5: hidden, let go of F with it 4 m away</size>";

    // Route anchors between the stations, area-local.
    private static readonly Vector3 CorridorEnd = new Vector3(-8.1f, 0f, -7.7f);
    private static readonly Vector3 RoundTheCorner = new Vector3(-8.1f, 0f, -3.8f);
    private static readonly Vector3 HallEast = new Vector3(2.5f, 0f, 4.5f);
    private static readonly Vector3 HallSouth = new Vector3(6f, 0f, -3.5f);

    // The route passes each spot from PassSide metres to one side of the approach point to PassSide
    // to the other, PassOut further out: a line over the approach point, on the NavMesh. PassSide is
    // kept short enough that the two ends are each other's nearest waypoints, nearer than the stops
    // in front of the table and the container — under d / sqrt(3), d being how far those stops are
    // from the pass line (2 m for the container). With cluster patrol on, the sweep is a
    // nearest-neighbour chain, and this is what keeps it walking from one end to the other.
    private const float PassSide = 1.1f;
    private const float PassOut = 0.1f;

    // The patrol counts a waypoint as reached 1 m short of it (SO_NemesisMovement.StoppingDistance)
    // and stops there, so these sit a metre closer than where it should end up: ~5 m and ~4 m from
    // the hidden player.
    private const float TableStareStop = 4f;
    private const float ContainerStareStop = 3f;

    // Where the Nemesis goes back to after a capture: the far end of the hall, away from the door
    // the checkpoint puts the player back at.
    private static readonly Vector3[] SpawnPoints =
    {
        new Vector3(-8f, 0f, 8.3f),
        new Vector3(0f, 0f, 8.3f),
    };
    private static readonly Vector3 NemesisStart = new Vector3(0f, 0f, 7f);
    private const float NemesisStartYaw = 180f;

    // Just inside the door, as wide as the corridor. The player respawns 2 m further in, facing it.
    private static readonly Vector3 CheckpointPosition = new Vector3(9f, 0f, -7.7f);

    // Floor marks and signs.
    private const float MarkLength = 1f;
    private const float MarkWidth = 0.05f;
    private const float MarkThickness = 0.01f;
    private const float MarkLabelSize = 2.5f;
    private const float StationSignSize = 2.4f;
    private const float EntranceSignSize = 4f;
    private static readonly Color SignColor = new Color(1f, 0.85f, 0.4f);

    // Same tolerance as HidingSpotValidator for approach points; waypoints get a little more.
    private const float ApproachTolerance = 0.3f;
    private const float WaypointTolerance = 0.5f;

    // ── Layers ──────────────────────────────────────────────────────────────

    private static int DefaultLayer => LayerOrFallback("Default", 0);
    private static int IgnoreRaycastLayer => LayerOrFallback("Ignore Raycast", 2);
    private static int GroundLayer => LayerOrFallback("Ground", 3);
    private static int InteractableLayer => LayerOrFallback("Interactable", 6);
    private static int WallLayer => LayerOrFallback("Wall", 11);
    private static int PropsLayer => LayerOrFallback("Props", 12);

    // ── Entry point ─────────────────────────────────────────────────────────

    [MenuItem(MenuPath, true)]
    private static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(MenuPath)]
    private static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError($"{LogTag} Leave Play mode first: this edits prefabs and saves a scene.");
            return;
        }

        var log = new StringBuilder();
        int problems = 0;

        try
        {
            BuildAssets assets = LoadAssets(log);
            if (assets == null) { problems++; return; }

            SpotPrefabs prefabs = SaveSpotPrefabs(assets, log);
            if (prefabs == null) { problems++; return; }

            problems += BuildInScene(prefabs, assets, log);
        }
        finally
        {
            AssetDatabase.SaveAssets();

            string report = $"{LogTag} {(problems == 0 ? "Done." : $"Finished with {problems} problem(s).")}\n{log}";
            if (problems == 0) Debug.Log(report);
            else Debug.LogWarning(report);
        }
    }

    // ── Assets ──────────────────────────────────────────────────────────────

    private sealed class BuildAssets
    {
        public SO_HidingData HidingData;
        public SO_HighlightProfile HighlightProfile;
        public GameObject LockerModel;
        public GameObject ContainerModel;
        public GameObject NemesisPrefab;
        public Material Floor;
        public Material Wall;
        public Material Mark;
        public Material Table;
        public Mesh Cube;
        public LensSettings Lens;
        public InputActionReference LookAction;
        public DefaultInputAxisDriver LookDriver;
    }

    private static BuildAssets LoadAssets(StringBuilder log)
    {
        var assets = new BuildAssets
        {
            HidingData = AssetDatabase.LoadAssetAtPath<SO_HidingData>(HidingDataPath),
            HighlightProfile = AssetDatabase.LoadAssetAtPath<SO_HighlightProfile>(HighlightProfilePath),
            LockerModel = AssetDatabase.LoadAssetAtPath<GameObject>(LockerModelPath),
            ContainerModel = AssetDatabase.LoadAssetAtPath<GameObject>(ContainerModelPath),
            NemesisPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NemesisPrefabPath),
            Floor = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath),
            Wall = AssetDatabase.LoadAssetAtPath<Material>(WallMaterialPath),
            Mark = AssetDatabase.LoadAssetAtPath<Material>(MarkMaterialPath),
            Table = AssetDatabase.LoadAssetAtPath<Material>(TableMaterialPath),
            Cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx"),
        };

        bool missing = false;
        missing |= Require(assets.HidingData, HidingDataPath, log);
        missing |= Require(assets.LockerModel, LockerModelPath, log);
        missing |= Require(assets.ContainerModel, ContainerModelPath, log);
        missing |= Require(assets.NemesisPrefab, NemesisPrefabPath, log);
        missing |= Require(assets.Cube, "the built-in cube mesh", log);
        if (missing) return null;

        // Optional: without them things still work, they just look or behave plainer.
        Recommend(assets.HighlightProfile, HighlightProfilePath,
                  "the spots will not light up under the crosshair", log);
        Recommend(assets.Floor, FloorMaterialPath, "the floor renders with no material", log);
        Recommend(assets.Wall, WallMaterialPath, "the walls render with no material", log);
        Recommend(assets.Mark, MarkMaterialPath, "the floor marks render with no material", log);
        Recommend(assets.Table, TableMaterialPath, "the table renders with no material", log);

        ReadPlayerRig(assets, log);
        return assets;
    }

    private static bool Require(Object asset, string what, StringBuilder log)
    {
        if (asset != null) return false;
        log.AppendLine($"ERROR: {what} is missing. Nothing was built.");
        return true;
    }

    private static void Recommend(Object asset, string path, string consequence, StringBuilder log)
    {
        if (asset == null) log.AppendLine($"Warning: {path} is missing — {consequence}.");
    }

    /// <summary>
    /// Reads the lens and the look input off the player's camera rig, so an interior camera frames
    /// and turns exactly like the rig it takes over from. The FOV is the one PlayerCameraController
    /// actually applies at runtime (SO_CameraConfig.WalkFov), not the one serialised on the rig.
    /// </summary>
    private static void ReadPlayerRig(BuildAssets assets, StringBuilder log)
    {
        assets.Lens = LensSettings.Default;
        assets.LookDriver = DefaultInputAxisDriver.Default;

        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        CinemachineInputAxisController rig = player != null
            ? player.GetComponentInChildren<CinemachineInputAxisController>(true)
            : null;

        if (rig != null)
        {
            if (rig.TryGetComponent(out CinemachineCamera rigCamera)) assets.Lens = rigCamera.Lens;
            if (rig.TryGetComponent(out PlayerCameraController rigController) && rigController.Config != null)
                assets.Lens.FieldOfView = rigController.Config.WalkFov;

            foreach (CinemachineInputAxisController.Controller controller in rig.Controllers)
            {
                if (controller == null || controller.Input == null || controller.Input.InputAction == null) continue;
                assets.LookAction = controller.Input.InputAction;
                assets.LookDriver = controller.Driver;
                break;
            }
        }
        else
        {
            log.AppendLine($"Warning: no camera rig found in {PlayerPrefabPath}; the interior cameras " +
                           "use Cinemachine's default lens.");
        }

        if (assets.LookAction == null)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(DefaultInputActionsPath))
            {
                if (asset is InputActionReference reference && reference.name == DefaultLookActionName)
                {
                    assets.LookAction = reference;
                    break;
                }
            }
        }

        if (assets.LookAction == null)
            log.AppendLine("Warning: no Look input action found; the interior cameras will not turn with the mouse.");
    }

    // ── Prefabs ─────────────────────────────────────────────────────────────

    private sealed class SpotPrefabs
    {
        public GameObject Locker;
        public GameObject Table;
        public GameObject Container;

        public GameObject For(SpotSpec spec)
        {
            if (spec == LockerSpec) return Locker;
            if (spec == TableSpec) return Table;
            return Container;
        }
    }

    private static SpotPrefabs SaveSpotPrefabs(BuildAssets assets, StringBuilder log)
    {
        EnsureFolder(PrefabsFolder, SpotFolderName);

        GameObject father = AssetDatabase.LoadAssetAtPath<GameObject>(FatherPath) != null
            ? EditPrefab(FatherPath, root => ConfigureFather(root, assets), log)
            : CreatePrefab(FatherPath, scene => ObjectFactory.CreateGameObject(scene, HideFlags.None, "HidingSpot"),
                           root => ConfigureFather(root, assets), log);
        if (father == null) return null;

        var prefabs = new SpotPrefabs
        {
            Locker = SaveVariant(father, LockerSpec, assets, log),
            Table = SaveVariant(father, TableSpec, assets, log),
            Container = SaveVariant(father, ContainerSpec, assets, log),
        };

        return prefabs.Locker != null && prefabs.Table != null && prefabs.Container != null ? prefabs : null;
    }

    private static GameObject SaveVariant(GameObject father, SpotSpec spec, BuildAssets assets, StringBuilder log)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(spec.Path);
        if (existing == null)
        {
            // Saving an INSTANCE of the Father as a new asset is what makes it a variant of it.
            return CreatePrefab(spec.Path, scene => (GameObject)PrefabUtility.InstantiatePrefab(father, scene),
                                root => ConfigureVariant(root, spec, assets), log);
        }

        // Only ever update our own variant. Whatever else sits at that path is somebody's asset.
        Object source = PrefabUtility.GetCorrespondingObjectFromSource(existing);
        if (PrefabUtility.GetPrefabAssetType(existing) != PrefabAssetType.Variant ||
            AssetDatabase.GetAssetPath(source) != FatherPath)
        {
            log.AppendLine($"ERROR: {spec.Path} exists but is not a variant of {FatherPath}. Left " +
                           "untouched: move it out of the way and run this again.");
            return null;
        }

        return EditPrefab(spec.Path, root => ConfigureVariant(root, spec, assets), log);
    }

    /// <summary>
    /// Opens an existing prefab in isolation, brings it up to date and saves it back in place: same
    /// asset, same GUID, and the same file ID for everything that already existed.
    /// </summary>
    private static GameObject EditPrefab(string path, Action<GameObject> configure, StringBuilder log)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            configure(root);
            return SavePrefab(root, path, "Updated", log);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Builds a new prefab in a preview scene — never in the active one: whatever is open in the
    /// editor may hold somebody's unsaved work, and a GameObject created there and deleted again
    /// still leaves that scene dirty.
    /// </summary>
    private static GameObject CreatePrefab(string path, Func<Scene, GameObject> spawn, Action<GameObject> configure,
                                           StringBuilder log)
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = spawn(preview);

            // ObjectFactory.CreateGameObject(scene, ...) does NOT reliably honour a preview scene:
            // the first run left the Father's source object sitting in the ACTIVE scene (Zona1),
            // unsaved. Moved explicitly, and destroyed below whatever happens.
            if (root.scene != preview) SceneManager.MoveGameObjectToScene(root, preview);

            configure(root);
            return SavePrefab(root, path, "Created", log);
        }
        finally
        {
            // Belt and braces: closing the preview scene only cleans what is IN it.
            if (root != null && root.scene != preview) Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static GameObject SavePrefab(GameObject root, string path, string verb, StringBuilder log)
    {
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
        if (!success || saved == null)
        {
            log.AppendLine($"ERROR: could not save {path}.");
            return null;
        }

        log.AppendLine($"{verb} {path}");
        return saved;
    }

    /// <summary>
    /// The Father: everything every kind of spot shares, with the locker's numbers as defaults — it
    /// is a locker minus the mesh. The variants only move poses, clamp the camera and put a model
    /// under <see cref="ModelName"/>.
    /// </summary>
    private static void ConfigureFather(GameObject root, BuildAssets assets)
    {
        // The root carries the crosshair's collider, so the root is what goes on Interactable. The
        // prop's own solid colliders stay on their layers below it (plan §14.4).
        root.layer = InteractableLayer;

        HidingSpot spot = GetOrAdd<HidingSpot>(root);
        GetOrAdd<BoxCollider>(root).isTrigger = true;
        ItemProximityHighlight highlight = GetOrAdd<ItemProximityHighlight>(root);

        Transform model = GetOrCreateChild(root.transform, ModelName);
        Transform interior = GetOrCreateChild(root.transform, InteriorPoseName);
        Transform approach = GetOrCreateChild(root.transform, ApproachPointName);
        Transform exit = GetOrCreateChild(root.transform, ExitPoseName);
        Transform camera = GetOrCreateChild(root.transform, InteriorCameraName);

        // The model slot. A spot is never somewhere to walk: whatever solid collider a variant puts
        // under it is baked as a hole, not as a walkable roof — a locker or container top would
        // otherwise come out as a NavMesh island. On Props, and not on the root's Interactable,
        // because a NavMeshSurface ignores any modifier whose own layer it does not bake.
        model.gameObject.layer = PropsLayer;
        SetLocalPose(model, Vector3.zero, Quaternion.identity);
        NavMeshModifier notWalkable = GetOrAdd<NavMeshModifier>(model.gameObject);
        notWalkable.overrideArea = true;
        notWalkable.area = NotWalkableArea();
        notWalkable.ignoreFromBuild = false;
        notWalkable.applyToChildren = true;

        ConfigureInteriorCamera(camera.gameObject, assets);

        // The props profile sockets and valves share. The table's PSXIndustrial material shows it;
        // the locker's and the container's will not — their materials are embedded in the FBX with
        // emission off (Tools > Items > Validate Interactable Highlights lists them).
        var highlightObject = new SerializedObject(highlight);
        Field(highlightObject, "profile").objectReferenceValue = assets.HighlightProfile;
        highlightObject.ApplyModifiedPropertiesWithoutUndo();

        var spotObject = new SerializedObject(spot);
        // Never authored on a prefab: every instance would inherit it — the duplicate the validator
        // exists to catch. The builder gives each placed spot its own.
        Field(spotObject, "spotId").stringValue = string.Empty;
        Field(spotObject, "data").objectReferenceValue = assets.HidingData;
        Field(spotObject, "interiorPose").objectReferenceValue = interior;
        Field(spotObject, "approachPoint").objectReferenceValue = approach;
        Field(spotObject, "exitPose").objectReferenceValue = exit;
        Field(spotObject, "interiorCamera").objectReferenceValue = camera.GetComponent<CinemachineCamera>();
        spotObject.ApplyModifiedPropertiesWithoutUndo();

        ApplySpec(root, LockerSpec);
    }

    /// <summary>
    /// The interior camera, set up like the player's own rig: the same lens, the same look action
    /// and acceleration, and the same sensitivity/invert-Y handling (CameraSensitivityApplier), so
    /// moving between the two is a change of place and not of feel.
    /// </summary>
    private static void ConfigureInteriorCamera(GameObject cameraObject, BuildAssets assets)
    {
        // Components go on while the object is inactive, so no OnEnable runs half set up — a live
        // CinemachineCamera, even for a frame, is a candidate for the Game view's brain.
        cameraObject.SetActive(false);

        CinemachineCamera vcam = GetOrAdd<CinemachineCamera>(cameraObject);
        // Off in the prefab as well as on Awake: HidingSpot enables it on entry, above the rig.
        vcam.enabled = false;
        vcam.Lens = assets.Lens;
        vcam.Priority = new PrioritySettings();
        vcam.OutputChannel = OutputChannels.Default;

        CinemachinePanTilt panTilt = GetOrAdd<CinemachinePanTilt>(cameraObject);
        // Relative to the spot root, so pan 0 / tilt 0 is looking straight out of the spot however
        // the spot is placed.
        panTilt.ReferenceFrame = CinemachinePanTilt.ReferenceFrames.ParentObject;
        panTilt.RecenterTarget = CinemachinePanTilt.RecenterTargetModes.AxisCenter;

        CinemachineInputAxisController input = GetOrAdd<CinemachineInputAxisController>(cameraObject);
        CameraSensitivityApplier sensitivity = GetOrAdd<CameraSensitivityApplier>(cameraObject);

        cameraObject.SetActive(true);

        input.ScanRecursively = true;
        input.SuppressInputWhileBlending = true;
        // Scaled time on purpose: the pause menu freezes time, and the view must not turn under it.
        input.IgnoreTimeScale = false;
        input.PlayerIndex = -1;
        input.AutoEnableInputs = true;
        input.SynchronizeControllers();

        foreach (CinemachineInputAxisController.Controller controller in input.Controllers)
        {
            bool isTilt = controller.Name == TiltAxisName;
            controller.Enabled = true;
            controller.Input.InputAction = assets.LookAction;
            // The rig's base gains: 1 across, -1 up so that mouse-up looks up.
            // CameraSensitivityApplier scales both by the player's sensitivity at runtime.
            controller.Input.Gain = isTilt ? -1f : 1f;
            controller.Input.CancelDeltaTime = false;
            controller.Driver = assets.LookDriver;
        }
        MarkModified(input);

        var sensitivityObject = new SerializedObject(sensitivity);
        Field(sensitivityObject, "_verticalAxisName").stringValue = TiltAxisName;
        sensitivityObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureVariant(GameObject root, SpotSpec spec, BuildAssets assets)
    {
        Transform model = Child(root, ModelName);

        switch (spec.SpotType)
        {
            case EHidingSpotType.Locker:     BuildLockerModel(model, assets); break;
            case EHidingSpotType.UnderTable: BuildTableModel(model, assets); break;
            case EHidingSpotType.Container:  BuildContainerModel(model, assets); break;
        }

        ApplySpec(root, spec);
    }

    /// <summary>
    /// Everything a kind of spot changes: its type, poses, trigger and look clamps. Called on the
    /// Father with the locker's numbers too. On a variant every field written here becomes an
    /// override of the Father, which is why each goes through <see cref="MarkModified"/>.
    /// </summary>
    private static void ApplySpec(GameObject root, SpotSpec spec)
    {
        SetLocalPose(Child(root, InteriorPoseName), spec.InteriorPose, Quaternion.identity);
        // Facing the spot: it is where the Nemesis stands to open it.
        SetLocalPose(Child(root, ApproachPointName), spec.ApproachPoint, Quaternion.Euler(0f, 180f, 0f));
        SetLocalPose(Child(root, ExitPoseName), spec.ExitPose, Quaternion.identity);

        Transform camera = Child(root, InteriorCameraName);
        SetLocalPose(camera, spec.CameraPosition, Quaternion.identity);

        CinemachinePanTilt panTilt = camera.GetComponent<CinemachinePanTilt>();
        panTilt.PanAxis = ClampedAxis(spec.PanRange);
        panTilt.TiltAxis = ClampedAxis(spec.TiltRange);
        MarkModified(panTilt);

        BoxCollider trigger = root.GetComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = spec.TriggerCenter;
        trigger.size = spec.TriggerSize;
        MarkModified(trigger);

        var spotObject = new SerializedObject(root.GetComponent<HidingSpot>());
        Field(spotObject, "type").enumValueIndex = (int)spec.SpotType;
        // HidingSpot also refreshes this in OnValidate, but only when something triggers it; written
        // here so the saved asset lists the model's colliders whatever ran.
        SetArray(Field(spotObject, "ownColliders"), root.GetComponentsInChildren<Collider>(true));
        spotObject.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>A clamped look axis. Wrap has to go: the default pan wraps round at +-180, which
    /// across a +-15 range would flip the view from one clamp straight to the other.</summary>
    private static InputAxis ClampedAxis(Vector2 range) => new InputAxis
    {
        Value = 0f,
        Center = 0f,
        Range = range,
        Wrap = false,
        Recentering = new InputAxis.RecenteringSettings { Enabled = true, Wait = RecenterWait, Time = RecenterTime },
    };

    private static void BuildLockerModel(Transform model, BuildAssets assets)
    {
        Transform locker = GetOrCreateNestedModel(model, assets.LockerModel);
        SetLocalPose(locker, LockerModelOffset, ModelRotation);
    }

    private static void BuildContainerModel(Transform model, BuildAssets assets)
    {
        Transform container = GetOrCreateNestedModel(model, assets.ContainerModel);
        SetLocalPose(container, new Vector3(0f, 0f, -ContainerLength * 0.5f), ModelRotation);

        // The solid collider the model does not ship with. On Default, like the lockers' own: it has
        // to block vision, hearing and the camera, and phase 2 skips it while the spot is occupied.
        Transform solid = GetOrCreateChild(model, "Solid Collider");
        solid.gameObject.layer = DefaultLayer;
        SetLocalPose(solid, Vector3.zero, Quaternion.identity);

        BoxCollider box = GetOrAdd<BoxCollider>(solid.gameObject);
        box.isTrigger = false;
        box.center = new Vector3(0f, ContainerHeight * 0.5f, -ContainerLength * 0.5f);
        box.size = new Vector3(ContainerWidth, ContainerHeight, ContainerLength);
    }

    private static void BuildTableModel(Transform model, BuildAssets assets)
    {
        float legHeight = TableHeight - TableTopThickness;
        float legX = TableLength * 0.5f - TableLegSize;
        float legZ = TableDepth * 0.5f - TableLegSize;

        TablePart(model, "Top", new Vector3(0f, TableHeight - TableTopThickness * 0.5f, 0f),
                  new Vector3(TableLength, TableTopThickness, TableDepth), assets);
        TablePart(model, "Leg FL", new Vector3(-legX, legHeight * 0.5f, legZ), LegSize(legHeight), assets);
        TablePart(model, "Leg FR", new Vector3(legX, legHeight * 0.5f, legZ), LegSize(legHeight), assets);
        TablePart(model, "Leg BL", new Vector3(-legX, legHeight * 0.5f, -legZ), LegSize(legHeight), assets);
        TablePart(model, "Leg BR", new Vector3(legX, legHeight * 0.5f, -legZ), LegSize(legHeight), assets);
    }

    private static Vector3 LegSize(float height) => new Vector3(TableLegSize, height, TableLegSize);

    /// <summary>A solid box of the table, on Props: it is furniture, and Props is what both the
    /// NavMesh and the Nemesis's senses treat as furniture.</summary>
    private static void TablePart(Transform model, string name, Vector3 center, Vector3 size, BuildAssets assets)
    {
        Transform part = GetOrCreateChild(model, name);
        part.gameObject.layer = PropsLayer;
        part.localPosition = center;
        part.localRotation = Quaternion.identity;
        part.localScale = size;

        GetOrAdd<MeshFilter>(part.gameObject).sharedMesh = assets.Cube;
        MeshRenderer renderer = GetOrAdd<MeshRenderer>(part.gameObject);
        if (assets.Table != null) renderer.sharedMaterial = assets.Table;

        BoxCollider box = GetOrAdd<BoxCollider>(part.gameObject);
        box.isTrigger = false;
        box.center = Vector3.zero;
        box.size = Vector3.one;
    }

    /// <summary>The model prefab nested under the model slot: the one already there when this
    /// variant has it, a new instance otherwise.</summary>
    private static Transform GetOrCreateNestedModel(Transform slot, GameObject modelAsset)
    {
        for (int i = 0; i < slot.childCount; i++)
        {
            GameObject child = slot.GetChild(i).gameObject;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(child) &&
                PrefabUtility.GetCorrespondingObjectFromOriginalSource(child) == modelAsset)
                return child.transform;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, slot.gameObject.scene);
        instance.transform.SetParent(slot, false);
        return instance.transform;
    }

    // ── Scene ───────────────────────────────────────────────────────────────

    private sealed class StationResult
    {
        public string Name;
        public Transform Station;
        public Transform Interior;
        public Transform Approach;
    }

    /// <summary>
    /// Opens TestIñaki beside whatever is open (never as the active scene, never in single mode),
    /// rebuilds the area, saves, and puts the scene back as it found it: closed if it was closed,
    /// unloaded if it was unloaded. Returns the number of problems found.
    /// </summary>
    private static int BuildInScene(SpotPrefabs prefabs, BuildAssets assets, StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
        {
            log.AppendLine($"ERROR: {ScenePath} does not exist.");
            return 1;
        }

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool wasListed = scene.IsValid();
        bool wasLoaded = wasListed && scene.isLoaded;

        if (!wasLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        else if (scene.isDirty)
            log.AppendLine($"Note: {SceneName} already had unsaved changes; they were saved with the area.");

        try
        {
            RemovePreviousArea(scene);

            GameObject root = CreateObject(scene, AreaRootName, null);
            root.transform.SetPositionAndRotation(AreaOrigin, Quaternion.identity);

            BuildGeometry(scene, root.transform, assets);
            List<StationResult> stations = BuildStations(scene, root.transform, prefabs, assets);
            NemesisRoute route = BuildRoute(scene, root.transform, stations);
            List<Transform> spawns = BuildSpawnPoints(scene, root.transform);
            Transform nemesis = PlaceNemesis(scene, root.transform, assets, route, spawns);
            BuildCheckpoint(scene, root.transform);
            Label(scene, root.transform, "Entrance Sign", EntranceSign,
                  new Vector3(HalfSize + 0.05f, 2f, -3f), Quaternion.LookRotation(Vector3.left),
                  EntranceSignSize, new Vector2(6f, 3f), SignColor);

            NavMeshSurface surface = AddNavMeshSurface(root);
            int problems = BakeNavMesh(surface, log);
            if (problems == 0) problems += Verify(stations, route, spawns, nemesis, assets, log);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                log.AppendLine($"ERROR: could not save {ScenePath}.");
                problems++;
            }
            else
            {
                log.AppendLine($"Built '{AreaRootName}' in {ScenePath}: {stations.Count} spots " +
                               $"({string.Join(", ", StationIds())}), {route.transform.childCount} " +
                               "waypoints, a Nemesis with F9/F10 and a checkpoint at the door.");
            }

            return problems;
        }
        finally
        {
            // Unsaved changes are discarded here if something threw half way: a half-built area never
            // reaches the scene file.
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, !wasListed);
        }
    }

    private static IEnumerable<string> StationIds()
    {
        foreach (StationSpec spec in Stations) yield return spec.SpotId;
    }

    private static void RemovePreviousArea(Scene scene)
    {
        foreach (GameObject rootObject in scene.GetRootGameObjects())
            if (rootObject.name == AreaRootName) Object.DestroyImmediate(rootObject);
    }

    private static void BuildGeometry(Scene scene, Transform root, BuildAssets assets)
    {
        Transform parent = CreateObject(scene, "Geometry", root).transform;
        float wallY = WallHeight * 0.5f;
        float shell = HalfSize - WallThickness * 0.5f;

        // Ground, so the player's ground check and the bake both take it as floor.
        Block(scene, parent, "Floor", new Vector3(0f, -FloorThickness * 0.5f, 0f),
              new Vector3(HalfSize * 2f, FloorThickness, HalfSize * 2f), GroundLayer, assets.Cube, assets.Floor);

        Block(scene, parent, "Wall North", new Vector3(0f, wallY, shell),
              new Vector3(HalfSize * 2f, WallHeight, WallThickness), WallLayer, assets.Cube, assets.Wall);
        Block(scene, parent, "Wall South", new Vector3(0f, wallY, -shell),
              new Vector3(HalfSize * 2f, WallHeight, WallThickness), WallLayer, assets.Cube, assets.Wall);
        Block(scene, parent, "Wall West", new Vector3(-shell, wallY, 0f),
              new Vector3(WallThickness, WallHeight, InnerFace * 2f), WallLayer, assets.Cube, assets.Wall);

        // East: only north of the corridor. The gap south of it is the door.
        float eastLength = InnerFace - CorridorNorthFace;
        Block(scene, parent, "Wall East", new Vector3(shell, wallY, (InnerFace + CorridorNorthFace) * 0.5f),
              new Vector3(WallThickness, WallHeight, eastLength), WallLayer, assets.Cube, assets.Wall);

        // Corridor below it, hall above it, and open at the west end: that opening is the corner.
        float dividerLength = InnerFace - CornerEdgeX;
        Block(scene, parent, "Wall Divider",
              new Vector3((InnerFace + CornerEdgeX) * 0.5f, wallY, CorridorNorthFace + WallThickness * 0.5f),
              new Vector3(dividerLength, WallHeight, WallThickness), WallLayer, assets.Cube, assets.Wall);
    }

    private static List<StationResult> BuildStations(Scene scene, Transform root, SpotPrefabs prefabs,
                                                     BuildAssets assets)
    {
        Transform parent = CreateObject(scene, "Stations", root).transform;
        var results = new List<StationResult>(Stations.Length);

        foreach (StationSpec spec in Stations)
        {
            Transform station = CreateObject(scene, spec.Name, parent).transform;
            station.localPosition = spec.Position;
            station.localRotation = Quaternion.Euler(0f, spec.Yaw, 0f);

            var spotObject = (GameObject)PrefabUtility.InstantiatePrefab(prefabs.For(spec.Spot), scene);
            spotObject.transform.SetParent(station, false);
            SetLocalPose(spotObject.transform, Vector3.zero, Quaternion.identity);
            spotObject.name = $"{spotObject.name} ({spec.SpotId})";
            MarkModified(spotObject);

            // Per instance, never on the prefab — see ConfigureFather.
            var spot = new SerializedObject(spotObject.GetComponent<HidingSpot>());
            Field(spot, "spotId").stringValue = spec.SpotId;
            spot.ApplyModifiedPropertiesWithoutUndo();

            Transform interior = spotObject.transform.Find(InteriorPoseName);
            Transform approach = spotObject.transform.Find(ApproachPointName);

            // Siblings of the spot and not children: anything under the spot is counted as part of
            // it by its highlight and by its own-collider list.
            Label(scene, station, "Sign", spec.Sign, new Vector3(0f, spec.SignHeight, 0.3f),
                  Quaternion.LookRotation(Vector3.back), StationSignSize, new Vector2(3.4f, 1.6f), SignColor);
            BuildDistanceMarks(scene, station, interior.localPosition.z, spec.DistanceMarks, assets);

            results.Add(new StationResult
            {
                Name = spec.Name,
                Station = station,
                Interior = interior,
                Approach = approach,
            });
        }

        return results;
    }

    /// <summary>
    /// A bar across the floor every metre straight out from the spot, counted from the interior
    /// pose — the distance to the hidden player, which is what the cases talk about. The labels lie
    /// flat, upright for someone looking out of the spot.
    /// </summary>
    private static void BuildDistanceMarks(Scene scene, Transform station, float fromZ, int count, BuildAssets assets)
    {
        if (count <= 0) return;

        Transform parent = CreateObject(scene, "Distance Marks", station).transform;
        Quaternion flat = Quaternion.LookRotation(Vector3.down, Vector3.forward);

        for (int metres = 1; metres <= count; metres++)
        {
            float z = fromZ + metres;
            Block(scene, parent, $"Mark {metres} m", new Vector3(0f, MarkThickness * 0.5f, z),
                  new Vector3(MarkLength, MarkThickness, MarkWidth), DefaultLayer, assets.Cube, assets.Mark,
                  withCollider: false);
            Label(scene, parent, $"Label {metres} m", $"{metres} m",
                  new Vector3(MarkLength * 0.5f + 0.3f, MarkThickness + 0.005f, z), flat,
                  MarkLabelSize, new Vector2(1f, 0.5f), Color.white);
        }
    }

    /// <summary>
    /// One loop through the whole area, in the order a chase would take it: down the corridor past
    /// locker A, round the corner, past locker B, into the hall, in front of the table and of the
    /// container, and along the front of both.
    ///
    /// That order is only walked with cluster patrol off. On (SO_NemesisData, the project default),
    /// the Nemesis sweeps these as a nearest-neighbour chain plus generated points around each one,
    /// and what survives is the pairs: see <see cref="PassSide"/>.
    /// </summary>
    private static NemesisRoute BuildRoute(Scene scene, Transform root, List<StationResult> stations)
    {
        GameObject routeObject = CreateObject(scene, "Nemesis Route", root, typeof(NemesisRoute));
        StationResult lockerA = stations[0], lockerB = stations[1], table = stations[2], container = stations[3];

        var waypoints = new List<(string Label, Vector3 Position)>
        {
            ("Past locker A (1)", Pass(lockerA, 1)),
            ("Past locker A (2)", Pass(lockerA, -1)),
            ("Corridor end", root.TransformPoint(CorridorEnd)),
            ("Round the corner", root.TransformPoint(RoundTheCorner)),
            ("Past locker B (1)", Pass(lockerB, -1)),
            ("Past locker B (2)", Pass(lockerB, 1)),
            // Lined up so it walks the last few metres straight at the table and stops facing it.
            ("Hall east", root.TransformPoint(HallEast)),
            ("Table, stops ~5 m in front", InFront(table, TableStareStop)),
            ("Past the table (1)", Pass(table, 1)),
            ("Past the table (2)", Pass(table, -1)),
            // The same for the container, from the south.
            ("Hall south", root.TransformPoint(HallSouth)),
            ("Container, stops ~4 m in front", InFront(container, ContainerStareStop)),
            ("Past the container (1)", Pass(container, 1)),
            ("Past the container (2)", Pass(container, -1)),
        };

        for (int i = 0; i < waypoints.Count; i++)
        {
            GameObject waypoint = CreateObject(scene, $"WP {i + 1:00} {waypoints[i].Label}", routeObject.transform);
            waypoint.tag = NemesisRoute.WaypointTag;
            waypoint.transform.position = waypoints[i].Position;
        }

        return routeObject.GetComponent<NemesisRoute>();
    }

    /// <summary>A point on the line that runs past the spot over its approach point.</summary>
    private static Vector3 Pass(StationResult station, int side)
    {
        Vector3 approach = station.Station.InverseTransformPoint(station.Approach.position);
        return station.Station.TransformPoint(approach + new Vector3(side * PassSide, 0f, PassOut));
    }

    /// <summary>A point straight out from the spot, <paramref name="metres"/> from the hidden player.</summary>
    private static Vector3 InFront(StationResult station, float metres)
    {
        Vector3 interior = station.Station.InverseTransformPoint(station.Interior.position);
        return station.Station.TransformPoint(new Vector3(interior.x, 0f, interior.z + metres));
    }

    private static List<Transform> BuildSpawnPoints(Scene scene, Transform root)
    {
        Transform parent = CreateObject(scene, "Nemesis Spawn Points", root).transform;
        var spawns = new List<Transform>(SpawnPoints.Length);

        for (int i = 0; i < SpawnPoints.Length; i++)
        {
            Transform spawn = CreateObject(scene, $"Spawn {i + 1}", parent).transform;
            spawn.localPosition = SpawnPoints[i];
            spawns.Add(spawn);
        }

        return spawns;
    }

    /// <summary>
    /// Nemesis.prefab, awake from the first frame (no activating puzzle), on this area's route only,
    /// and with the F10 console on top of the F9 HUD the prefab already carries. Every change is an
    /// override on the instance; the prefab itself is not touched.
    /// </summary>
    private static Transform PlaceNemesis(Scene scene, Transform root, BuildAssets assets, NemesisRoute route,
                                          List<Transform> spawns)
    {
        var nemesis = (GameObject)PrefabUtility.InstantiatePrefab(assets.NemesisPrefab, scene);
        nemesis.transform.SetParent(root, false);
        SetLocalPose(nemesis.transform, NemesisStart, Quaternion.Euler(0f, NemesisStartYaw, 0f));

        var controller = new SerializedObject(nemesis.GetComponent<NemesisController>());
        Field(controller, "activatedByPuzzleId").stringValue = string.Empty;
        SetArray(Field(controller, "routes"), new[] { route });
        SetArray(Field(controller, "spawnPoints"), spawns);
        controller.ApplyModifiedPropertiesWithoutUndo();

        if (!nemesis.TryGetComponent(out NemesisTestConsole _)) nemesis.AddComponent<NemesisTestConsole>();

        return nemesis.transform;
    }

    /// <summary>
    /// The checkpoint at the door. Its trigger is on Ignore Raycast and not on anything solid:
    /// queries hit triggers in this project and the Nemesis's sight raycasts do not skip them, so a
    /// trigger on Default or Props would blind it across the doorway (plan §14.3).
    /// </summary>
    private static void BuildCheckpoint(Scene scene, Transform root)
    {
        GameObject checkpointObject = CreateObject(scene, "Checkpoint (door)", root, typeof(BoxCollider));
        checkpointObject.layer = IgnoreRaycastLayer;
        checkpointObject.transform.localPosition = CheckpointPosition;

        BoxCollider box = checkpointObject.GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = new Vector3(0f, 1.5f, 0f);
        box.size = new Vector3(1f, 3f, CorridorWidth);

        Transform respawn = CreateObject(scene, "Respawn Point", checkpointObject.transform).transform;
        respawn.localPosition = new Vector3(-2f, 0.02f, 0f);
        respawn.localRotation = Quaternion.Euler(0f, -90f, 0f);

        Checkpoint checkpoint = checkpointObject.AddComponent<Checkpoint>();
        var checkpointData = new SerializedObject(checkpoint);
        Field(checkpointData, "activationMode").enumValueIndex = (int)Checkpoint.EActivationMode.PhysicalTrigger;
        Field(checkpointData, "respawnPoint").objectReferenceValue = respawn;
        Field(checkpointData, "puzzleIds").arraySize = 0;
        checkpointData.ApplyModifiedPropertiesWithoutUndo();
    }

    // ── NavMesh ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A surface that bakes this area and nothing else in the scene (Collect Objects = children).
    ///
    /// DEFAULT IS IN THE MASK, unlike the project's usual Ground | Wall | Props. The lockers' and the
    /// container's solid colliders are on Default and have to stay there (plan §3.3); left out of
    /// the bake, the NavMesh would run straight through the container and the Nemesis would walk
    /// through it. Default is out elsewhere because ceilings live on it, and this area has none; the
    /// spots' model slot marks its colliders Not Walkable, so their tops do not become floor either.
    /// Zona1's own surface bakes Default for the same reason.
    /// </summary>
    private static NavMeshSurface AddNavMeshSurface(GameObject root)
    {
        NavMeshSurface surface = root.AddComponent<NavMeshSurface>();
        surface.agentTypeID = 0;
        surface.collectObjects = CollectObjects.Children;
        surface.layerMask = (1 << DefaultLayer) | (1 << GroundLayer) | (1 << WallLayer) | (1 << PropsLayer);
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.defaultArea = 0;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;

        // No public setter in this version of the package.
        var surfaceData = new SerializedObject(surface);
        Field(surfaceData, "m_GenerateLinks").boolValue = false;
        surfaceData.ApplyModifiedPropertiesWithoutUndo();

        return surface;
    }

    /// <summary>
    /// Bakes synchronously and stores the result as an asset next to the scene, replacing the last
    /// run's. The old one is deleted rather than overwritten: nothing else references it, and the
    /// area that did was destroyed at the start of the run.
    /// </summary>
    private static int BakeNavMesh(NavMeshSurface surface, StringBuilder log)
    {
        surface.BuildNavMesh();
        NavMeshData data = surface.navMeshData;
        if (data == null)
        {
            log.AppendLine("ERROR: the NavMesh bake produced nothing.");
            return 1;
        }

        EnsureFolder(DevScenesFolder, SceneName);
        if (AssetDatabase.LoadMainAssetAtPath(NavMeshAssetPath) != null) AssetDatabase.DeleteAsset(NavMeshAssetPath);
        AssetDatabase.CreateAsset(data, NavMeshAssetPath);

        log.AppendLine($"Baked the NavMesh into {NavMeshAssetPath}");
        return 0;
    }

    /// <summary>
    /// The checks that fail silently in play (see HidingSpotValidator): approach points off the
    /// NavMesh or out of grab reach, and waypoints or spawn points the agent cannot stand on.
    /// Measured against every NavMesh loaded in the editor — a level open on top of the same
    /// coordinates, at the same height, could make one pass that should not.
    /// </summary>
    private static int Verify(List<StationResult> stations, NemesisRoute route, List<Transform> spawns,
                              Transform nemesis, BuildAssets assets, StringBuilder log)
    {
        float reach = 1f;
        if (assets.NemesisPrefab.TryGetComponent(out NemesisStateManager stateManager) &&
            stateManager.NemesisData != null && stateManager.NemesisData.CatchMaxReach > 0f)
            reach = stateManager.NemesisData.CatchMaxReach;

        int problems = 0;

        foreach (StationResult station in stations)
        {
            if (!OnNavMesh(station.Approach.position, ApproachTolerance))
            {
                log.AppendLine($"- '{station.Name}': its ApproachPoint is not on the NavMesh " +
                               $"(within {ApproachTolerance} m).");
                problems++;
            }

            float gap = Vector3.Distance(station.Approach.position, station.Interior.position);
            if (gap > reach)
            {
                log.AppendLine($"- '{station.Name}': ApproachPoint {gap:0.00} m from the InteriorPose, " +
                               $"beyond the catch reach ({reach:0.##} m).");
                problems++;
            }
        }

        foreach (Transform waypoint in route.transform)
            problems += ReportOffNavMesh(waypoint, log);
        foreach (Transform spawn in spawns)
            problems += ReportOffNavMesh(spawn, log);
        problems += ReportOffNavMesh(nemesis, log);

        log.AppendLine(problems == 0
            ? "Checks passed: every approach point is on the NavMesh and within reach; every waypoint, " +
              "spawn point and the Nemesis stand on it."
            : $"Checks: {problems} problem(s) listed above.");
        return problems;
    }

    private static int ReportOffNavMesh(Transform point, StringBuilder log)
    {
        if (OnNavMesh(point.position, WaypointTolerance)) return 0;
        log.AppendLine($"- '{point.name}' is not on the NavMesh (within {WaypointTolerance} m).");
        return 1;
    }

    private static bool OnNavMesh(Vector3 position, float tolerance) =>
        NavMesh.SamplePosition(position, out NavMeshHit _, tolerance, NavMesh.AllAreas);

    private static int NotWalkableArea()
    {
        int area = NavMesh.GetAreaFromName("Not Walkable");
        return area >= 0 ? area : 1;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Creates straight into <paramref name="scene"/>: <c>new GameObject()</c> would land
    /// in the active scene first, and that is somebody else's.</summary>
    private static GameObject CreateObject(Scene scene, string name, Transform parent, params Type[] components)
    {
        GameObject created = ObjectFactory.CreateGameObject(scene, HideFlags.None, name, components);
        if (parent != null) created.transform.SetParent(parent, false);
        return created;
    }

    private static GameObject Block(Scene scene, Transform parent, string name, Vector3 center, Vector3 size,
                                    int layer, Mesh cube, Material material, bool withCollider = true)
    {
        GameObject block = withCollider
            ? CreateObject(scene, name, parent, typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider))
            : CreateObject(scene, name, parent, typeof(MeshFilter), typeof(MeshRenderer));

        block.layer = layer;
        block.transform.localPosition = center;
        block.transform.localScale = size;
        block.GetComponent<MeshFilter>().sharedMesh = cube;
        if (material != null) block.GetComponent<MeshRenderer>().sharedMaterial = material;
        return block;
    }

    /// <summary>World-space text. TextMeshPro reads from its -Z side, so the rotation passed in has
    /// to point +Z away from whoever is meant to read it.</summary>
    private static TextMeshPro Label(Scene scene, Transform parent, string name, string text, Vector3 localPosition,
                                     Quaternion localRotation, float fontSize, Vector2 box, Color color)
    {
        GameObject labelObject = CreateObject(scene, name, parent, typeof(TextMeshPro));
        labelObject.transform.localPosition = localPosition;
        labelObject.transform.localRotation = localRotation;

        TextMeshPro label = labelObject.GetComponent<TextMeshPro>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.color = color;
        label.rectTransform.sizeDelta = box;
        return label;
    }

    private static Transform Child(GameObject root, string name)
    {
        Transform child = root.transform.Find(name);
        if (child == null)
            throw new InvalidOperationException(
                $"'{root.name}' has no child '{name}'; it should come from {FatherPath}.");
        return child;
    }

    /// <summary>Found by name so that a re-run updates the same object — keeping its file ID and so
    /// every override that points at it — instead of stacking a copy next to it.</summary>
    private static Transform GetOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) return child;

        GameObject created = ObjectFactory.CreateGameObject(parent.gameObject.scene, HideFlags.None, name);
        created.transform.SetParent(parent, false);
        return created.transform;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component =>
        target.TryGetComponent(out T existing) ? existing : target.AddComponent<T>();

    private static void SetLocalPose(Transform target, Vector3 position, Quaternion rotation)
    {
        target.localPosition = position;
        target.localRotation = rotation;
        MarkModified(target);
    }

    /// <summary>
    /// A plain field write on anything that belongs to a prefab instance — the Father's objects seen
    /// from a variant, a model nested in one, a spot or the Nemesis placed in the scene — is only
    /// kept as an override once it is recorded. Otherwise the next reload quietly puts the source's
    /// value back.
    /// </summary>
    private static void MarkModified(Object target)
    {
        if (PrefabUtility.IsPartOfPrefabInstance(target))
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        EditorUtility.SetDirty(target);
    }

    /// <summary>A serialized field by name, or an error that names it: a field renamed in its script
    /// would otherwise surface as a bare NullReferenceException half way through the build.</summary>
    private static SerializedProperty Field(SerializedObject target, string name) =>
        target.FindProperty(name) ?? throw new InvalidOperationException(
            $"{target.targetObject.GetType().Name} has no serialized field '{name}'; " +
            $"update {nameof(HidingTestAreaBuilder)}.");

    private static void SetArray<T>(SerializedProperty property, IList<T> values) where T : Object
    {
        property.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }

    private static int LayerOrFallback(string name, int fallback)
    {
        int layer = LayerMask.NameToLayer(name);
        return layer >= 0 ? layer : fallback;
    }
}
