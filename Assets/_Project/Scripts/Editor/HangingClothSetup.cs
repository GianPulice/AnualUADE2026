using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Turns the curtains of the open scene into cloth: every panel of <c>Cortina.fbx</c> (its two cloth
/// meshes, not the rod) gets a <see cref="HangingCloth"/> with the shared config, and loses its static
/// flags (a batched mesh cannot be bent). The model is made readable, and the config asset is created
/// the first time.
///
/// Left alone, and listed: a curtain that has a solid collider on it or above it (nobody walks through
/// that one, so there is nothing to push it), and panels that already have the component.
///
/// One Undo step, re-runnable, and it does NOT save the scene.
/// </summary>
public static class HangingClothSetup
{
    private const string MenuPath = "Tools/Level/Setup Hanging Cloth (Cortinas)";
    private const string LogTag = "[HangingClothSetup]";
    private const string ModelPath = "Assets/_Project/Art/Models/Environment/Cortina.fbx";
    private const string ConfigFolder = "Assets/_Project/ScriptableObjects/Environment";
    private const string ConfigPath = ConfigFolder + "/SO_HangingCloth_Curtain.asset";

    /// <summary>The rod: part of the model, not cloth.</summary>
    private const string RodMeshName = "Soporte";

    [MenuItem(MenuPath, true)]
    private static bool CanRun() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(MenuPath)]
    private static void Run()
    {
        var log = new StringBuilder();

        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"{LogTag} {ModelPath} is not there. Nothing was done.");
            return;
        }

        if (!importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
            log.AppendLine("  Cortina.fbx: Read/Write turned on (the cloth reads its vertices).");
        }

        SO_HangingClothConfig config = AssetDatabase.LoadAssetAtPath<SO_HangingClothConfig>(ConfigPath);
        if (config == null)
        {
            if (!AssetDatabase.IsValidFolder(ConfigFolder))
                AssetDatabase.CreateFolder("Assets/_Project/ScriptableObjects", "Environment");

            config = ScriptableObject.CreateInstance<SO_HangingClothConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            log.AppendLine($"  created {ConfigPath}");
        }

        var clothMeshes = new HashSet<Mesh>(AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Mesh>()
                                                         .Where(m => m.name != RodMeshName));

        Undo.SetCurrentGroupName("Setup hanging cloth");
        int group = Undo.GetCurrentGroup();
        int added = 0, already = 0, solid = 0;

        foreach (MeshFilter filter in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
        {
            if (filter.sharedMesh == null || !clothMeshes.Contains(filter.sharedMesh)) continue;

            if (filter.GetComponent<HangingCloth>() != null)
            {
                already++;
                continue;
            }

            Collider blocker = filter.GetComponentsInParent<Collider>(true).FirstOrDefault(c => !c.isTrigger);
            if (blocker != null)
            {
                solid++;
                log.AppendLine($"  left still: {PathOf(filter.transform)} (solid collider on '{blocker.name}': nobody walks through it)");
                continue;
            }

            Undo.RecordObject(filter.gameObject, "Clear static flags");
            GameObjectUtility.SetStaticEditorFlags(filter.gameObject, 0);

            HangingCloth cloth = Undo.AddComponent<HangingCloth>(filter.gameObject);
            var serialized = new SerializedObject(cloth);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            added++;
        }

        Undo.CollapseUndoOperations(group);
        if (added > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        Debug.Log($"{LogTag} {added} curtain panel(s) are cloth now, {already} already were, {solid} left still. " +
                  $"Scene not saved; Ctrl+Z undoes the scene part.\n{log}");
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
