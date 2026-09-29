using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Puts the three decoys in NemesisTestbed (plan Fase 2B part 4): until now there were none in any
/// scene, so nothing about decoys — the choice, the habituation, the radio being smashed — could be
/// played. Cases 22, 25, 28–33 and 37.
///
/// WHERE (world metres; each one's InvestigatePoint, the spot the Nemesis walks to, faces into the room):
///   RADIO       SALA_LATERAL, on a table against the north wall (x -19, z 24.6). Heard at 12 m. The
///               Nemesis smashes it when it comes for it.
///   FIRE ALARM  PASILLO_CARGA, on the west wall at 1.4 m (x -6.9, z 38). Heard from anywhere.
///   CHAINS      PASILLO_OESTE, against its west wall (x -9.7, z 30). Heard at 8 m; infinite uses.
/// Each has a sign saying what to try with it. The player sets them off with E, like in the level.
///
/// Nothing here is baked: the decoys sit on the Interactable layer and the table on Default, which the
/// testbed's surface does not collect. So no NavMesh changes and no bake.
///
/// Same rules as the other testbed builders: it only ever saves NemesisTestbed (opened additively when
/// not loaded), never changes the active scene, and re-running it rebuilds the stations from scratch.
/// Also runnable without the editor open: -executeMethod NemesisTestbedDecoysBuilder.BuildFromCommandLine.
/// </summary>
public static class NemesisTestbedDecoysBuilder
{
    private const string LogTag = "[NemesisDecoyStations]";
    private const string MenuPath = "Tools/Nemesis/Build Decoy Stations (NemesisTestbed)";
    private const string ScenePath = "Assets/_Project/Scenes/Dev/NemesisTestbed.unity";
    private const string RootName = "Decoy Stations";

    private const string RadioPrefabPath = "Assets/_Project/Prefabs/Decoys/Decoy_Radio.prefab";
    private const string AlarmPrefabPath = "Assets/_Project/Prefabs/Decoys/Decoy_FireAlarm.prefab";
    private const string ChainsPrefabPath = "Assets/_Project/Prefabs/Decoys/Decoy_Chains.prefab";
    private const string PropMaterialPath = "Assets/_Project/Scenes/Dev/NemesisTestbedMaterials/testbed_prop.mat";

    private static readonly Color SignColor = new Color(1f, 0.85f, 0.4f);
    private const float SignFontSize = 2.2f;

    // The radio's prefab keeps its InvestigatePoint 0.8 m below and 1 m in front of it: it is meant to
    // stand on a table, with the Nemesis walking up in front of it.
    private const float TableHeight = 0.8f;
    private static readonly Vector3 RadioPosition = new Vector3(-19f, TableHeight, 24.6f);
    private static readonly Vector3 AlarmPosition = new Vector3(-6.85f, 1.4f, 38f);
    private static readonly Vector3 ChainsPosition = new Vector3(-9.7f, 0f, 30f);

    private const string RadioSign =
        "<b>RADIO</b> (12 m)\n<size=70%>E para prenderla. Case 31: te perdió hace 10 s y suena lejos -> va y la " +
        "rompe. Case 30: te ve y la prendés -> te sigue a vos. Case 37: rompiéndola, te ve -> corta y te " +
        "persigue. F9 fila 'foco'.</size>";

    private const string AlarmSign =
        "<b>ALARMA</b> (se oye en todo el nivel)\n<size=70%>Case 25: prendela y andá a otra zona sin que te " +
        "sienta -> va a revisarla, no te persigue. Case 22: te persigue y te ve -> sigue con vos.</size>";

    private const string ChainsSign =
        "<b>CADENAS</b> (8 m, usos infinitos)\n<size=70%>Case 32: sacudilas 3 veces desde lejos -> cada vez le " +
        "importan menos; a la tercera no cruza el nivel. Case 28: investigándolas, un paso tuyo del otro " +
        "lado -> gira y va hacia vos. Case 29: con la radio, no va y viene.</size>";

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

    /// <summary>For -batchmode -executeMethod: builds, and exits with 0 on success.</summary>
    public static void BuildFromCommandLine()
    {
        int exitCode = 1;
        try
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            exitCode = RunAndReport() ? 0 : 1;
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

    private static bool RunAndReport()
    {
        var log = new StringBuilder();
        bool ok;
        try
        {
            ok = Run(log);
        }
        catch (Exception exception)
        {
            log.AppendLine($"ERROR: {exception}");
            ok = false;
        }

        string report = $"{LogTag} {(ok ? "Built and saved." : "FAILED.")}\n{log}";
        if (ok) Debug.Log(report);
        else Debug.LogError(report);
        return ok;
    }

    private static bool Run(StringBuilder log)
    {
        var radio = AssetDatabase.LoadAssetAtPath<GameObject>(RadioPrefabPath);
        var alarm = AssetDatabase.LoadAssetAtPath<GameObject>(AlarmPrefabPath);
        var chains = AssetDatabase.LoadAssetAtPath<GameObject>(ChainsPrefabPath);
        if (radio == null || alarm == null || chains == null)
        {
            log.AppendLine("ERROR: a decoy prefab is missing under Prefabs/Decoys. Nothing was built.");
            return false;
        }

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool wasListed = scene.IsValid();
        bool wasLoaded = wasListed && scene.isLoaded;

        if (!wasLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        else if (scene.isDirty)
            log.AppendLine("Note: NemesisTestbed already had unsaved changes; they are saved together with the stations.");

        try
        {
            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.name != RootName) continue;
                Object.DestroyImmediate(rootObject);
                log.AppendLine("Removed the previous Decoy Stations.");
            }

            GameObject root = ObjectFactory.CreateGameObject(scene, HideFlags.None, RootName);
            if (root.scene != scene) SceneManager.MoveGameObjectToScene(root, scene);

            Place(radio, root.transform, "Decoy_Radio (SALA_LATERAL)", RadioPosition, 180f, log);
            Table(root.transform, scene);
            Sign(root.transform, scene, "Sign (radio)", RadioSign, new Vector3(-19f, 2.3f, 24.88f), Vector3.back);

            Place(alarm, root.transform, "Decoy_FireAlarm (PASILLO_CARGA)", AlarmPosition, 90f, log);
            Sign(root.transform, scene, "Sign (alarm)", AlarmSign, new Vector3(-6.88f, 2.5f, 40.5f), Vector3.right);

            Place(chains, root.transform, "Decoy_Chains (PASILLO_OESTE)", ChainsPosition, 90f, log);
            Sign(root.transform, scene, "Sign (chains)", ChainsSign, new Vector3(-9.88f, 2.3f, 32.2f), Vector3.right);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                log.AppendLine($"ERROR: could not save {ScenePath}.");
                return false;
            }

            log.AppendLine($"Saved {ScenePath}.");
            return true;
        }
        finally
        {
            if (!wasLoaded && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, !wasListed);
        }
    }

    private static void Place(GameObject prefab, Transform parent, string name, Vector3 position, float yaw,
                              StringBuilder log)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
        instance.transform.SetParent(parent, false);
        instance.name = name;
        instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance);

        Transform point = instance.transform.Find("InvestigatePoint");
        string where = point != null ? $", the Nemesis walks to {point.position}" : "";
        log.AppendLine($"{name} at {position}{where}.");
    }

    /// <summary>What the radio stands on. On Default, which the testbed does not bake: it is against
    /// the wall, out of every path, and the Nemesis stops a metre in front of it.</summary>
    private static void Table(Transform parent, Scene scene)
    {
        GameObject table = ObjectFactory.CreateGameObject(scene, HideFlags.None, "Radio table (Default, not baked)",
                                                          typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider));
        table.transform.SetParent(parent, false);
        table.transform.position = new Vector3(RadioPosition.x, TableHeight * 0.5f, RadioPosition.z);
        table.transform.localScale = new Vector3(0.9f, TableHeight, 0.5f);
        table.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

        var material = AssetDatabase.LoadAssetAtPath<Material>(PropMaterialPath);
        if (material != null) table.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>A sign on a wall face. <paramref name="towardsReader"/> points from the wall to whoever
    /// reads it; TextMeshPro reads from its -Z side.</summary>
    private static void Sign(Transform parent, Scene scene, string name, string text, Vector3 position,
                             Vector3 towardsReader)
    {
        GameObject labelObject = ObjectFactory.CreateGameObject(scene, HideFlags.None, name, typeof(TextMeshPro));
        labelObject.transform.SetParent(parent, false);
        labelObject.transform.position = position;
        labelObject.transform.rotation = Quaternion.LookRotation(-towardsReader);

        TextMeshPro label = labelObject.GetComponent<TextMeshPro>();
        label.text = text;
        label.fontSize = SignFontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.color = SignColor;
        label.rectTransform.sizeDelta = new Vector2(3.6f, 1.8f);
    }
}
