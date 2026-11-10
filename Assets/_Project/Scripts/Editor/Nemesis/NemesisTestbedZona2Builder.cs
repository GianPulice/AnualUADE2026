using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the "Zona 2 Lab" inside NemesisTestbed: a copy of Zona 2 as it is in WIRED_Zona1_Blockout,
/// at the same world coordinates (x 180..235, y -27..-9: nowhere near the testbed's own rooms), so its
/// routes, spawn points, pressure zones and drop read exactly as in the level. With it come two
/// <see cref="NemesisTestArea"/>s, "Testbed" and "Zona 2", which the F10 console lists under
/// ESPACIO DE PRUEBA: one press moves the player, the capture respawn and the Nemesis to that space.
///
/// WHAT IS COPIED (prefab instances stay prefab instances; the copy goes through the pasteboard, the
/// same path as Ctrl+D):
///   the level geometry (WIRED_ZONA_02, WIRED_ZONA_02_PROPS and the loose LV2 props, NavMeshObstacles
///   included), Zona 2's doors, the gate that closes its way back, the SP3 access door and keypad, the
///   boiler, the lever and switch panels and the hub's panel (they shape the rooms; their puzzles are
///   not the lab's business), the drop link and the NavMesh link over the distributor, the hub's Safe
///   Area volume, and "Nemesis Zona 2" (routes, spawn points, pressure zones) minus its
///   NemesisZoneWake — the "Zona 2" test area does that wake's job here.
/// Left out on purpose: everything that drives the level rather than shapes it (module triggers,
/// ambience zones, Architect triggers, the "ZONE 2 CHANGES" set, checkpoints, the entry teleport).
/// Any reference a copied object held to something in Zona 1 that was not copied is cleared, and
/// listed in the report.
///
/// NAVMESH. The lab root carries its own NavMeshSurface with Zona 1's settings (it bakes Default, the
/// testbed does not), collecting its children only, baked from the lab alone (TestbedNavMeshBake). The
/// testbed's own surface and NavMesh are not touched, and TestbedNavMeshBake leaves the lab out of
/// any later bake of it.
///
/// RE-RUNNING rebuilds the lab and the two areas from scratch: run it again whenever Zona 2 changes in
/// the level. It needs Zona 1 saved (it is copied from disk-equal state and closed without saving
/// afterwards, so nothing of the copy stays in it), never changes which scenes are open, and saves only
/// NemesisTestbed.
/// </summary>
public static class NemesisTestbedZona2Builder
{
    private const string LogTag = "[NemesisZona2Lab]";
    private const string MenuPath = "Tools/Nemesis/Build Zona 2 Lab (NemesisTestbed)";
    private const string TestbedPath = "Assets/_Project/Scenes/Dev/NemesisTestbed.unity";
    private const string LevelPath = "Assets/_Project/Scenes/GameScenes/WIRED_Zona1_Blockout.unity";

    private const string LabRootName = "Zona 2 Lab";
    private const string TestbedRootName = "Testbed";
    private const string TestbedAreaName = "Test Area - Testbed";

    /// <summary>Paths in WIRED_Zona1_Blockout, root first.</summary>
    private static readonly string[] CopiedPaths =
    {
        "---- NIVEL ----/WIRED_ZONA_02",
        "---- NIVEL ----/WIRED_ZONA_02_PROPS",
        "---- PROPS SUELTOS LV2 ----",
        "---- GAMEPLAY ----/---- DOORS ----/ZONA 2",
        "---- GAMEPLAY ----/Porton Zona 2",
        "---- GAMEPLAY ----/Nemesis Zona 2",
        "---- NAV ----/Drop Links/Drop_Hueco02",
        "---- NAV ----/NavLink SUB01 Oeste (sobre el distribuidor)",
        "---- SISTEMA ----/Safe Area Zona 2 (Hub ventilacion)",
        "---- PUZZLES ----/ZONE 2",
        "---- PUZZLES ----/AccessDoor",
        "---- PUZZLES ----/AccessKeypad",
        "---- PUZZLES ----/Boiler",
        "---- PUZZLES ----/LeverPanel",
        "---- PUZZLES ----/SwitchPanel",
    };

    /// <summary>The ventilation hub's floor, inside its Safe Area: where the level respawns the player
    /// too (Respawn Hub Zona 2). Facing south-west, away from all four spawn points (they are north and
    /// east of the hub): facing them, the spawn-in found every one in view and waited until the player
    /// turned round (measured 09/11/2026).</summary>
    private static readonly Vector3 Zona2Start = new Vector3(190.3f, -21.08f, 28.0f);
    private const float Zona2StartYaw = 225f;

    [MenuItem(MenuPath, true)]
    private static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(MenuPath)]
    private static void Build()
    {
        var log = new StringBuilder();
        int problems;
        try
        {
            problems = Run(log);
        }
        catch (Exception exception)
        {
            log.AppendLine($"ERROR: {exception}");
            problems = 1;
        }

        string report = $"{LogTag} {(problems == 0 ? "Built, baked and saved." : $"FAILED ({problems}).")}\n{log}";
        if (problems == 0) Debug.Log(report);
        else Debug.LogError(report);
    }

    private static int Run(StringBuilder log)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            log.AppendLine("ERROR: leave Play mode first.");
            return 1;
        }

        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();

        Scene level = SceneManager.GetSceneByPath(LevelPath);
        bool levelWasLoaded = level.IsValid() && level.isLoaded;
        if (levelWasLoaded && level.isDirty)
        {
            log.AppendLine("ERROR: WIRED_Zona1_Blockout has unsaved changes. Save or discard them first: " +
                           "the copy is taken from it and it is then closed without saving.");
            return 1;
        }

        Scene testbed = SceneManager.GetSceneByPath(TestbedPath);
        if (!(testbed.IsValid() && testbed.isLoaded)) testbed = EditorSceneManager.OpenScene(TestbedPath, OpenSceneMode.Additive);
        if (!levelWasLoaded) level = EditorSceneManager.OpenScene(LevelPath, OpenSceneMode.Additive);

        int problems;
        try
        {
            problems = BuildInto(level, testbed, log);
            if (problems == 0)
            {
                EditorSceneManager.MarkSceneDirty(testbed);
                EditorSceneManager.SaveScene(testbed);
            }
        }
        finally
        {
            // The duplicates were made in the level before moving out: closing it unsaved leaves no
            // trace there. The setup puts back what was open, read from disk.
            if (SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(level, true);
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        return problems;
    }

    private static int BuildInto(Scene level, Scene testbed, StringBuilder log)
    {
        GameObject testbedRoot = FindRoot(testbed, TestbedRootName);
        NemesisController testbedNemesis = FindInScene<NemesisController>(testbed);
        PlayerStateManager testbedPlayer = FindInScene<PlayerStateManager>(testbed);
        NavMeshSurface levelSurface = FindInScene<NavMeshSurface>(level);

        if (testbedRoot == null || testbedNemesis == null || testbedPlayer == null || levelSurface == null)
        {
            log.AppendLine("ERROR: missing one of: NemesisTestbed's 'Testbed' root, its Nemesis, its Player, " +
                           "or WIRED_Zona1_Blockout's NavMeshSurface. Nothing was built.");
            return 1;
        }

        // From scratch.
        GameObject oldLab = FindRoot(testbed, LabRootName);
        if (oldLab != null) Object.DestroyImmediate(oldLab);
        Transform oldArea = testbedRoot.transform.Find(TestbedAreaName);
        if (oldArea != null) Object.DestroyImmediate(oldArea.gameObject);

        var labRoot = new GameObject(LabRootName);
        SceneManager.MoveGameObjectToScene(labRoot, testbed);

        // ── The copy ──
        int copied = 0;
        foreach (string path in CopiedPaths)
        {
            GameObject source = FindByPath(level, path);
            if (source == null)
            {
                log.AppendLine($"Warning: '{path}' is not in the level any more; skipped.");
                continue;
            }

            GameObject copy = Duplicate(source);
            copy.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(copy, testbed);
            copy.transform.SetParent(labRoot.transform, true);
            copy.name = source.name;
            copied++;
        }
        log.AppendLine($"Copied {copied} of {CopiedPaths.Length} objects from the level.");

        // ── Zona 2's wake becomes the test area ──
        var zoneRoutes = new List<NemesisRoute>();
        var zoneSpawns = new List<Transform>();
        NemesisZoneWake wake = labRoot.GetComponentInChildren<NemesisZoneWake>(true);
        if (wake != null)
        {
            var so = new SerializedObject(wake);
            ReadList(so.FindProperty("routes"), zoneRoutes);
            ReadList(so.FindProperty("spawnPoints"), zoneSpawns);
            Object.DestroyImmediate(wake);
        }
        else
        {
            log.AppendLine("Warning: no NemesisZoneWake in the copy: the Zona 2 area gets every route under the lab.");
            zoneRoutes.AddRange(labRoot.GetComponentsInChildren<NemesisRoute>(true));
        }

        int cleared = ClearCrossSceneReferences(labRoot, testbed, log);

        // ── Areas ──
        CreateArea(labRoot.transform, "Test Area - Zona 2", "Zona 2", Zona2Start, Quaternion.Euler(0f, Zona2StartYaw, 0f),
                   zoneRoutes, zoneSpawns);

        var testbedRoutes = new List<NemesisRoute>();
        var testbedSpawns = new List<Transform>();
        var nemesisSo = new SerializedObject(testbedNemesis);
        ReadList(nemesisSo.FindProperty("routes"), testbedRoutes);
        ReadList(nemesisSo.FindProperty("spawnPoints"), testbedSpawns);
        Transform playerTransform = testbedPlayer.transform;
        Transform testbedArea = CreateArea(testbedRoot.transform, TestbedAreaName, "Testbed", playerTransform.position,
                                           Quaternion.Euler(0f, playerTransform.eulerAngles.y, 0f), testbedRoutes, testbedSpawns);
        // First in the hierarchy, so it is the first button.
        testbedArea.SetAsFirstSibling();

        log.AppendLine($"Areas: Zona 2 ({zoneRoutes.Count} routes, {zoneSpawns.Count} spawn points), " +
                       $"Testbed ({testbedRoutes.Count} routes, {testbedSpawns.Count} spawn points).");

        // ── NavMesh ──
        NavMeshSurface surface = labRoot.AddComponent<NavMeshSurface>();
        EditorUtility.CopySerialized(levelSurface, surface);
        var surfaceSo = new SerializedObject(surface);
        surfaceSo.FindProperty("m_NavMeshData").objectReferenceValue = null;
        surfaceSo.ApplyModifiedPropertiesWithoutUndo();
        surface.collectObjects = CollectObjects.Children;

        DeleteOwnNavMeshAsset(testbed, surface, log);
        var data = TestbedNavMeshBake.BakeRoots(testbed, surface, new[] { labRoot.transform }, log);
        TestbedNavMeshBake.Install(testbed, surface, data, log);

        if (cleared > 0) log.AppendLine($"{cleared} reference(s) to Zona 1 cleared (listed above).");
        return 0;
    }

    private static Transform CreateArea(Transform parent, string objectName, string displayName, Vector3 start,
                                        Quaternion rotation, List<NemesisRoute> routes, List<Transform> spawns)
    {
        var area = new GameObject(objectName);
        area.transform.SetParent(parent, false);

        var startPoint = new GameObject("Player Start");
        startPoint.transform.SetParent(area.transform, false);
        startPoint.transform.SetPositionAndRotation(start, rotation);

        // Activated by the area's move, never by itself: no puzzle ids, no trigger.
        Checkpoint checkpoint = startPoint.AddComponent<Checkpoint>();
        var checkpointSo = new SerializedObject(checkpoint);
        checkpointSo.FindProperty("activationMode").enumValueIndex = (int)Checkpoint.EActivationMode.PuzzleCompleted;
        checkpointSo.ApplyModifiedPropertiesWithoutUndo();

        NemesisTestArea testArea = area.AddComponent<NemesisTestArea>();
        var so = new SerializedObject(testArea);
        so.FindProperty("displayName").stringValue = displayName;
        so.FindProperty("playerStart").objectReferenceValue = startPoint.transform;
        so.FindProperty("respawn").objectReferenceValue = checkpoint;
        WriteList(so.FindProperty("routes"), routes);
        WriteList(so.FindProperty("spawnPoints"), spawns);
        so.ApplyModifiedPropertiesWithoutUndo();

        return area.transform;
    }

    /// <summary>Ctrl+D on one object: prefab instances stay instances, references inside it are remapped.</summary>
    private static GameObject Duplicate(GameObject source)
    {
        Object[] previous = Selection.objects;
        Selection.objects = new Object[] { source };
        Unsupported.DuplicateGameObjectsUsingPasteboard();
        GameObject copy = Selection.activeGameObject;
        Selection.objects = previous;

        if (copy == null || copy == source) throw new InvalidOperationException($"Could not duplicate '{source.name}'.");
        return copy;
    }

    /// <summary>A reference across scenes does not survive the save (and would point at Zona 1 if it
    /// did): cleared here, and named, so a copied object that needs one is visible.</summary>
    private static int ClearCrossSceneReferences(GameObject root, Scene scene, StringBuilder log)
    {
        int cleared = 0;
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null || component is Transform) continue;

            var so = new SerializedObject(component);
            SerializedProperty property = so.GetIterator();
            bool changed = false;
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;

                Object value = property.objectReferenceValue;
                Scene owner = value is GameObject go ? go.scene : value is Component c ? c.gameObject.scene : default;
                if (!owner.IsValid() || owner == scene) continue;

                log.AppendLine($"  cleared {component.GetType().Name}.{property.propertyPath} on " +
                               $"'{PathOf(component.transform)}' (pointed at '{value.name}' in {owner.name})");
                property.objectReferenceValue = null;
                changed = true;
                cleared++;
            }

            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }

        return cleared;
    }

    /// <summary>The lab's NavMesh from an earlier run, so Install writes to the same file name.</summary>
    private static void DeleteOwnNavMeshAsset(Scene scene, NavMeshSurface surface, StringBuilder log)
    {
        string folder = Path.Combine(Path.GetDirectoryName(scene.path) ?? "Assets",
                                     Path.GetFileNameWithoutExtension(scene.path)).Replace('\\', '/');
        string path = $"{folder}/NavMesh-{surface.name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Object>(path) == null) return;

        AssetDatabase.DeleteAsset(path);
        log.AppendLine($"Old {path} deleted.");
    }

    private static void ReadList<T>(SerializedProperty list, List<T> into) where T : Object
    {
        if (list == null) return;
        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue is T item) into.Add(item);
        }
    }

    private static void WriteList<T>(SerializedProperty list, List<T> items) where T : Object
    {
        list.arraySize = items.Count;
        for (int i = 0; i < items.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name) return root;
        }

        return null;
    }

    private static GameObject FindByPath(Scene scene, string path)
    {
        string[] parts = path.Split('/');
        GameObject current = FindRoot(scene, parts[0]);
        for (int i = 1; current != null && i < parts.Length; i++)
        {
            Transform child = current.transform.Find(parts[i]);
            current = child != null ? child.gameObject : null;
        }

        return current;
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);
            if (found != null) return found;
        }

        return null;
    }

    private static string PathOf(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }
}
