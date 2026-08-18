using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor utility: builds a blocking scene for testing, adapted to WIRED's GDD.
/// Floor 1 (Y=0): Entrance, Central Hub, SP1 (Fuses+Panel), SP2 (Containers),
///              SP3 (Valves), SP2 Clues Room, Final Valves Room.
/// Floor 2 (Y=0, to the north): Elevator Room, Key, Fuse, Electrical Panel,
///              Valve Info 1, Valves Info 2, Story Room.
/// Connection between floors: a yellow "Elevator" cube on each floor (visual marker only).
/// Menu: Tools / Scenes / Build Testing Blockout
/// </summary>
public static class TestSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/TestScenes/TestingBlockout.unity";

    private const float WallHeight = 3f;
    private const float WallThickness = 0.2f;
    private const float DoorwayWidth = 3f;

    private static Material floorMat, wallMat, hubMat, accentMat, elevatorMat;

    [MenuItem("Tools/Scenes/Build Testing Blockout")]
    public static void Build()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        BuildLights();
        BuildCamera();
        BuildMaterials();

        GameObject root = new GameObject("Blockout");

        // ============ FLOOR 1 ============
        Transform p1 = NewContainer(root.transform, "Piso1");

        BuildRoom(p1, "Room_01", new Vector2(0, -16), new Vector2(6, 4), floorMat, wallMat,
            Op(WallSide.North, 0, DoorwayWidth));

        BuildRoom(p1, "Room_02", new Vector2(0, -7), new Vector2(12, 10), floorMat, hubMat,
            Op(WallSide.South, 0, DoorwayWidth),
            Op(WallSide.East, 0, DoorwayWidth),
            Op(WallSide.West, 0, DoorwayWidth),
            Op(WallSide.North, 0, DoorwayWidth));

        BuildRoom(p1, "Room_03", new Vector2(15, -7), new Vector2(8, 8), floorMat, wallMat,
            Op(WallSide.West, 0, DoorwayWidth));

        BuildRoom(p1, "Room_04", new Vector2(-15, -7), new Vector2(8, 8), floorMat, wallMat,
            Op(WallSide.East, 0, DoorwayWidth),
            Op(WallSide.West, 0, DoorwayWidth));

        BuildRoom(p1, "Room_05", new Vector2(-25, -7), new Vector2(6, 6), floorMat, wallMat,
            Op(WallSide.East, 0, DoorwayWidth));

        BuildRoom(p1, "Room_06", new Vector2(8, 6), new Vector2(8, 6), floorMat, accentMat,
            Op(WallSide.West, 0, DoorwayWidth));

        BuildRoom(p1, "Room_07", new Vector2(-8, 6), new Vector2(6, 6), floorMat, wallMat,
            Op(WallSide.East, 0, DoorwayWidth));

        BuildCorridor(p1, "Corridor_01", new Vector2(6, -7), new Vector2(11, -7), DoorwayWidth, floorMat, wallMat);
        BuildCorridor(p1, "Corridor_02", new Vector2(-6, -7), new Vector2(-11, -7), DoorwayWidth, floorMat, wallMat);
        BuildCorridor(p1, "Corridor_03", new Vector2(-19, -7), new Vector2(-22, -7), DoorwayWidth, floorMat, wallMat);
        BuildCorridor(p1, "Corridor_04", new Vector2(0, -2), new Vector2(0, 6), DoorwayWidth, floorMat, wallMat);
        BuildCorridor(p1, "Corridor_05", new Vector2(0, 6), new Vector2(4, 6), DoorwayWidth, floorMat, wallMat);
        BuildCorridor(p1, "Corridor_06", new Vector2(0, 6), new Vector2(-5, 6), DoorwayWidth, floorMat, wallMat);

        BuildElevatorMarker(p1, "Marker_01", new Vector3(4f, 1f, -3f));

        // ============ FLOOR 2 ============
        // We place it north of Floor 1 (separated), conceptually connected by the elevator.
        Transform p2 = NewContainer(root.transform, "Piso2");
        float p2Y = 0f;     // same floor level, to keep testing simple
        Vector2 p2Origin = new Vector2(0, 30);

        BuildRoom(p2, "Room_01", p2Origin + new Vector2(0, 0), new Vector2(6, 6), floorMat, hubMat,
            Op(WallSide.North, 0, DoorwayWidth),
            Op(WallSide.East, 0, DoorwayWidth),
            Op(WallSide.West, 0, DoorwayWidth));

        BuildRoom(p2, "Room_02", p2Origin + new Vector2(9, 0), new Vector2(6, 6), floorMat, accentMat,
            Op(WallSide.West, 0, DoorwayWidth),
            Op(WallSide.North, 0, DoorwayWidth));

        BuildRoom(p2, "Room_03", p2Origin + new Vector2(-9, 0), new Vector2(6, 6), floorMat, accentMat,
            Op(WallSide.East, 0, DoorwayWidth),
            Op(WallSide.North, 0, DoorwayWidth));

        BuildRoom(p2, "Room_04", p2Origin + new Vector2(0, 9), new Vector2(6, 6), floorMat, wallMat,
            Op(WallSide.South, 0, DoorwayWidth),
            Op(WallSide.North, 0, DoorwayWidth));

        BuildRoom(p2, "Room_05", p2Origin + new Vector2(9, 9), new Vector2(6, 6), floorMat, wallMat,
            Op(WallSide.West, 0, DoorwayWidth),
            Op(WallSide.South, 0, DoorwayWidth));

        BuildRoom(p2, "Room_06", p2Origin + new Vector2(-9, 9), new Vector2(6, 6), floorMat, wallMat,
            Op(WallSide.East, 0, DoorwayWidth),
            Op(WallSide.South, 0, DoorwayWidth));

        BuildRoom(p2, "Room_07", p2Origin + new Vector2(0, 18), new Vector2(8, 6), floorMat, wallMat,
            Op(WallSide.South, 0, DoorwayWidth));

        BuildCorridor(p2, "Corridor_01",
            p2Origin + new Vector2(3, 0), p2Origin + new Vector2(6, 0), DoorwayWidth, floorMat, wallMat);
        BuildCorridor(p2, "Corridor_02",
            p2Origin + new Vector2(-3, 0), p2Origin + new Vector2(-6, 0), DoorwayWidth, floorMat, wallMat);
        BuildCorridor(p2, "Corridor_03",
            p2Origin + new Vector2(0, 3), p2Origin + new Vector2(0, 6), DoorwayWidth, floorMat, wallMat);
        BuildCorridor(p2, "Corridor_04",
            p2Origin + new Vector2(9, 3), p2Origin + new Vector2(9, 6), DoorwayWidth, floorMat, wallMat);
        BuildCorridor(p2, "Corridor_05",
            p2Origin + new Vector2(-9, 3), p2Origin + new Vector2(-9, 6), DoorwayWidth, floorMat, wallMat);
        BuildCorridor(p2, "Corridor_06",
            p2Origin + new Vector2(3, 9), p2Origin + new Vector2(6, 9), DoorwayWidth, floorMat, wallMat);
        BuildCorridor(p2, "Corridor_07",
            p2Origin + new Vector2(-3, 9), p2Origin + new Vector2(-6, 9), DoorwayWidth, floorMat, wallMat);
        BuildCorridor(p2, "Corridor_08",
            p2Origin + new Vector2(0, 12), p2Origin + new Vector2(0, 15), DoorwayWidth, floorMat, wallMat);

        BuildElevatorMarker(p2, "Marker_01", new Vector3(0f, 1f, p2Origin.y));

        // ============ Spawn point ============
        GameObject spawn = new GameObject("PlayerSpawnPoint");
        spawn.transform.position = new Vector3(0f, 0.1f, -16f); // inside the Entrance

        // ============ Save ============
        SaveMaterial(floorMat, "Assets/Scenes/TestScenes/Materials/Blockout_Floor.mat");
        SaveMaterial(wallMat, "Assets/Scenes/TestScenes/Materials/Blockout_Wall.mat");
        SaveMaterial(hubMat, "Assets/Scenes/TestScenes/Materials/Blockout_Hub.mat");
        SaveMaterial(accentMat, "Assets/Scenes/TestScenes/Materials/Blockout_Accent.mat");
        SaveMaterial(elevatorMat, "Assets/Scenes/TestScenes/Materials/Blockout_Elevator.mat");

        EnsureFolder("Assets/Scenes/TestScenes");
        EditorSceneManager.SaveScene(scene, ScenePath);

        Debug.Log($"[TestSceneBuilder] Blockout WIRED VS creado en {ScenePath}");
    }

    // ===================== HELPERS =====================

    private enum WallSide { North, South, East, West }

    private struct WallOpening
    {
        public WallSide side;
        public float offset;
        public float width;
        public WallOpening(WallSide side, float offset, float width)
        {
            this.side = side; this.offset = offset; this.width = width;
        }
    }

    private static WallOpening Op(WallSide s, float o, float w) => new WallOpening(s, o, w);

    private static Transform NewContainer(Transform parent, string n)
    {
        GameObject go = new GameObject(n);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static void BuildLights()
    {
        GameObject lightGO = new GameObject("Directional Light");
        Light light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        light.shadows = LightShadows.Soft;
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    private static void BuildCamera()
    {
        GameObject camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        Camera cam = camGO.AddComponent<Camera>();
        camGO.AddComponent<AudioListener>();
        camGO.transform.position = new Vector3(0f, 40f, 0f);
        camGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.orthographic = false;
        cam.fieldOfView = 80f;
    }

    private static void BuildMaterials()
    {
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null) litShader = Shader.Find("Standard");

        floorMat = new Material(litShader); floorMat.color = new Color(0.50f, 0.50f, 0.55f);
        wallMat = new Material(litShader); wallMat.color = new Color(0.72f, 0.68f, 0.58f);
        hubMat = new Material(litShader); hubMat.color = new Color(0.45f, 0.65f, 0.55f);     // verdoso = zona segura
        accentMat = new Material(litShader); accentMat.color = new Color(0.65f, 0.40f, 0.40f); // rojizo = restringido / item importante
        elevatorMat = new Material(litShader); elevatorMat.color = new Color(0.95f, 0.85f, 0.20f); // amarillo brillante
    }

    private static void BuildElevatorMarker(Transform parent, string name, Vector3 worldPos)
    {
        GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pillar.name = name;
        pillar.transform.SetParent(parent, false);
        pillar.transform.localPosition = worldPos;
        pillar.transform.localScale = new Vector3(2f, 2f, 2f);
        pillar.GetComponent<MeshRenderer>().sharedMaterial = elevatorMat;
        Object.DestroyImmediate(pillar.GetComponent<Collider>());
    }

    private static void BuildRoom(Transform parent, string name, Vector2 center, Vector2 size,
        Material floorM, Material wallM, params WallOpening[] openings)
    {
        GameObject room = new GameObject(name);
        room.transform.SetParent(parent, false);

        // Floor
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.SetParent(room.transform, false);
        floor.transform.localPosition = new Vector3(center.x, -0.05f, center.y);
        floor.transform.localScale = new Vector3(size.x, 0.1f, size.y);
        floor.GetComponent<MeshRenderer>().sharedMaterial = floorM;

        BuildWall(room.transform, "WallNorth", center, size, WallSide.North, openings, wallM);
        BuildWall(room.transform, "WallSouth", center, size, WallSide.South, openings, wallM);
        BuildWall(room.transform, "WallEast", center, size, WallSide.East, openings, wallM);
        BuildWall(room.transform, "WallWest", center, size, WallSide.West, openings, wallM);
    }

    private static void BuildWall(Transform roomT, string name, Vector2 center, Vector2 size,
        WallSide side, WallOpening[] openings, Material mat)
    {
        float wallLength;
        Vector3 wallCenter;
        bool isVertical;

        switch (side)
        {
            case WallSide.North:
                wallCenter = new Vector3(center.x, WallHeight * 0.5f, center.y + size.y * 0.5f);
                wallLength = size.x; isVertical = false; break;
            case WallSide.South:
                wallCenter = new Vector3(center.x, WallHeight * 0.5f, center.y - size.y * 0.5f);
                wallLength = size.x; isVertical = false; break;
            case WallSide.East:
                wallCenter = new Vector3(center.x + size.x * 0.5f, WallHeight * 0.5f, center.y);
                wallLength = size.y; isVertical = true; break;
            default:
                wallCenter = new Vector3(center.x - size.x * 0.5f, WallHeight * 0.5f, center.y);
                wallLength = size.y; isVertical = true; break;
        }

        WallOpening? opening = null;
        foreach (WallOpening o in openings)
            if (o.side == side) { opening = o; break; }

        if (opening == null)
        {
            SpawnWallSegment(roomT, name, wallCenter, wallLength, isVertical, mat);
            return;
        }

        float openCenterAlongWall = opening.Value.offset;
        float openHalf = opening.Value.width * 0.5f;

        float seg1Length = (wallLength * 0.5f) + openCenterAlongWall - openHalf;
        float seg2Length = (wallLength * 0.5f) - openCenterAlongWall - openHalf;

        float seg1CenterOffset = -wallLength * 0.5f + seg1Length * 0.5f;
        float seg2CenterOffset = wallLength * 0.5f - seg2Length * 0.5f;

        if (seg1Length > 0.01f)
        {
            Vector3 c1 = wallCenter + (isVertical ? new Vector3(0, 0, seg1CenterOffset) : new Vector3(seg1CenterOffset, 0, 0));
            SpawnWallSegment(roomT, name + "_A", c1, seg1Length, isVertical, mat);
        }
        if (seg2Length > 0.01f)
        {
            Vector3 c2 = wallCenter + (isVertical ? new Vector3(0, 0, seg2CenterOffset) : new Vector3(seg2CenterOffset, 0, 0));
            SpawnWallSegment(roomT, name + "_B", c2, seg2Length, isVertical, mat);
        }
    }

    private static void SpawnWallSegment(Transform parent, string name, Vector3 center,
        float length, bool isVertical, Material mat)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = center;
        wall.transform.localScale = isVertical
            ? new Vector3(WallThickness, WallHeight, length)
            : new Vector3(length, WallHeight, WallThickness);
        wall.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    private static void BuildCorridor(Transform parent, string name, Vector2 from, Vector2 to,
        float width, Material floorM, Material wallM)
    {
        GameObject corridor = new GameObject(name);
        corridor.transform.SetParent(parent, false);

        Vector2 center = (from + to) * 0.5f;
        Vector2 dir = to - from;
        bool horizontal = Mathf.Abs(dir.x) > Mathf.Abs(dir.y);
        float length = horizontal ? Mathf.Abs(dir.x) : Mathf.Abs(dir.y);

        Vector2 size = horizontal ? new Vector2(length, width) : new Vector2(width, length);

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.SetParent(corridor.transform, false);
        floor.transform.localPosition = new Vector3(center.x, -0.05f, center.y);
        floor.transform.localScale = new Vector3(size.x, 0.1f, size.y);
        floor.GetComponent<MeshRenderer>().sharedMaterial = floorM;

        if (horizontal)
        {
            SpawnWallSegment(corridor.transform, "WallNorth",
                new Vector3(center.x, WallHeight * 0.5f, center.y + width * 0.5f), length, false, wallM);
            SpawnWallSegment(corridor.transform, "WallSouth",
                new Vector3(center.x, WallHeight * 0.5f, center.y - width * 0.5f), length, false, wallM);
        }
        else
        {
            SpawnWallSegment(corridor.transform, "WallEast",
                new Vector3(center.x + width * 0.5f, WallHeight * 0.5f, center.y), length, true, wallM);
            SpawnWallSegment(corridor.transform, "WallWest",
                new Vector3(center.x - width * 0.5f, WallHeight * 0.5f, center.y), length, true, wallM);
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
        string leaf = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    private static void SaveMaterial(Material mat, string path)
    {
        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace("\\", "/"));
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mat, path);
    }
}
