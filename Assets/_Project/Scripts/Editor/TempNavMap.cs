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

    // The toggle only flips the feature in memory; without SaveAssets the .asset stays at 0.
    [MenuItem("Tools/Temp/PostFX Persist")]
    public static void PostFxPersist()
    {
        var t = AssetDatabase.LoadAssetAtPath<SO_PostProcessToggle>("Assets/_Project/ScriptableObjects/SO_PostProcessToggle.asset");
        if (t == null) { Debug.Log("TEMP-PFX toggle asset not found"); return; }

        t.SetAllEnabled(true);

        var targets = new System.Collections.Generic.List<Object>();
        t.CollectTargets(targets);
        foreach (Object o in targets) if (o != null) EditorUtility.SetDirty(o);
        EditorUtility.SetDirty(t);
        AssetDatabase.SaveAssets();

        Debug.Log($"TEMP-PFX saved. ps1={t.IsPs1Enabled} fog={t.IsVisionFogEnabled} features={t.AreRendererFeaturesEnabled} targets={targets.Count}");
    }

    // How wide is the gap the player actually has to squeeze through, prop by prop along x.
    [MenuItem("Tools/Temp/Escape Pinch Point")]
    public static void PinchPoint()
    {
        float r = 0.30f;
        Debug.Log("TEMP-PINCH player capsule radius 0.30 -> needs a 0.60 m gap minimum");
        for (float x = 5.0f; x <= 13.0f; x += 0.5f)
        {
            float best = 0f, bestZ = 0f, runStart = -1f;
            for (float z = 13.0f; z <= 17.05f; z += 0.05f)
            {
                bool free = !Physics.CheckCapsule(new Vector3(x, 0.4f, z), new Vector3(x, 1.7f, z), r, ~0, QueryTriggerInteraction.Ignore);
                if (free && runStart < 0f) runStart = z;
                if ((!free || z > 17.0f) && runStart >= 0f)
                {
                    float len = z - runStart;
                    if (len > best) { best = len; bestZ = runStart + len * 0.5f; }
                    runStart = -1f;
                }
            }
            string verdict = best <= 0.01f ? "BLOCKED" : (best < 0.35f ? "too tight" : "ok");
            Debug.Log($"TEMP-PINCH x={x:0.0} widest free lane={best:0.00} m centred at z={bestZ:0.00} -> {verdict}");
        }
    }

    // Hangs one realtime light under each corridor ceiling lamp and wires them into the flicker.
    // Re-runnable: it never adds a second light to a lamp that already has one.
    [MenuItem("Tools/Temp/Escape Build Corridor Lamps")]
    public static void BuildCorridorLamps()
    {
        const string ChildName = "EscapeLamp";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Light/Light Base.prefab");
        if (prefab == null) { Debug.Log("TEMP-LAMP Light Base.prefab not found"); return; }

        var made = new System.Collections.Generic.List<Light>();
        var props = new System.Collections.Generic.List<Transform>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!t.name.StartsWith("CeilingLamp")) continue;
            Vector3 p = t.position;
            if (p.x < -28f || p.x > 32f || p.z < 12f || p.z > 18f) continue;
            props.Add(t);
        }
        props.Sort((a, b) => a.position.x.CompareTo(b.position.x));

        foreach (Transform prop in props)
        {
            Transform existing = prop.Find(ChildName);
            Light light;
            if (existing != null)
            {
                light = existing.GetComponentInChildren<Light>();
                Debug.Log($"TEMP-LAMP reusing on {prop.name} at x={prop.position.x:0.00}");
            }
            else
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, prop);
                go.name = ChildName;
                go.transform.localPosition = Vector3.zero;
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // straight down
                light = go.GetComponentInChildren<Light>();
                // Off by default: the corridor stays dark in normal play and only these lamps
                // come alive during the escape. Begin() then falls back to config.LampIntensity,
                // and End() puts them back out.
                if (light != null) { light.enabled = false; light.intensity = 0f; }
                Undo.RegisterCreatedObjectUndo(go, "Escape corridor lamp");
                Debug.Log($"TEMP-LAMP created on {prop.name} at x={prop.position.x:0.00}");
            }
            if (light != null) made.Add(light);
        }

        var flicker = Object.FindAnyObjectByType<EscapeCorridorFlicker>();
        if (flicker == null) { Debug.Log($"TEMP-LAMP {made.Count} lamps ready, but no EscapeCorridorFlicker in the scene to wire them to"); return; }

        var so = new SerializedObject(flicker);
        var arr = so.FindProperty("lamps");
        arr.arraySize = made.Count;
        for (int i = 0; i < made.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = made[i];
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(flicker);

        Debug.Log($"TEMP-LAMP wired {made.Count} lamps into EscapeCorridorFlicker");
    }

    // Light Base.prefab brings a LightZone (pushes a fog preset on the player) and its trigger:
    // gameplay behaviour the escape lamps must not have. Strip it, keep Light + FogLightBypass,
    // and leave each lamp asleep (inactive) so it only exists while the escape runs.
    [MenuItem("Tools/Temp/Escape Strip Corridor Lamps")]
    public static void StripCorridorLamps()
    {
        int n = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.name != "EscapeLamp") continue;
            GameObject go = t.gameObject;

            foreach (var fade in go.GetComponents<FogLightBypassPlayerFade>()) Undo.DestroyObjectImmediate(fade);
            foreach (var zone in go.GetComponents<LightZone>()) Undo.DestroyObjectImmediate(zone);
            foreach (var col in go.GetComponents<SphereCollider>()) Undo.DestroyObjectImmediate(col);

            Undo.RecordObject(go, "Sleep escape lamp");
            go.SetActive(false);
            EditorUtility.SetDirty(go);
            n++;

            string left = string.Join(", ", System.Array.ConvertAll(go.GetComponents<Component>(), c => c.GetType().Name));
            Debug.Log($"TEMP-STRIP {go.transform.parent.name}: [{left}] active={go.activeSelf}");
        }
        Debug.Log($"TEMP-STRIP done, {n} lamps");
    }

    // One-shot audit of everything the escape depends on. Prints OK / FAIL lines.
    [MenuItem("Tools/Temp/Escape Audit")]
    public static void Audit()
    {
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        int fails = 0;
        void Check(bool ok, string what) { if (!ok) fails++; Debug.Log($"TEMP-AUDIT {(ok ? "OK  " : "FAIL")} {what}"); }

        var d = Object.FindAnyObjectByType<EscapeSequenceDirector>(FindObjectsInactive.Include);
        Check(d != null, "director in scene");
        if (d == null) return;

        foreach (string f in new[] { "config", "openingTimeline", "socketMacroCamera", "fogCycle", "corridorFlicker", "escapeAudio",
                                     "corridorLock", "actor", "pursuit", "captureGameOver", "playerRun", "cameraPan" })
        {
            var v = typeof(EscapeSequenceDirector).GetField(f, F)?.GetValue(d) as Object;
            Check(v != null, "director." + f);
        }

        object stage = typeof(EscapeSequenceDirector).GetField("stage", F).GetValue(d);
        foreach (var fi in stage.GetType().GetFields())
        {
            var v = fi.GetValue(stage) as Object;
            string extra = "";
            if (v is Transform t) extra = $" pos={t.position} yaw={t.eulerAngles.y:0}";
            Check(v != null, "stage." + fi.Name + extra);
        }

        // Flicker lamps
        var flicker = Object.FindAnyObjectByType<EscapeCorridorFlicker>(FindObjectsInactive.Include);
        var lamps = typeof(EscapeCorridorFlicker).GetField("lamps", F)?.GetValue(flicker) as Light[];
        Check(lamps != null && lamps.Length == 7, $"flicker lamps count = {(lamps == null ? -1 : lamps.Length)}");
        if (lamps != null)
            foreach (var l in lamps)
            {
                if (l == null) { Check(false, "lamp null"); continue; }
                bool clean = l.GetComponent<LightZone>() == null && l.GetComponent<SphereCollider>() == null && l.GetComponent<FogLightBypassPlayerFade>() == null;
                Check(!l.gameObject.activeSelf && clean && l.GetComponent<FogLightBypass>() != null,
                      $"lamp x={l.transform.position.x:0.0} inactive={!l.gameObject.activeSelf} clean={clean} bypass={l.GetComponent<FogLightBypass>() != null} fwdY={l.transform.forward.y:0.00}");
            }

        // Camera pan
        var pan = Object.FindAnyObjectByType<EscapeCameraPan>(FindObjectsInactive.Include);
        float sa = (float)typeof(EscapeCameraPan).GetField("startAngle", F).GetValue(pan);
        float sl = (float)typeof(EscapeCameraPan).GetField("startLowering", F).GetValue(pan);
        Check(Mathf.Approximately(sa, 55f) && Mathf.Approximately(sl, 5f), $"pan startAngle={sa} startLowering={sl}");

        // Timeline
        var pd = (UnityEngine.Playables.PlayableDirector)typeof(EscapeSequenceDirector).GetField("openingTimeline", F).GetValue(d);
        var tl = pd.playableAsset as UnityEngine.Timeline.TimelineAsset;
        Check(tl != null && Mathf.Approximately((float)tl.duration, 11f), $"timeline duration={tl?.duration}");
        var times = new System.Collections.Generic.Dictionary<string, double>();
        foreach (var m in tl.markerTrack.GetMarkers()) if (m is EscapeBeatMarker bm) times[bm.Beat.ToString()] = bm.time;
        foreach (var kv in times) Debug.Log($"TEMP-AUDIT      beat {kv.Key} @ {kv.Value:0.00}");
        Check(times["PlayerOpensSafeDoor"] < times["PlayerRunToSpot"], "door opens before the run");
        Check(times["PlacePlayer"] < times["PlayerOpensSafeDoor"], "player placed before the door opens");
        Check(times["PlayerCameraPan"] < tl.duration, "pan starts before the end");
        foreach (var track in tl.GetOutputTracks())
            foreach (var clip in track.GetClips())
                Debug.Log($"TEMP-AUDIT      clip '{clip.displayName}' {clip.start:0.00}->{clip.end:0.00}");

        // Config
        var cfg = typeof(EscapeSequenceDirector).GetField("config", F).GetValue(d) as SO_EscapeSequenceConfig;
        Check(cfg.PaceNear > 0.9f && cfg.PaceFar > 1.2f, $"pace near={cfg.PaceNear} far={cfg.PaceFar} nearD={cfg.PaceNearDistance} farD={cfg.PaceFarDistance} min={cfg.MinChaseSpeed}");

        // Player route
        Transform rs = (Transform)stage.GetType().GetField("playerRunStart").GetValue(stage);
        Transform sp = (Transform)stage.GetType().GetField("playerSpot").GetValue(stage);
        bool a = UnityEngine.AI.NavMesh.SamplePosition(rs.position, out var ha, 4f, UnityEngine.AI.NavMesh.AllAreas);
        bool b = UnityEngine.AI.NavMesh.SamplePosition(sp.position, out var hb, 3f, UnityEngine.AI.NavMesh.AllAreas);
        var path = new UnityEngine.AI.NavMeshPath();
        bool c = a && b && UnityEngine.AI.NavMesh.CalculatePath(ha.position, hb.position, UnityEngine.AI.NavMesh.AllAreas, path);
        float len = Vector3.Distance(rs.position, ha.position);
        for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        Check(c && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete, $"player route complete, {len:0.00} m = {len / 4.5f:0.00} s of sprint");
        bool clearAtSpot = !Physics.CheckCapsule(sp.position + Vector3.up * 0.4f, sp.position + Vector3.up * 1.7f, 0.3f, ~0, QueryTriggerInteraction.Ignore);
        Check(clearAtSpot, "spot is free of colliders");

        // Nemesis gap at handover
        Transform ne = (Transform)stage.GetType().GetField("nemesisApproachEnd").GetValue(stage);
        Debug.Log($"TEMP-AUDIT      handover gap player<->nemesis = {Vector3.Distance(sp.position, ne.position):0.00} m");

        // Renderer features
        var tog = AssetDatabase.LoadAssetAtPath<SO_PostProcessToggle>("Assets/_Project/ScriptableObjects/SO_PostProcessToggle.asset");
        Check(tog != null && tog.AreRendererFeaturesEnabled, "renderer features active");

        // Nemesis eyes on the prefab
        var nem = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Nemesis.prefab");
        var eyes = nem != null ? nem.GetComponentInChildren<NemesisEyes>(true) : null;
        if (eyes != null)
        {
            float mpr = (float)typeof(NemesisEyes).GetField("minPixelRadius", F).GetValue(eyes);
            float fa = (float)typeof(NemesisEyes).GetField("facingAngle", F).GetValue(eyes);
            float inten = (float)typeof(NemesisEyes).GetField("intensity", F).GetValue(eyes);
            Check(mpr >= 9f && fa >= 360f && inten >= 5f, $"eyes minPixelRadius={mpr} facingAngle={fa} intensity={inten}");
        }
        else Check(false, "NemesisEyes on prefab");

        Debug.Log($"TEMP-AUDIT DONE fails={fails}");
    }

    // Runtime snapshot: who is running, where everyone is, where the camera looks.
    [MenuItem("Tools/Temp/Escape Probe")]
    public static void Probe()
    {
        if (!Application.isPlaying) { Debug.Log("TEMP-PROBE not playing"); return; }
        var flicker = Object.FindAnyObjectByType<EscapeCorridorFlicker>();
        var pursuit = Object.FindAnyObjectByType<NemesisEscapePursuit>();
        var go = Object.FindAnyObjectByType<EscapeCaptureGameOver>();
        var player = PlayerRegistry.Current;
        var nem = Object.FindAnyObjectByType<NemesisStateManager>();
        int litLamps = 0;
        var lamps = typeof(EscapeCorridorFlicker).GetField("lamps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(flicker) as Light[];
        if (lamps != null) foreach (var l in lamps) if (l != null && l.gameObject.activeInHierarchy && l.enabled) litLamps++;
        Vector3 cf = Camera.main != null ? Camera.main.transform.forward : Vector3.zero;
        Vector3 pp = player != null ? player.transform.position : Vector3.zero;
        Vector3 np = nem != null ? nem.transform.position : Vector3.zero;
        Debug.Log($"TEMP-PROBE t={Time.time:0.0} flicker={flicker?.IsRunning} litLamps={litLamps}/7 pursuit={pursuit?.IsActive} gameOverArmed={go?.IsActive} " +
                  $"player={pp} bodyYaw={(player != null && player.PlayerBody != null ? player.PlayerBody.eulerAngles.y : -1):0} disabled={player?.IsDisabled} " +
                  $"camFwd=({cf.x:0.00},{cf.y:0.00},{cf.z:0.00}) nemesis={np} gap={Vector3.Distance(pp, np):0.00} nemState={nem?.CurrentStateKey} cpEnabled={(CheckpointManager.Exists ? CheckpointManager.Instance.enabled.ToString() : "n/a")}");
    }

    // Door (5), the Nemesis's door: its real opening, where the leaf ends up when the Nemesis opens
    // it, and whether the Nemesis's walk crosses the open leaf or the frame.
    [MenuItem("Tools/Temp/Escape Nemesis Door")]
    public static void NemesisDoor()
    {
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var d = Object.FindAnyObjectByType<EscapeSequenceDirector>(FindObjectsInactive.Include);
        object stage = typeof(EscapeSequenceDirector).GetField("stage", F).GetValue(d);
        T Get<T>(string n) where T : class => stage.GetType().GetField(n).GetValue(stage) as T;
        var door = Get<DoorInteractable>("nemesisSideDoor");
        Transform hidden = Get<Transform>("nemesisHidden"), doorway = Get<Transform>("nemesisDoorway"), exit = Get<Transform>("nemesisRunExit");

        var hinge = typeof(DoorInteractable).GetField("hinge", F).GetValue(door) as Transform;
        float angle = (float)typeof(DoorInteractable).GetField("openAngle", F).GetValue(door);
        Debug.Log($"TEMP-NDOOR hinge={hinge.position} openAngle={angle}");

        Bounds Leaf()
        {
            Bounds b = new Bounds(); bool first = true;
            foreach (var r in hinge.GetComponentsInChildren<Renderer>())
            { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            return b;
        }
        foreach (var r in door.GetComponentsInChildren<Renderer>())
            Debug.Log($"TEMP-NDOOR part '{r.name}' x=[{r.bounds.min.x:0.00},{r.bounds.max.x:0.00}] z=[{r.bounds.min.z:0.00},{r.bounds.max.z:0.00}] underHinge={r.transform.IsChildOf(hinge)}");

        Bounds closed = Leaf();
        Quaternion saved = hinge.localRotation;
        Bounds best = closed; float bestAway = float.MinValue; int bestSign = 0;
        foreach (int sign in new[] { 1, -1 })
        {
            hinge.localRotation = saved * Quaternion.Euler(0f, sign * angle, 0f);
            Bounds b = Leaf();
            float away = Vector3.Distance(new Vector3(b.center.x, 0, b.center.z), new Vector3(hidden.position.x, 0, hidden.position.z));
            Debug.Log($"TEMP-NDOOR open sign={sign} leaf x=[{b.min.x:0.00},{b.max.x:0.00}] z=[{b.min.z:0.00},{b.max.z:0.00}] distFromNemesis={away:0.00}");
            if (away > bestAway) { bestAway = away; best = b; bestSign = sign; }
        }
        hinge.localRotation = saved; // restore
        Debug.Log($"TEMP-NDOOR closed leaf x=[{closed.min.x:0.00},{closed.max.x:0.00}] z=[{closed.min.z:0.00},{closed.max.z:0.00}]  -> Nemesis swing = sign {bestSign}, open leaf x=[{best.min.x:0.00},{best.max.x:0.00}] z=[{best.min.z:0.00},{best.max.z:0.00}]");

        // Walk segments vs the open leaf (Nemesis agent radius 0.5).
        void Seg(string name, Vector3 a, Vector3 b)
        {
            Bounds grown = best; grown.Expand(new Vector3(1.0f, 0f, 1.0f)); // radius 0.5 each side
            int hits = 0; float firstT = -1f;
            for (int i = 0; i <= 40; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / 40f); p.y = best.center.y;
                if (grown.Contains(p)) { hits++; if (firstT < 0) firstT = i / 40f; }
            }
            Debug.Log($"TEMP-NDOOR walk {name} {a}->{b}: {(hits > 0 ? $"CROSSES the open leaf ({hits}/41 samples, from t={firstT:0.00})" : "clear of the open leaf")}");
        }
        Seg("hidden->doorway", hidden.position, doorway.position);
        Seg("doorway->exit", doorway.position, exit.position);

        // The actor drives a NavMeshAgent: what matters is the NavMesh path, corner by corner.
        void NavSeg(string name, Vector3 a, Vector3 b)
        {
            bool sa = UnityEngine.AI.NavMesh.SamplePosition(a, out var ha, 1.5f, UnityEngine.AI.NavMesh.AllAreas);
            bool sb = UnityEngine.AI.NavMesh.SamplePosition(b, out var hb, 1.5f, UnityEngine.AI.NavMesh.AllAreas);
            if (!sa || !sb) { Debug.Log($"TEMP-NDOOR nav {name}: OFF NAVMESH (from={sa} to={sb})"); return; }
            var path = new UnityEngine.AI.NavMeshPath();
            UnityEngine.AI.NavMesh.CalculatePath(ha.position, hb.position, UnityEngine.AI.NavMesh.AllAreas, path);
            Debug.Log($"TEMP-NDOOR nav {name}: status={path.status} corners={path.corners.Length} snapFrom={ha.position} snapTo={hb.position}");
            for (int i = 1; i < path.corners.Length; i++)
                Seg($"  nav {name} leg{i}", path.corners[i - 1], path.corners[i]);
        }
        NavSeg("hidden->doorway", hidden.position, doorway.position);
        NavSeg("doorway->exit", doorway.position, exit.position);
    }

    // Shot 2A setup: fog off for exactly the frames of the shot, plus a steady key light on the
    // Nemesis's doorway. One inactive object, switched on by an Activation Track 2.0 -> 8.35.
    [MenuItem("Tools/Temp/Escape Build Shot2A")]
    public static void BuildShot2A()
    {
        const string PresetPath = "Assets/_Project/ScriptableObjects/Escape/SO_VisionFog_CinematicClear.asset";
        const string ObjName = "Shot2A_Setup (fog off + Nemesis key light)";

        // 1. The preset: a copy of Dark with the fog collapsed (start == end -> the shader skips it).
        //    Same numbers as Dark everywhere else, so popping back reads as a cut, not an iris.
        var preset = AssetDatabase.LoadAssetAtPath<SO_VisionFogConfig>(PresetPath);
        if (preset == null)
        {
            AssetDatabase.CopyAsset("Assets/_Project/ScriptableObjects/Rendering/Fog/SO_VisionFog_Dark.asset", PresetPath);
            preset = AssetDatabase.LoadAssetAtPath<SO_VisionFogConfig>(PresetPath);
        }
        preset.visionStart = 6.8f;
        preset.visionEnd = 6.8f;
        preset.transitionDuration = 0f;
        EditorUtility.SetDirty(preset);

        // 2. The object, under the escape sequence.
        var dir = Object.FindAnyObjectByType<EscapeSequenceDirector>(FindObjectsInactive.Include);
        Transform root = dir.transform;
        Transform existing = root.Find(ObjName);
        GameObject go = existing != null ? existing.gameObject : new GameObject(ObjName);
        if (existing == null) { go.transform.SetParent(root, false); Undo.RegisterCreatedObjectUndo(go, "Shot2A setup"); }

        // Not ??: a missing component is a Unity fake-null in the Editor, which ?? does not see.
        var ov = go.GetComponent<VisionFogOverride>();
        if (ov == null) ov = go.AddComponent<VisionFogOverride>();
        var so = new SerializedObject(ov);
        so.FindProperty("config").objectReferenceValue = preset;
        so.ApplyModifiedProperties();

        // Key light: between the 2A camera and the Nemesis's doorway, high, aimed at where it steps
        // out and looks around. Steady on purpose — the corridor lamps flicker, the reveal must not.
        Transform keyT = go.transform.Find("NemesisKeyLight");
        if (keyT == null) { keyT = new GameObject("NemesisKeyLight").transform; keyT.SetParent(go.transform, false); }
        keyT.position = new Vector3(-17.0f, 3.3f, 14.2f);
        keyT.LookAt(new Vector3(-21.1f, 1.2f, 14.8f));
        var key = keyT.GetComponent<Light>();
        if (key == null) key = keyT.gameObject.AddComponent<Light>();
        key.type = LightType.Spot;
        key.spotAngle = 55f;
        key.innerSpotAngle = 35f;
        key.range = 9f;
        key.intensity = 6f;
        key.color = new Color(0.82f, 0.88f, 1f); // a cold key, to set it apart from the warm corridor lamps
        key.shadows = LightShadows.None;
        key.lightmapBakeType = LightmapBakeType.Realtime;

        go.SetActive(false); // the track owns when it is on
        EditorUtility.SetDirty(go);

        // 3. Activation Track 2.0 -> 8.35 (the 2A clip), bound to the object.
        var pd = (UnityEngine.Playables.PlayableDirector)typeof(EscapeSequenceDirector)
            .GetField("openingTimeline", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(dir);
        var tl = (UnityEngine.Timeline.TimelineAsset)pd.playableAsset;
        UnityEngine.Timeline.ActivationTrack track = null;
        foreach (var t in tl.GetOutputTracks())
            if (t is UnityEngine.Timeline.ActivationTrack at && at.name == "Shot2A Setup") track = at;
        if (track == null) track = tl.CreateTrack<UnityEngine.Timeline.ActivationTrack>(null, "Shot2A Setup");
        track.postPlaybackState = UnityEngine.Timeline.ActivationTrack.PostPlaybackState.Inactive;

        UnityEngine.Timeline.TimelineClip clip = null;
        foreach (var c in track.GetClips()) clip = c;
        if (clip == null) clip = track.CreateDefaultClip();
        clip.start = 2.0;
        clip.duration = 6.35;
        clip.displayName = "2A · niebla off + luz Nemesis";

        pd.SetGenericBinding(track, go);
        EditorUtility.SetDirty(tl);
        EditorUtility.SetDirty(pd);
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);

        Debug.Log($"TEMP-2A preset start={preset.visionStart} end={preset.visionEnd} transition={preset.transitionDuration} | " +
                  $"object active={go.activeSelf} override={ov != null} key at {keyT.position} fwd={keyT.forward} | " +
                  $"track clip {clip.start:0.00}->{clip.end:0.00} bound={(pd.GetGenericBinding(track) as GameObject)?.name}");
    }

    // Fog off for the whole final chase: both fog-cycle presets point at a preset that pushes the
    // fog out past the corridor. The originals (EscapeClosed / EscapeOpen) are left untouched.
    [MenuItem("Tools/Temp/Escape No Fog Chase")]
    public static void NoFogChase()
    {
        const string NoFogPath = "Assets/_Project/ScriptableObjects/Escape/SO_VisionFog_EscapeNoFog.asset";
        var noFog = AssetDatabase.LoadAssetAtPath<SO_VisionFogConfig>(NoFogPath);
        if (noFog == null)
        {
            AssetDatabase.CopyAsset("Assets/_Project/ScriptableObjects/Rendering/Fog/SO_VisionFog_Dark.asset", NoFogPath);
            noFog = AssetDatabase.LoadAssetAtPath<SO_VisionFogConfig>(NoFogPath);
        }
        // Far, not collapsed: the fog cycle lerps into it over its close seconds, so the wall of fog
        // recedes past the corridor (~56 m) instead of snapping away at the end of the lerp.
        noFog.visionStart = 60f;
        noFog.visionEnd = 120f;
        EditorUtility.SetDirty(noFog);

        var dir = Object.FindAnyObjectByType<EscapeSequenceDirector>(FindObjectsInactive.Include);
        var cfg = typeof(EscapeSequenceDirector).GetField("config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(dir) as SO_EscapeSequenceConfig;
        var so = new SerializedObject(cfg);
        var closed = so.FindProperty("closedFog");
        var open = so.FindProperty("openFog");
        string before = $"{(closed.objectReferenceValue ? closed.objectReferenceValue.name : "null")} / {(open.objectReferenceValue ? open.objectReferenceValue.name : "null")}";
        closed.objectReferenceValue = noFog;
        open.objectReferenceValue = noFog;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(cfg);
        AssetDatabase.SaveAssets();

        Debug.Log($"TEMP-NOFOG preset start={noFog.visionStart} end={noFog.visionEnd} | config closed/open was [{before}] now [{cfg.ClosedFog.name} / {cfg.OpenFog.name}]");
    }
}
