using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Re-applies to <c>WIRED_Zona1_Blockout</c> the scene changes made after the 04/11 playday, so they
/// survive a merge where the scene is taken whole from the other side. Safe to run any number of
/// times: it finds each object by name (and, where the name repeats, by where it is), puts it where
/// it belongs and leaves everything else alone. Run it with the scene open, check, save.
///
/// What it does:
///   • The Stairs Key and the trash can it lies on: next to the fuse box (CajaFusible), in ELECTRICITY.
///   • Note (2), the left valve note: in the knocked-over toilet of the upstairs bathroom.
///   • A key hook on the bathroom anteroom wall (Concrete_miniwall_short (5)) with the Valve Key
///     hanging from it (its Rigidbody kinematic, or it would fall off).
///   • The UV warning sign at the box room entrance (see <see cref="SignPosition"/>).
///   • The box room's Architect line (ARC_CTX_06) waits 6 s inside the room before playing.
/// </summary>
public static class Zona1PlaydayLayout
{
    private const string ScenePath  = "Assets/_Project/Scenes/GameScenes/WIRED_Zona1_Blockout.unity";
    private const string HookPrefab = "Assets/_Project/Prefabs/Environment/KeyHook.prefab";
    private const string SignPrefab = "Assets/_Project/Prefabs/Environment/UVWarningSign.prefab";
    private const string PropsParent = "---- PROPS SUELTOS LV1 ----/PROPS";

    // Trash can + Stairs Key: from the cabinets by the ELECTRICITY door to beside the fuse box.
    private static readonly Vector3 TrashOld = new Vector3(-23.882f, 0f, 17.716f);
    private static readonly Vector3 TrashNew = new Vector3(-29.35f, 0f, 24.1f);
    // The key's pose on the can as Iñaki left it on 09/11.
    private static readonly Vector3 StairsKeyPosition = new Vector3(-29.322f, 0.889f, 24.1028f);
    private static readonly Vector3 StairsKeyEuler = new Vector3(358.9064f, 75.0387f, 355.9152f);

    // Where the Valve Key used to be, inside the knocked-over toilet: the left valve note now.
    // Pose as Iñaki adjusted it on 09/11.
    private static readonly Vector3 NotePosition = new Vector3(-26.3148f, 5.579f, 20.3093f);
    private static readonly Vector3 NoteEuler = new Vector3(352.4465f, 239.7103f, 4.3907f);

    // Hook: on the west face of the anteroom's mini wall, 1.4 m above the upstairs floor (y 5).
    private const float HookHeight = 6.4f;
    private const float HookZ = 22.93f;   // where Iñaki left the key by the wall on 09/11

    // UV warning sign: where the hallway door into the box room was (DoorMetalRed (6), walled up on
    // 09/11), on the hallway side of the wall at z 17, facing the hallway.
    private static readonly bool PlaceSign = true;
    private static readonly Vector3 SignPosition = new Vector3(0f, 1.7f, 16.89f);
    private static readonly Vector3 SignFacing = Vector3.back;

    [MenuItem("Tools/Levels/Zona 1/Apply Playday Layout")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            EditorUtility.DisplayDialog("Apply Playday Layout", "Open WIRED_Zona1_Blockout first.", "OK");
            return;
        }

        int changes = 0;
        changes += MoveTrashAndStairsKey();
        changes += Place("Note (2)", NotePosition, Quaternion.Euler(NoteEuler));
        changes += HangValveKey();
        if (PlaceSign) changes += PlaceUvSign();
        changes += DelayBoxRoomLine();

        if (changes > 0) EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Zona1PlaydayLayout] {changes} change(s) applied. Check them and save the scene.");
    }

    private static int MoveTrashAndStairsKey()
    {
        GameObject can = NearestNamed("TrashCan", TrashOld, 0.6f) ?? NearestNamed("TrashCan", TrashNew, 0.6f);
        if (can == null) { Debug.LogWarning("[Zona1PlaydayLayout] TrashCan by ELECTRICITY not found."); return 0; }
        int n = SetPosition(can, TrashNew);
        n += Place("InventoryItemKeyStairs", StairsKeyPosition, Quaternion.Euler(StairsKeyEuler));
        return n;
    }

    private static int HangValveKey()
    {
        GameObject wall = GameObject.Find("Concrete_miniwall_short (5)");
        if (wall == null) { Debug.LogWarning("[Zona1PlaydayLayout] Concrete_miniwall_short (5) not found."); return 0; }
        var wallCollider = wall.GetComponent<Collider>();
        Bounds wb = wallCollider != null ? wallCollider.bounds : wall.GetComponent<Renderer>().bounds;

        int n = 0;
        GameObject hook = GameObject.Find("KeyHook_Bathroom");
        if (hook == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HookPrefab);
            if (prefab == null) { Debug.LogWarning("[Zona1PlaydayLayout] KeyHook prefab missing."); return 0; }
            hook = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            // Zona 1 keeps script-less decoration unpacked.
            PrefabUtility.UnpackPrefabInstance(hook, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            hook.name = "KeyHook_Bathroom";
            GameObject parent = GameObject.Find(PropsParent);
            if (parent != null) hook.transform.SetParent(parent.transform, true);
            Undo.RegisterCreatedObjectUndo(hook, "Key hook");
            n++;
        }
        n += SetPose(hook, new Vector3(wb.min.x, HookHeight, Mathf.Clamp(HookZ, wb.min.z + 0.2f, wb.max.z - 0.2f)),
                     Quaternion.LookRotation(Vector3.left, Vector3.up));

        GameObject key = GameObject.Find("InventoryItemKeyValve");
        if (key == null) { Debug.LogWarning("[Zona1PlaydayLayout] InventoryItemKeyValve not found."); return n; }

        var rb = key.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            Undo.RecordObject(rb, "Key kinematic");
            rb.isKinematic = true;
            rb.useGravity = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rb);
            n++;
        }

        // Hang it: its longest side down, its thinnest side against the wall.
        var mf = key.GetComponentInChildren<MeshFilter>();
        Vector3 size = mf.sharedMesh.bounds.size;
        Vector3 longAxis = AxisOf(size, largest: true), thinAxis = AxisOf(size, largest: false);
        Quaternion meshToKey = Quaternion.LookRotation(thinAxis, longAxis);
        Quaternion rot = Quaternion.LookRotation(Vector3.left, Vector3.up) * Quaternion.Inverse(meshToKey);
        Transform anchor = hook.transform.Find("KeyAnchor");
        Vector3 anchorPos = anchor != null ? anchor.position : hook.transform.position + Vector3.left * 0.05f;

        // Turn it first, then move it so the top of the key sits on the arm, centred under it.
        Vector3 before = key.transform.position;
        Quaternion beforeRot = key.transform.rotation;
        key.transform.SetPositionAndRotation(anchorPos, rot);
        Bounds kb = key.GetComponentInChildren<Renderer>().bounds;
        Vector3 target = anchorPos + new Vector3(anchorPos.x - kb.center.x, anchorPos.y + 0.01f - kb.max.y,
                                                 anchorPos.z - kb.center.z);
        key.transform.SetPositionAndRotation(before, beforeRot);
        n += SetPose(key, target, rot);
        return n;
    }

    private static int PlaceUvSign()
    {
        GameObject sign = GameObject.Find("UVWarningSign_Boxes");
        if (sign == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SignPrefab);
            if (prefab == null) { Debug.LogWarning("[Zona1PlaydayLayout] UVWarningSign prefab missing."); return 0; }
            sign = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(sign, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            sign.name = "UVWarningSign_Boxes";
            GameObject parent = GameObject.Find(PropsParent);
            if (parent != null) sign.transform.SetParent(parent.transform, true);
            Undo.RegisterCreatedObjectUndo(sign, "UV sign");
        }
        return SetPose(sign, SignPosition, Quaternion.LookRotation(SignFacing, Vector3.up));
    }

    /// <summary>
    /// ARC_CTX_06 ("To solve this, you need another perspective") waits until the player has been
    /// in the box room a few seconds instead of playing on the doorstep (Iñaki, 09/11).
    /// </summary>
    private static int DelayBoxRoomLine()
    {
        int n = 0;
        foreach (var trigger in Object.FindObjectsByType<ArchitectZoneTrigger>(FindObjectsInactive.Include))
        {
            var so = new SerializedObject(trigger);
            if (so.FindProperty("line").intValue != (int)ArchitectLineID.ContextBoxRoom) continue;
            SerializedProperty delay = so.FindProperty("delaySeconds");
            if (Mathf.Approximately(delay.floatValue, BoxRoomLineDelay)) continue;
            delay.floatValue = BoxRoomLineDelay;
            so.ApplyModifiedProperties();
            n++;
        }
        return n;
    }

    private const float BoxRoomLineDelay = 6f;

    // ── helpers ──────────────────────────────────────────────────────────────────────

    private static int Place(string name, Vector3 position, Quaternion? rotation)
    {
        GameObject go = GameObject.Find(name);
        if (go == null) { Debug.LogWarning($"[Zona1PlaydayLayout] '{name}' not found."); return 0; }
        return rotation.HasValue ? SetPose(go, position, rotation.Value) : SetPosition(go, position);
    }

    private static int SetPosition(GameObject go, Vector3 position)
    {
        if ((go.transform.position - position).sqrMagnitude < 1e-6f) return 0;
        Undo.RecordObject(go.transform, "Playday layout");
        go.transform.position = position;
        PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
        return 1;
    }

    private static int SetPose(GameObject go, Vector3 position, Quaternion rotation)
    {
        bool same = (go.transform.position - position).sqrMagnitude < 1e-6f
                 && Quaternion.Angle(go.transform.rotation, rotation) < 0.01f;
        if (same) return 0;
        Undo.RecordObject(go.transform, "Playday layout");
        go.transform.SetPositionAndRotation(position, rotation);
        PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
        return 1;
    }

    private static GameObject NearestNamed(string name, Vector3 near, float maxDistance)
    {
        GameObject best = null;
        float bestSqr = maxDistance * maxDistance;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t.name != name) continue;
            float d = (t.position - near).sqrMagnitude;
            if (d <= bestSqr) { bestSqr = d; best = t.gameObject; }
        }
        return best;
    }

    private static Vector3 AxisOf(Vector3 size, bool largest)
    {
        if (largest)
            return size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
        return size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
    }
}
