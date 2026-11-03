using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Two layouts for one scene's hierarchy, switched with the buttons in this asset's inspector.
///
///   Flatten   every organiser folder gives up its children to the scene root. Transforms under one
///             root share one hierarchy buffer, so anything moving under a big folder costs for the
///             whole folder and the job system cannot split it. Separate roots avoid both.
///   Restore   puts the organised layout back, exact: parents, order and local values. It is
///             recorded by Flatten, in this asset, right before it touches anything.
///
/// A folder is listed by hand or by <see cref="DetectFolders"/>, which must run while the prefabs in
/// the scene are still prefabs: once unpacked, a prefab's empty root looks like a folder, and scripts
/// do read those (BasketTrigger takes its parent as the basket). Flatten re-checks every listed folder
/// and keeps whole any that has grown a component, is switched off, has a non-uniform scale or is
/// referenced from the scene. Its children cannot leave an inactive parent without turning on, and a
/// referenced one is being read by someone.
///
/// Objects are identified by GlobalObjectId, which needs them saved in the scene: Flatten saves first.
/// Objects created after a Flatten are not in the record: Restore leaves them where they are and
/// reports the count.
/// </summary>
[CreateAssetMenu(fileName = "SO_SceneHierarchyLayout", menuName = "Scriptable Objects/Levels/Scene Hierarchy Layout")]
public class SO_SceneHierarchyLayout : ScriptableObject
{
    [Serializable]
    private struct Entry
    {
        public string id;
        public string parentId;
        public int sibling;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }

    [Tooltip("The scene this layout belongs to. The buttons only work with it open and active.")]
    [SerializeField] private SceneAsset scene;

    [Tooltip("GlobalObjectIds of the organiser folders that Flatten empties.")]
    [SerializeField] private List<string> folderIds = new List<string>();

    [Tooltip("True while the scene is in the flat layout.")]
    [SerializeField] private bool isFlattened;

    [SerializeField, HideInInspector] private List<Entry> organisedLayout = new List<Entry>();

    public bool IsFlattened => isFlattened;
    public int FolderCount => folderIds.Count;
    public int RecordedObjects => organisedLayout.Count;

    public bool SceneIsOpen(out Scene active)
    {
        active = SceneManager.GetActiveScene();
        return scene != null && active.path == AssetDatabase.GetAssetPath(scene);
    }

    /// <summary>
    /// Lists every GameObject reachable from the roots through empty, non-prefab objects with
    /// children. Stops at the first object that is not one: its subtree belongs to it.
    /// </summary>
    public void DetectFolders()
    {
        if (!RequireScene(out Scene active)) return;

        List<GameObject> folders = new List<GameObject>();
        foreach (GameObject root in active.GetRootGameObjects()) CollectFolders(root, folders);

        folderIds = new List<string>(IdsOf(folders));
        Save();

        Debug.Log($"[{nameof(SO_SceneHierarchyLayout)}] {folders.Count} folders detected in '{active.name}'.", this);
    }

    private static void CollectFolders(GameObject go, List<GameObject> folders)
    {
        if (go.transform.childCount == 0 || PrefabUtility.IsPartOfAnyPrefab(go)) return;
        if (go.GetComponents<Component>().Length != 1) return;

        folders.Add(go);
        foreach (Transform child in go.transform) CollectFolders(child.gameObject, folders);
    }

    /// <summary>Records the layout as it is and empties the folders into the root.</summary>
    public void Flatten()
    {
        if (!RequireScene(out Scene active)) return;

        if (isFlattened)
        {
            Debug.LogWarning($"[{nameof(SO_SceneHierarchyLayout)}] Already flat: Restore first.", this);
            return;
        }

        // GlobalObjectIds of objects that were never saved are not stable.
        if (active.isDirty) EditorSceneManager.SaveScene(active);

        HashSet<GameObject> folders = ResolveFolders(active, out int kept);
        List<GameObject> walked = new List<GameObject>();
        List<GameObject> parents = new List<GameObject>();

        foreach (GameObject root in active.GetRootGameObjects()) Walk(root, null, folders, walked, parents);

        Record(walked, parents);

        int moved = 0;
        foreach (GameObject go in walked)
        {
            if (folders.Contains(go) || go.transform.parent == null) continue;

            go.transform.SetParent(null, true);
            moved++;
        }

        isFlattened = true;
        Save();
        EditorSceneManager.MarkSceneDirty(active);

        Debug.Log($"[{nameof(SO_SceneHierarchyLayout)}] Flat: {moved} objects moved to the root, " +
                  $"{folders.Count} folders emptied, {kept} kept whole. {active.rootCount} roots. " +
                  "Save the scene to keep it.", this);
    }

    // Parents always before their children, siblings in order: Restore replays this same order.
    private static void Walk(GameObject go, GameObject parent, HashSet<GameObject> folders,
                             List<GameObject> walked, List<GameObject> parents)
    {
        walked.Add(go);
        parents.Add(parent);

        if (!folders.Contains(go)) return;
        foreach (Transform child in go.transform) Walk(child.gameObject, go, folders, walked, parents);
    }

    private void Record(List<GameObject> walked, List<GameObject> parents)
    {
        string[] ids = IdsOf(walked);
        Dictionary<GameObject, string> idOf = new Dictionary<GameObject, string>(walked.Count);
        for (int i = 0; i < walked.Count; i++) idOf[walked[i]] = ids[i];

        organisedLayout = new List<Entry>(walked.Count);
        for (int i = 0; i < walked.Count; i++)
        {
            Transform t = walked[i].transform;
            organisedLayout.Add(new Entry
            {
                id = ids[i],
                parentId = parents[i] != null ? idOf[parents[i]] : string.Empty,
                sibling = t.GetSiblingIndex(),
                localPosition = t.localPosition,
                localRotation = t.localRotation,
                localScale = t.localScale,
            });
        }
    }

    /// <summary>Puts the recorded layout back.</summary>
    public void Restore()
    {
        if (!RequireScene(out Scene active)) return;

        if (!isFlattened || organisedLayout.Count == 0)
        {
            Debug.LogWarning($"[{nameof(SO_SceneHierarchyLayout)}] Nothing to restore: the scene is " +
                             "already in its organised layout.", this);
            return;
        }

        string[] ids = new string[organisedLayout.Count];
        for (int i = 0; i < ids.Length; i++) ids[i] = organisedLayout[i].id;
        GameObject[] objects = Resolve(ids);

        Dictionary<string, Transform> byId = new Dictionary<string, Transform>(ids.Length);
        for (int i = 0; i < ids.Length; i++)
            if (objects[i] != null) byId[ids[i]] = objects[i].transform;

        int missing = 0;
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] == null)
            {
                missing++;
                continue;
            }

            Entry entry = organisedLayout[i];
            Transform t = objects[i].transform;

            // A folder deleted while flat: the child stays at the root instead of being lost.
            byId.TryGetValue(entry.parentId, out Transform parent);

            t.SetParent(parent, false);
            t.SetLocalPositionAndRotation(entry.localPosition, entry.localRotation);
            t.localScale = entry.localScale;
            t.SetSiblingIndex(entry.sibling);
        }

        isFlattened = false;
        Save();
        EditorSceneManager.MarkSceneDirty(active);

        int recordedRoots = 0;
        foreach (Entry entry in organisedLayout)
            if (string.IsNullOrEmpty(entry.parentId)) recordedRoots++;

        Debug.Log($"[{nameof(SO_SceneHierarchyLayout)}] Restored {objects.Length - missing} objects " +
                  $"({missing} recorded objects no longer exist). {active.rootCount} roots, " +
                  $"{active.rootCount - recordedRoots} of them not in the record. Save the scene to keep it.", this);
    }

    private HashSet<GameObject> ResolveFolders(Scene active, out int kept)
    {
        HashSet<GameObject> referenced = FindReferencedObjects(active);
        HashSet<GameObject> folders = new HashSet<GameObject>();
        kept = 0;

        foreach (GameObject go in Resolve(folderIds.ToArray()))
        {
            if (go == null) continue;

            string reason = WhyNotEmptied(go, referenced);
            if (reason == null)
            {
                folders.Add(go);
                continue;
            }

            kept++;
            Debug.Log($"[{nameof(SO_SceneHierarchyLayout)}] Kept whole: '{go.name}' ({reason}).", go);
        }

        return folders;
    }

    private static string WhyNotEmptied(GameObject go, HashSet<GameObject> referenced)
    {
        if (go.GetComponents<Component>().Length != 1) return "has components";
        if (!go.activeSelf) return "switched off";
        if (referenced.Contains(go)) return "referenced from the scene";

        Vector3 scale = go.transform.lossyScale;
        bool uniform = Mathf.Approximately(scale.x, scale.y) && Mathf.Approximately(scale.y, scale.z);
        return uniform ? null : "non-uniform scale";
    }

    /// <summary>Every GameObject some component in the scene points at, itself or through its
    /// Transform. A component pointing inside its own object does not count: it is not a folder.</summary>
    private static HashSet<GameObject> FindReferencedObjects(Scene active)
    {
        HashSet<GameObject> referenced = new HashSet<GameObject>();

        foreach (GameObject root in active.GetRootGameObjects())
        {
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform) continue;

                SerializedObject serialized = new SerializedObject(component);
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;

                    GameObject target = property.objectReferenceValue switch
                    {
                        GameObject go => go,
                        Component other => other.gameObject,
                        _ => null,
                    };

                    if (target != null && target != component.gameObject) referenced.Add(target);
                }
            }
        }

        return referenced;
    }

    private static string[] IdsOf(List<GameObject> objects)
    {
        GlobalObjectId[] ids = new GlobalObjectId[objects.Count];
        GlobalObjectId.GetGlobalObjectIdsSlow(objects.ToArray(), ids);

        string[] result = new string[ids.Length];
        for (int i = 0; i < ids.Length; i++) result[i] = ids[i].ToString();
        return result;
    }

    private static GameObject[] Resolve(string[] ids)
    {
        GlobalObjectId[] parsed = new GlobalObjectId[ids.Length];
        for (int i = 0; i < ids.Length; i++) GlobalObjectId.TryParse(ids[i], out parsed[i]);

        UnityEngine.Object[] found = new UnityEngine.Object[ids.Length];
        GlobalObjectId.GlobalObjectIdentifiersToObjectsSlow(parsed, found);

        GameObject[] result = new GameObject[ids.Length];
        for (int i = 0; i < ids.Length; i++) result[i] = found[i] as GameObject;
        return result;
    }

    private bool RequireScene(out Scene active)
    {
        if (Application.isPlaying)
        {
            active = default;
            Debug.LogWarning($"[{nameof(SO_SceneHierarchyLayout)}] Not in Play mode.", this);
            return false;
        }

        if (SceneIsOpen(out active)) return true;

        Debug.LogWarning($"[{nameof(SO_SceneHierarchyLayout)}] Open '{(scene != null ? scene.name : "the scene")}' " +
                         "and make it the active scene first.", this);
        return false;
    }

    private void Save()
    {
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssetIfDirty(this);
    }
}

[CustomEditor(typeof(SO_SceneHierarchyLayout))]
public class SO_SceneHierarchyLayoutEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SO_SceneHierarchyLayout layout = (SO_SceneHierarchyLayout)target;
        bool ready = layout.SceneIsOpen(out _) && !Application.isPlaying;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            (layout.IsFlattened ? "The scene is FLAT." : "The scene is ORGANISED.") +
            $" {layout.FolderCount} folders listed, {layout.RecordedObjects} objects recorded." +
            (ready ? string.Empty : "\nOpen the scene and make it active to use the buttons."),
            MessageType.Info);

        using (new EditorGUI.DisabledScope(!ready))
        {
            using (new EditorGUI.DisabledScope(layout.IsFlattened))
            {
                if (GUILayout.Button("Flatten hierarchy (performance)", GUILayout.Height(28))) layout.Flatten();
            }

            using (new EditorGUI.DisabledScope(!layout.IsFlattened))
            {
                if (GUILayout.Button("Restore organised hierarchy", GUILayout.Height(28))) layout.Restore();
            }
        }
    }
}
