using System.Collections.Generic;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Moves level geometry that ended up on layer Default over to Props, so the NavMesh bake picks it
/// up without Default having to be in the surface's layer mask.
///
/// Why this exists: the bake collects Physics Colliders on Ground, Wall and Props — the same three
/// layers <see cref="NemesisSetupValidator"/> already treats as "the navigation layers". Adding
/// Default to that mask does make new props block the agent, but it also drags in every door leaf
/// and frame, which bakes the doorways shut and leaves the Nemesis treating each door as a wall.
/// Putting the geometry on Props instead gets the blocking without the doors.
///
/// Scope, deliberately narrow: only objects whose mesh comes from an <b>.fbx</b> and that carry a
/// solid (non-trigger) collider. A collider is what the bake reads, and the .fbx test is what
/// separates imported level geometry from triggers, gameplay volumes and hand-made helper objects.
/// Doors, interactables, the player, the Nemesis, moving physics props and anything already marked
/// to be ignored by the bake are skipped — see <see cref="ShouldSkip"/>.
///
/// Changes are made on the scene instances, as prefab overrides, and are undoable. Preview first,
/// then apply.
/// </summary>
public static class NavMeshLayerFixer
{
    private const string TargetLayerName = "Props";

    /// <summary>The layers the bake should collect, and the ones the project's own Nemesis
    /// validator expects. Default is pointedly absent.</summary>
    private static readonly string[] NavLayerNames = { "Ground", "Wall", "Props" };

    [MenuItem("WIRED/NavMesh/Preview: Default FBX geometry to move to Props")]
    private static void Preview() => Run(apply: false);

    [MenuItem("WIRED/NavMesh/Apply: move Default FBX geometry to Props")]
    private static void Apply() => Run(apply: true);

    [MenuItem("WIRED/NavMesh/Set NavMeshSurface layers to Ground, Wall, Props")]
    private static void FixSurfaceMask()
    {
        NavMeshSurface[] surfaces = Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include);

        if (surfaces.Length == 0)
        {
            Debug.LogWarning($"[{nameof(NavMeshLayerFixer)}] No NavMeshSurface in the open scene.");
            return;
        }

        int mask = LayerMask.GetMask(NavLayerNames);

        foreach (NavMeshSurface surface in surfaces)
        {
            Undo.RecordObject(surface, "Set NavMesh layers");
            surface.layerMask = mask;
            EditorUtility.SetDirty(surface);

            Debug.Log($"[{nameof(NavMeshLayerFixer)}] '{surface.name}' layerMask set to " +
                      $"{string.Join(", ", NavLayerNames)} (value {mask}). Bake to apply.", surface);
        }

        EditorSceneManager.MarkAllScenesDirty();
    }

    private static void Run(bool apply)
    {
        List<GameObject> targets = Collect();

        if (targets.Count == 0)
        {
            Debug.Log($"[{nameof(NavMeshLayerFixer)}] Nothing to move: no NEW .fbx geometry with " +
                      "a solid collider is left on Default.");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine($"[{nameof(NavMeshLayerFixer)}] {targets.Count} object(s): new since the " +
                          $"art pass, on Default, .fbx mesh, solid collider" +
                          $"{(apply ? " — MOVED to Props" : " — preview only")}:");

        const int MaxListed = 60;
        int shown = Mathf.Min(targets.Count, MaxListed);
        for (int i = 0; i < shown; i++) report.AppendLine($"  {Path(targets[i])}");

        if (targets.Count > shown) report.AppendLine($"  … and {targets.Count - shown} more.");

        if (apply)
        {
            int layer = LayerMask.NameToLayer(TargetLayerName);
            if (layer < 0)
            {
                Debug.LogError($"[{nameof(NavMeshLayerFixer)}] No layer named '{TargetLayerName}'.");
                return;
            }

            // One undo group, so a wrong call is a single Ctrl+Z and not 300 of them.
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Move Default geometry to Props");
            int group = Undo.GetCurrentGroup();

            foreach (GameObject go in targets)
            {
                Undo.RecordObject(go, "Move to Props");
                go.layer = layer;
                EditorUtility.SetDirty(go);
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkAllScenesDirty();

            report.AppendLine("Now: WIRED ▸ NavMesh ▸ Set NavMeshSurface layers…, then Bake.");
        }

        Debug.Log(report.ToString());
    }

    /// <summary>
    /// Restricts the pass to the objects that arrived with the art pass, instead of every piece of
    /// Default geometry in the level.
    ///
    /// The ids were produced by diffing the scene YAML against commit deeb0df2 (2026-09-11), the
    /// last state before GianPulice's art commits. Older geometry is left exactly as it is: the
    /// NavMesh those areas were baked with is the one the patrol routes were authored against, and
    /// silently thickening it is not this utility's job.
    ///
    /// Matching is by scene id, not by name: half of these props are called Barrel_1 or
    /// Pipes_out_2 (3), and name matching would sweep up the older copies sitting next to them.
    /// </summary>
    private static HashSet<ulong> LoadNewObjectIds()
    {
        string path = System.IO.Path.Combine(Application.dataPath,
                                             "_Project/Scripts/Editor/NavMeshNewObjectIds.txt");

        var ids = new HashSet<ulong>();
        if (!System.IO.File.Exists(path))
        {
            Debug.LogWarning($"[{nameof(NavMeshLayerFixer)}] '{path}' is missing, so the pass " +
                             "cannot tell new geometry from old and would touch the whole level. " +
                             "Nothing was collected.");
            return ids;
        }

        foreach (string line in System.IO.File.ReadAllLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
            if (ulong.TryParse(trimmed, out ulong id)) ids.Add(id);
        }

        return ids;
    }

    /// <summary>
    /// True when this object is one of the listed new ones.
    ///
    /// A prefab instance answers with the id of the PrefabInstance it belongs to (every child of a
    /// placed Barrel_1 shares it), a plain scene object with its own id — which is exactly how the
    /// two lists in the file were built.
    /// </summary>
    private static bool IsNew(GameObject go, HashSet<ulong> newIds)
    {
        GlobalObjectId gid = GlobalObjectId.GetGlobalObjectIdSlow(go);

        return newIds.Contains(gid.targetPrefabId) || newIds.Contains(gid.targetObjectId);
    }

    private static List<GameObject> Collect()
    {
        var targets = new List<GameObject>();
        HashSet<ulong> newIds = LoadNewObjectIds();
        if (newIds.Count == 0) return targets;

        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    GameObject go = t.gameObject;

                    if (go.layer != 0) continue;
                    if (!HasSolidCollider(go)) continue;
                    if (!ComesFromFbx(go)) continue;
                    if (ShouldSkip(go)) continue;
                    if (!IsNew(go, newIds)) continue;

                    targets.Add(go);
                }
            }
        }

        return targets;
    }

    /// <summary>A non-trigger collider ON this object: that, and only that, is what the bake reads
    /// when the surface collects Physics Colliders.</summary>
    private static bool HasSolidCollider(GameObject go)
    {
        foreach (Collider collider in go.GetComponents<Collider>())
        {
            if (collider != null && collider.enabled && !collider.isTrigger) return true;
        }

        return false;
    }

    /// <summary>True when the mesh this object renders or collides with lives inside an .fbx.
    /// Checked through the mesh asset rather than the prefab source, because the mesh is what
    /// actually came out of the model importer no matter how many prefabs it was wrapped in.</summary>
    private static bool ComesFromFbx(GameObject go)
    {
        var filter = go.GetComponent<MeshFilter>();
        if (filter != null && IsFbxAsset(filter.sharedMesh)) return true;

        var meshCollider = go.GetComponent<MeshCollider>();
        if (meshCollider != null && IsFbxAsset(meshCollider.sharedMesh)) return true;

        var skinned = go.GetComponent<SkinnedMeshRenderer>();
        return skinned != null && IsFbxAsset(skinned.sharedMesh);
    }

    private static bool IsFbxAsset(Mesh mesh)
    {
        if (mesh == null) return false;

        string path = AssetDatabase.GetAssetPath(mesh);
        return !string.IsNullOrEmpty(path) && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Things that must keep their layer even though they match everything else.
    ///
    /// Doors are the whole reason this utility exists: on Props they bake shut again. Interactables
    /// in general are left alone because their layer is what the interaction raycast filters on.
    /// The player and the Nemesis move, so baking them is meaningless, and a non-kinematic
    /// Rigidbody means the object is expected to be shoved around at runtime. Anything under a
    /// NavMeshModifier set to ignoreFromBuild was deliberately excluded from the bake already.
    /// </summary>
    private static bool ShouldSkip(GameObject go)
    {
        if (go.GetComponentInParent<IInteractable>() != null) return true;
        if (go.GetComponentInParent<NavMeshAgent>() != null) return true;
        if (go.GetComponentInParent<CharacterController>() != null) return true;

        var body = go.GetComponentInParent<Rigidbody>();
        if (body != null && !body.isKinematic) return true;

        foreach (NavMeshModifier modifier in go.GetComponentsInParent<NavMeshModifier>(includeInactive: true))
        {
            if (modifier.ignoreFromBuild) return true;
        }

        return false;
    }

    private static string Path(GameObject go)
    {
        var path = new StringBuilder(go.name);
        for (Transform t = go.transform.parent; t != null; t = t.parent)
            path.Insert(0, t.name + "/");

        return path.ToString();
    }
}
