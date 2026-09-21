using UnityEditor;
using UnityEngine;

// TEMP diagnostic — delete after use.
public static class TempNavMap
{
    // Completes the trigger puzzle for real, so the whole chain runs exactly as it does in game:
    // the gate opens, the director's own subscription fires, the escape starts.
    [MenuItem("Tools/Temp/Escape Test Start")]
    public static void Start()
    {
        if (!Application.isPlaying) { Debug.Log("TEMP-ESCAPE not in play mode"); return; }

        EscapeSequenceDirector director = Object.FindAnyObjectByType<EscapeSequenceDirector>();
        if (director == null) { Debug.Log("TEMP-ESCAPE no director"); return; }

        var cf = typeof(EscapeSequenceDirector).GetField("config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var config = cf.GetValue(director) as SO_EscapeSequenceConfig;
        string id = config != null ? config.TriggerPuzzleId : null;

        if (!PuzzleStateManager.Exists || string.IsNullOrEmpty(id))
        { Debug.Log($"TEMP-ESCAPE cannot complete puzzle (manager={PuzzleStateManager.Exists} id='{id}')"); return; }

        bool already = PuzzleStateManager.Instance.IsPuzzleCompleted(id);
        Debug.Log($"TEMP-ESCAPE completing puzzle '{id}' (alreadyCompleted={already})");
        PuzzleStateManager.Instance.SetPuzzleCompleted(id);
    }

    [MenuItem("Tools/Temp/Escape Test Skip")]
    public static void Skip()
    {
        EscapeSequenceDirector director = Object.FindAnyObjectByType<EscapeSequenceDirector>();
        var m = typeof(EscapeSequenceDirector).GetMethod("Skip", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Debug.Log("TEMP-ESCAPE skip requested");
        m.Invoke(director, null);
    }

    // Lists every collider along the player's cinematic run corridor (z ~ 14.9, x from 18 to -1).
    [MenuItem("Tools/Temp/Escape Corridor Colliders")]
    public static void CorridorColliders()
    {
        Vector3 center = new Vector3(8.5f, 1.0f, 14.9f);
        Vector3 half = new Vector3(10.0f, 1.2f, 1.6f);
        Collider[] hits = Physics.OverlapBox(center, half, Quaternion.identity, ~0, QueryTriggerInteraction.Collide);
        Debug.Log($"TEMP-COL count={hits.Length} box center={center} half={half}");
        System.Array.Sort(hits, (a, b) => b.bounds.center.x.CompareTo(a.bounds.center.x));
        foreach (Collider c in hits)
        {
            Bounds b = c.bounds;
            Debug.Log($"TEMP-COL x={b.center.x:0.00} z={b.center.z:0.00} xRange=[{b.min.x:0.00},{b.max.x:0.00}] " +
                      $"zRange=[{b.min.z:0.00},{b.max.z:0.00}] trigger={c.isTrigger} layer={LayerMask.LayerToName(c.gameObject.layer)} " +
                      $"name={c.name} path={Path(c.transform)}");
        }
    }

    private static string Path(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    // Sweeps the player capsule down the corridor at several z lanes and reports the first blocker.
    [MenuItem("Tools/Temp/Escape Corridor Lanes")]
    public static void CorridorLanes()
    {
        float radius = 0.35f, height = 1.8f;
        PlayerStateManager player = Object.FindAnyObjectByType<PlayerStateManager>();
        CapsuleCollider cap = player != null ? player.GetComponentInChildren<CapsuleCollider>() : null;
        if (cap != null) { radius = cap.radius * Mathf.Max(cap.transform.lossyScale.x, cap.transform.lossyScale.z); height = cap.height * cap.transform.lossyScale.y; }
        Debug.Log($"TEMP-LANE player capsule radius={radius:0.000} height={height:0.000} found={cap != null}");

        int mask = ~0;
        for (float z = 13.4f; z <= 16.7f; z += 0.2f)
        {
            Vector3 start = new Vector3(17.60f, 0f, z);
            Vector3 p1 = start + Vector3.up * radius;
            Vector3 p2 = start + Vector3.up * Mathf.Max(height - radius, radius);
            float dist = 17.60f - (-0.06f);
            bool hit = Physics.CapsuleCast(p1, p2, radius, Vector3.left, out RaycastHit info, dist, mask, QueryTriggerInteraction.Ignore);
            if (hit) Debug.Log($"TEMP-LANE z={z:0.00} BLOCKED at x={17.60f - info.distance:0.00} by {info.collider.name} (layer {LayerMask.LayerToName(info.collider.gameObject.layer)})");
            else     Debug.Log($"TEMP-LANE z={z:0.00} CLEAR all the way");
        }
    }

    // For each z lane, how far east of the spot the player can stand and still have a clear straight run in.
    [MenuItem("Tools/Temp/Escape Clear Runway")]
    public static void ClearRunway()
    {
        float radius = 0.30f, height = 1.86f;
        Vector3 spot = new Vector3(-0.06f, 0f, 14.90f);
        Debug.Log("TEMP-RUN spot=" + spot);
        for (float z = 13.4f; z <= 16.7f; z += 0.2f)
        {
            Vector3 origin = new Vector3(spot.x, 0f, z);
            Vector3 p1 = origin + Vector3.up * radius;
            Vector3 p2 = origin + Vector3.up * Mathf.Max(height - radius, radius);
            bool hit = Physics.CapsuleCast(p1, p2, radius, Vector3.right, out RaycastHit info, 30f, ~0, QueryTriggerInteraction.Ignore);
            float len = hit ? info.distance : 30f;
            string by = hit ? info.collider.name : "nothing";
            Debug.Log($"TEMP-RUN z={z:0.00} runway={len:0.00} m (start x={spot.x + len:0.00}) first blocker={by}");
        }
    }

    // Does a NavMesh path exist from the run start marker to the spot, and how long is it?
    [MenuItem("Tools/Temp/Escape Run Path")]
    public static void RunPath()
    {
        EscapeSequenceDirector dir = Object.FindAnyObjectByType<EscapeSequenceDirector>();
        var sf = typeof(EscapeSequenceDirector).GetField("stage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        object st = sf.GetValue(dir);
        Transform ms = (Transform)st.GetType().GetField("playerRunStart").GetValue(st);
        Transform me = (Transform)st.GetType().GetField("playerSpot").GetValue(st);
        Vector3 from = ms.position;
        Vector3 to = me.position;
        bool okFrom = UnityEngine.AI.NavMesh.SamplePosition(from, out var hf, 3f, UnityEngine.AI.NavMesh.AllAreas);
        bool okTo = UnityEngine.AI.NavMesh.SamplePosition(to, out var ht, 3f, UnityEngine.AI.NavMesh.AllAreas);
        Debug.Log($"TEMP-PATH sampleFrom={okFrom} at {(okFrom ? hf.position : Vector3.zero)} | sampleTo={okTo} at {(okTo ? ht.position : Vector3.zero)}");
        if (!okFrom || !okTo) return;

        var path = new UnityEngine.AI.NavMeshPath();
        bool got = UnityEngine.AI.NavMesh.CalculatePath(hf.position, ht.position, UnityEngine.AI.NavMesh.AllAreas, path);
        Debug.Log($"TEMP-PATH calculated={got} status={path.status} corners={path.corners.Length}");
        float len = 0f;
        for (int i = 0; i < path.corners.Length; i++)
        {
            if (i > 0) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            Debug.Log($"TEMP-PATH corner[{i}] = {path.corners[i]}");
        }
        Debug.Log($"TEMP-PATH total length={len:0.00} m -> at 4.5 m/s that is {len / 4.5f:0.00} s of sprint");
    }

    // Dumps every stage marker + the two doors, with world position and facing.
    [MenuItem("Tools/Temp/Escape Stage Dump")]
    public static void StageDump()
    {
        EscapeSequenceDirector d = Object.FindAnyObjectByType<EscapeSequenceDirector>();
        if (d == null) { Debug.Log("TEMP-STAGE no director"); return; }

        var f = typeof(EscapeSequenceDirector).GetField("stage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        object stage = f.GetValue(d);
        foreach (var fi in stage.GetType().GetFields())
        {
            object v = fi.GetValue(stage);
            Transform t = v as Transform;
            if (t == null && v is Component c) t = c.transform;
            if (t == null) { Debug.Log($"TEMP-STAGE {fi.Name} = <null>"); continue; }
            Debug.Log($"TEMP-STAGE {fi.Name}: pos={t.position} fwd={t.forward} yaw={t.eulerAngles.y:0.0} obj={t.name}");
        }

        foreach (var door in Object.FindObjectsByType<DoorInteractable>(FindObjectsSortMode.None))
        {
            Bounds b = new Bounds(door.transform.position, Vector3.zero);
            foreach (var col in door.GetComponentsInChildren<Collider>()) b.Encapsulate(col.bounds);
            Debug.Log($"TEMP-DOOR {door.name} pivot={door.transform.position} boundsCenter={b.center} x=[{b.min.x:0.00},{b.max.x:0.00}] z=[{b.min.z:0.00},{b.max.z:0.00}]");
        }
    }

    // Finds the safe door's real opening and measures NavMesh run lengths eastward from it.
    [MenuItem("Tools/Temp/Escape Door Runway")]
    public static void DoorRunway()
    {
        // The safe door sits in the south wall around x = -0.6, z = 12.5..13.1.
        // Probe a grid in front of it to find where a character can actually stand.
        for (float x = -2.5f; x <= 1.5f; x += 0.25f)
        {
            for (float z = 12.2f; z <= 14.2f; z += 0.4f)
            {
                bool on = UnityEngine.AI.NavMesh.SamplePosition(new Vector3(x, 0.1f, z), out var h, 0.35f, UnityEngine.AI.NavMesh.AllAreas);
                if (on) Debug.Log($"TEMP-DOORWAY navmesh at x={x:0.00} z={z:0.00} -> {h.position}");
            }
        }

        // From the doorway, how long is the NavMesh run to candidate stop spots eastward?
        Vector3 from = new Vector3(-0.65f, 0.05f, 13.60f);
        if (!UnityEngine.AI.NavMesh.SamplePosition(from, out var start, 2f, UnityEngine.AI.NavMesh.AllAreas))
        { Debug.Log("TEMP-DOORWAY start not on navmesh"); return; }
        Debug.Log($"TEMP-DOORWAY start snapped to {start.position}");

        var path = new UnityEngine.AI.NavMeshPath();
        foreach (float x in new[] { 4f, 6f, 8f, 10f, 12f, 14f, 16f, 17.6f })
        {
            if (!UnityEngine.AI.NavMesh.SamplePosition(new Vector3(x, 0.05f, 14.9f), out var end, 2f, UnityEngine.AI.NavMesh.AllAreas)) { Debug.Log($"TEMP-DOORWAY x={x} off navmesh"); continue; }
            if (!UnityEngine.AI.NavMesh.CalculatePath(start.position, end.position, UnityEngine.AI.NavMesh.AllAreas, path)) { Debug.Log($"TEMP-DOORWAY x={x} no path"); continue; }
            float len = 0f;
            for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            Debug.Log($"TEMP-DOORWAY to x={x:0.0} ({end.position}) status={path.status} corners={path.corners.Length} len={len:0.00} m = {len / 4.5f:0.00} s sprint");
        }
    }

    [MenuItem("Tools/Temp/PostFX On")]
    public static void PostFxOn()
    {
        var t = AssetDatabase.LoadAssetAtPath<SO_PostProcessToggle>("Assets/_Project/ScriptableObjects/SO_PostProcessToggle.asset");
        if (t == null) { Debug.Log("TEMP-PFX toggle asset not found"); return; }
        t.SetAllEnabled(true);
        Debug.Log($"TEMP-PFX ps1={t.IsPs1Enabled} fog={t.IsVisionFogEnabled} features={t.AreRendererFeaturesEnabled} any={t.IsAnyEnabled}");
    }

    // Probes the safe room behind the door (south of z=13) for floor to stand on.
    [MenuItem("Tools/Temp/Escape Safe Room")]
    public static void SafeRoom()
    {
        for (float z = 13.2f; z >= 9.0f; z -= 0.4f)
        {
            bool floor = Physics.Raycast(new Vector3(-0.65f, 3f, z), Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore);
            bool nav = UnityEngine.AI.NavMesh.SamplePosition(new Vector3(-0.65f, 0.1f, z), out var h, 0.3f, UnityEngine.AI.NavMesh.AllAreas);
            bool blocked = Physics.CheckCapsule(new Vector3(-0.65f, 0.4f, z), new Vector3(-0.65f, 1.6f, z), 0.3f, ~0, QueryTriggerInteraction.Ignore);
            Debug.Log($"TEMP-ROOM z={z:0.0} floor={(floor ? hit.point.y.ToString("0.00") + " (" + hit.collider.name + ")" : "none")} navmesh={nav} blocked={blocked}");
        }
    }

    // Lists every Light along the escape corridor, so we know what is already there to flicker.
    [MenuItem("Tools/Temp/Escape Corridor Lights")]
    public static void CorridorLights()
    {
        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int n = 0;
        System.Array.Sort(lights, (a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
        foreach (Light l in lights)
        {
            Vector3 p = l.transform.position;
            if (p.x < -28f || p.x > 32f || p.z < 11f || p.z > 19f) continue;
            n++;
            Debug.Log($"TEMP-LIGHT x={p.x:0.0} z={p.z:0.0} y={p.y:0.0} type={l.type} mode={l.lightmapBakeType} " +
                      $"range={l.range:0.0} intensity={l.intensity:0.00} on={l.enabled && l.gameObject.activeInHierarchy} " +
                      $"name={l.name} path={Path(l.transform)}");
        }
        Debug.Log($"TEMP-LIGHT total in corridor = {n} (of {lights.Length} in scene)");
    }

    // Finds the REAL openings in the south wall of the corridor, and where each door leaf is.
    [MenuItem("Tools/Temp/Escape Find Doorway")]
    public static void FindDoorway()
    {
        // An opening is a spot where a person-sized capsule fits through the wall band z = 12.3..13.5.
        Debug.Log("TEMP-VANO scanning south wall, z band 12.3..13.5");
        bool wasOpen = false;
        for (float x = -8f; x <= 4f; x += 0.15f)
        {
            bool clear = true;
            for (float z = 12.3f; z <= 13.5f; z += 0.3f)
                if (Physics.CheckCapsule(new Vector3(x, 0.4f, z), new Vector3(x, 1.7f, z), 0.28f, ~0, QueryTriggerInteraction.Ignore))
                { clear = false; break; }

            if (clear != wasOpen)
            {
                Debug.Log($"TEMP-VANO x={x:0.00} -> {(clear ? "OPENING starts" : "opening ends")}");
                wasOpen = clear;
            }
        }

        foreach (var door in Object.FindObjectsByType<DoorInteractable>(FindObjectsInactive.Include))
        {
            Vector3 dp = door.transform.position;
            if (dp.x < -8f || dp.x > 4f || dp.z < 10f || dp.z > 19f) continue;
            Debug.Log($"TEMP-VANO door {door.name} root={dp}");
            foreach (Transform c in door.GetComponentsInChildren<Transform>())
            {
                var r = c.GetComponent<Renderer>();
                if (r == null) continue;
                Bounds b = r.bounds;
                Debug.Log($"TEMP-VANO    leaf '{c.name}' center={b.center} size={b.size} x=[{b.min.x:0.00},{b.max.x:0.00}] z=[{b.min.z:0.00},{b.max.z:0.00}]");
            }
        }
    }
}
