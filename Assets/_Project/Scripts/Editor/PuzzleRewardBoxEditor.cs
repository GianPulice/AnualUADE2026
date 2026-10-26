#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of <see cref="PuzzleRewardBox"/>: the puzzle dropdown is the only thing to set. Picking
/// an id links the puzzle asset that declares it (the box reads that asset's Reward Item), and the
/// box says what it will give, or why it will give nothing.
/// </summary>
[CustomEditor(typeof(PuzzleRewardBox))]
public class PuzzleRewardBoxEditor : Editor
{
    private SerializedProperty puzzleId;
    private SerializedProperty puzzleSource;
    private SerializedProperty rewardOverride;

    private void OnEnable()
    {
        puzzleId = serializedObject.FindProperty("puzzleId");
        puzzleSource = serializedObject.FindProperty("puzzleSource");
        rewardOverride = serializedObject.FindProperty("rewardOverride");

        // Opening the inspector re-links a box whose puzzle asset changed id or went away.
        if (targets.Length == 1 && IsStale()) Relink();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();
        DrawPropertiesExcluding(serializedObject, "m_Script", "puzzleSource");
        bool idChanged = EditorGUI.EndChangeCheck();

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(puzzleSource, new GUIContent("Puzzle Asset (auto)"));

        serializedObject.ApplyModifiedProperties();

        if (idChanged) Relink();

        if (targets.Length != 1) return;
        DrawSummary();
        DrawItemModel();
    }

    // ── Item model ──────────────────────────────────────────────────────────

    /// <summary>
    /// What the player sees inside the open box: whatever is under the rig's Item Visual. Dragging a
    /// model or prefab onto the field replaces it, scaled to fit. Item Visual stays switched off in
    /// Play until the box opens, so "Preview" can be left on while placing it.
    /// </summary>
    private void DrawItemModel()
    {
        var rig = ((PuzzleRewardBox)target).GetComponent<RewardBoxRig>();
        if (rig == null) return;

        var rigSo = new SerializedObject(rig);
        var visual = rigSo.FindProperty("itemVisual").objectReferenceValue as GameObject;
        float fitSize = rigSo.FindProperty("itemFitSize").floatValue;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Item inside the box", EditorStyles.boldLabel);

        if (visual == null)
        {
            EditorGUILayout.HelpBox("The rig has no Item Visual. Run Tools > Puzzles > Set Up Reward Boxes.",
                                    MessageType.Warning);
            return;
        }

        Transform holder = visual.transform;
        EditorGUILayout.LabelField("Current", holder.childCount > 0 ? holder.GetChild(0).name : "(none)");

        var model = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Item Model", "Drag a model or prefab here: it replaces what is inside, scaled " +
                                         "to fit. Colliders, rigidbodies and pickup scripts are removed."),
            null, typeof(GameObject), false);
        if (model != null) PutInBox(holder, model, fitSize);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(visual.activeSelf ? "Hide preview" : "Preview"))
            {
                Undo.RecordObject(visual, "Toggle item preview");
                visual.SetActive(!visual.activeSelf);
            }

            using (new EditorGUI.DisabledScope(holder.childCount == 0))
            {
                if (GUILayout.Button("Remove model")) ClearHolder(holder);
            }
        }

        EditorGUILayout.HelpBox("Hidden in Play until the box opens, even if left visible here.", MessageType.None);
    }

    private static void PutInBox(Transform holder, GameObject model, float fitSize)
    {
        Undo.SetCurrentGroupName($"Put {model.name} in the box");
        int group = Undo.GetCurrentGroup();

        if (!ClearHolder(holder)) return;

        var instance = PrefabUtility.InstantiatePrefab(model, holder) as GameObject;
        if (instance == null) instance = Instantiate(model, holder);
        Undo.RegisterCreatedObjectUndo(instance, "Put item in the box");

        StripPickup(instance);

        Transform t = instance.transform;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;

        // Scaled so its largest side is fitSize, then standing on the holder, centred.
        Bounds bounds = LocalBounds(holder, t);
        float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        float scale = largest > 0.0001f ? fitSize / largest : 1f;
        t.localScale = Vector3.one * scale;
        t.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;

        Undo.CollapseUndoOperations(group);
    }

    /// <summary>Removes what is inside. False when a part of it comes from the prefab and can only be
    /// changed there.</summary>
    private static bool ClearHolder(Transform holder)
    {
        for (int i = holder.childCount - 1; i >= 0; i--)
        {
            GameObject child = holder.GetChild(i).gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(child) && !PrefabUtility.IsAddedGameObjectOverride(child) &&
                PrefabUtility.GetOutermostPrefabInstanceRoot(child) != child)
            {
                Debug.LogWarning($"[{nameof(PuzzleRewardBoxEditor)}] '{child.name}' comes from the box prefab: " +
                                 "change it in the prefab (Caja Panel / Caja Teclado).", child);
                return false;
            }
            Undo.DestroyObjectImmediate(child);
        }
        return true;
    }

    /// <summary>A pickup dragged in would bring its own interaction (taking it would skip the box),
    /// its gravity and its colliders. Only the look stays.</summary>
    private static void StripPickup(GameObject instance)
    {
        foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour is IInteractable || behaviour is ItemProximityHighlight || behaviour is ItemGlint)
                Undo.DestroyObjectImmediate(behaviour);
        }
        foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true)) Undo.DestroyObjectImmediate(body);
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) Undo.DestroyObjectImmediate(collider);
    }

    /// <summary>Bounds of the meshes under <paramref name="part"/> in <paramref name="space"/>'s local
    /// space, read from the meshes so it works on a switched-off object too.</summary>
    private static Bounds LocalBounds(Transform space, Transform part)
    {
        Bounds bounds = default;
        bool any = false;
        Matrix4x4 toSpace = space.worldToLocalMatrix;

        void Add(Mesh mesh, Transform owner)
        {
            if (mesh == null) return;
            Matrix4x4 m = toSpace * owner.localToWorldMatrix;
            Bounds local = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }

        foreach (MeshFilter filter in part.GetComponentsInChildren<MeshFilter>(true)) Add(filter.sharedMesh, filter.transform);
        foreach (SkinnedMeshRenderer skinned in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            Add(skinned.sharedMesh, skinned.transform);
        return bounds;
    }

    private bool IsStale()
    {
        string id = puzzleId.stringValue;
        var source = puzzleSource.objectReferenceValue as IPuzzleRewardSource;
        if (string.IsNullOrWhiteSpace(id)) return puzzleSource.objectReferenceValue != null;
        return source == null || !string.Equals(source.PuzzleId, id, StringComparison.Ordinal);
    }

    private void Relink()
    {
        serializedObject.Update();
        ScriptableObject found = FindRewardSource(puzzleId.stringValue);
        if (puzzleSource.objectReferenceValue == found) return;

        puzzleSource.objectReferenceValue = found;
        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSummary()
    {
        string id = puzzleId.stringValue;
        if (string.IsNullOrWhiteSpace(id))
        {
            EditorGUILayout.HelpBox("Pick the puzzle that opens this box.", MessageType.Warning);
            return;
        }

        var item = rewardOverride.objectReferenceValue as SO_InventoryItem;
        string origin = "override";
        if (item == null && puzzleSource.objectReferenceValue is IPuzzleRewardSource source)
        {
            item = source.RewardItem;
            origin = puzzleSource.objectReferenceValue.name;
        }

        if (item != null)
            EditorGUILayout.HelpBox($"Opens when '{id}' is solved and gives {item.ItemName} ({origin}).",
                                    MessageType.Info);
        else
            EditorGUILayout.HelpBox($"'{id}' has no reward item: the box opens empty.", MessageType.Warning);

        if (item != null && item.WorldPickupPrefab == null)
            EditorGUILayout.HelpBox($"{item.ItemName} has no World Pickup Prefab: nothing shows inside the box.",
                                    MessageType.None);

        var self = (PuzzleRewardBox)target;
        foreach (PuzzleRewardBox other in FindObjectsByType<PuzzleRewardBox>(FindObjectsInactive.Include))
        {
            if (other == self || other.gameObject.scene != self.gameObject.scene) continue;
            if (!string.Equals(other.PuzzleId, id, StringComparison.Ordinal)) continue;
            EditorGUILayout.HelpBox($"'{other.name}' is also a box for '{id}'. Only one of them gets the reward.",
                                    MessageType.Error);
        }
    }

    /// <summary>The puzzle asset that declares <paramref name="puzzleId"/> and can hand out a reward,
    /// or null. Searched by the types that implement <see cref="IPuzzleRewardSource"/>.</summary>
    public static ScriptableObject FindRewardSource(string puzzleId)
    {
        if (string.IsNullOrWhiteSpace(puzzleId)) return null;

        foreach (Type type in TypeCache.GetTypesDerivedFrom<IPuzzleRewardSource>())
        {
            if (!typeof(ScriptableObject).IsAssignableFrom(type) || type.IsAbstract) continue;

            foreach (string guid in AssetDatabase.FindAssets($"t:{type.Name}"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset is IPuzzleRewardSource source &&
                    string.Equals(source.PuzzleId, puzzleId, StringComparison.Ordinal))
                    return asset;
            }
        }
        return null;
    }
}
#endif
