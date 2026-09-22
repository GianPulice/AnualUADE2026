using System.Collections.Generic;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Builds the scene side of the escape's chase cinematics on the open scene and wires it into the
/// <see cref="EscapeSequenceDirector"/>: the reveal trigger past the centre door, the reveal's turn,
/// the Nemesis's marks for the reveal and the ending, the restart checkpoint on Player_Spot, the
/// gate's shot camera and its dust, and the hub's other doors into the lock-down. Then clears out
/// what only the old opening used — the 2A shot, the run and look markers, the over-the-shoulder
/// reveal camera, and the components whose scripts were deleted (the automatic run, the camera pan).
///
/// Re-runnable: whatever the director already has wired is left alone, so an object moved by hand
/// is never put back. Only what is missing is created, placed from the scene's own geometry (the
/// centre door, the gate, Player_Spot). Everything goes through Undo, and the scene is only marked
/// dirty: saving is yours.
/// </summary>
public static class EscapeChaseSetup
{
    private const string SmokeMaterialPath = "Assets/_Project/Art/Materials/VFX/mat_vfx_smoke.mat";
    private const string DebrisMaterialPath = "Assets/_Project/Art/Materials/VFX/mat_vfx_debris.mat";

    // Placement, in metres. Starting points only: tune the objects in the scene afterwards.
    private const float RevealStartDistance = 12f;  // from the centre door, away from the gate
    private const float TriggerDoorGap = 0.6f;      // how far out of the door the trigger begins
    private const float TriggerLength = 3f;         // along the corridor

    // The last shot looks at the gate from behind the Nemesis: it starts just in front of the
    // camera and runs away from it, so the gate comes down in its face, in frame.
    private const float GateStopDistance = 1.2f;    // in front of the gate, corridor side
    private const float GateStartDistance = 6f;
    private const float GateCameraBack = 8f;
    private const float GateCameraSide = 1.2f;
    private const float GateCameraHeight = 1.6f;

    // The hub's doors: this far from its sockets, on its floor.
    private const float HubDoorRadius = 12f;
    private const float HubSameFloor = 2.5f;

    // The marks and objects of the old opening, by the name prefix they were built with.
    private static readonly string[] RetiredStageMarkers =
    {
        "Player_RunStart", "Nemesis_Doorway", "Nemesis_LookLeft", "Nemesis_LookRight",
        "Nemesis_RunExit", "Nemesis_ApproachEnd",
    };

    [MenuItem("Tools/Escape Sequence/Setup Chase Cinematics")]
    public static void Run()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("[EscapeChaseSetup] Leave Play mode first.");
            return;
        }

        EscapeSequenceDirector director = Object.FindAnyObjectByType<EscapeSequenceDirector>(FindObjectsInactive.Include);
        if (director == null)
        {
            Debug.LogError("[EscapeChaseSetup] No EscapeSequenceDirector in the open scene.");
            return;
        }

        SerializedObject so = new SerializedObject(director);
        SerializedProperty stage = so.FindProperty("stage");

        Transform playerSpot = Ref<Transform>(stage, "playerSpot");
        DoorInteractable safeDoor = Ref<DoorInteractable>(stage, "safeDoor");
        PuzzleGate gate = Ref<PuzzleGate>(stage, "endGate");
        if (playerSpot == null || safeDoor == null || gate == null)
        {
            Debug.LogError("[EscapeChaseSetup] The director's Stage needs Player Spot, Safe Door and " +
                           "End Gate wired first: everything else is placed from them.", director);
            return;
        }

        Undo.SetCurrentGroupName("Escape chase cinematics setup");
        int undoGroup = Undo.GetCurrentGroup();
        StringBuilder log = new StringBuilder("[EscapeChaseSetup] done.\n");

        Transform root = director.transform;
        Transform stageRoot = ChildOrCreate(root, "Stage", log);
        Transform camerasRoot = ChildOrCreate(root, "Cameras", log);

        // ── Geometry ────────────────────────────────────────────────────────
        float floorY = playerSpot.position.y;
        Vector3 door = Flat(BoundsOf(safeDoor.gameObject).center, floorY);
        Vector3 gateCentre = Flat(BoundsOf(gate.gameObject).center, floorY);

        // The corridor's axis, towards the gate: Player_Spot and the gate both stand on its centre
        // line (the door does not: it is in the side wall).
        Vector3 along = Flat(gateCentre - playerSpot.position, 0f).normalized;
        if (along.sqrMagnitude < 0.01f) along = Flat(gateCentre - door, 0f).normalized;
        Vector3 approach = along;

        // Out of the door: from the door to the corridor's centre line, across the corridor.
        Vector3 toCentre = Flat(playerSpot.position - door, 0f);
        toCentre -= Vector3.Project(toCentre, along);
        float halfWidth = toCentre.magnitude;
        Vector3 outOfDoor = halfWidth > 0.3f ? toCentre / halfWidth : Vector3.Cross(Vector3.up, along);
        if (halfWidth <= 0.3f) halfWidth = 2f;
        Vector3 corridorCentre = door + outOfDoor * halfWidth;

        // ── Reveal trigger ──────────────────────────────────────────────────
        if (Ref<EscapeRevealTrigger>(stage, "revealTrigger") == null)
        {
            float depth = Mathf.Max(1f, 2f * halfWidth - TriggerDoorGap);
            Vector3 centre = door + outOfDoor * (TriggerDoorGap + depth * 0.5f) + Vector3.up * 1f;

            GameObject go = NewObject(stageRoot, "RevealTrigger (the Nemesis appears when the player steps in)",
                                      centre, Quaternion.LookRotation(along));
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(depth, 2.6f, TriggerLength);
            EscapeRevealTrigger trigger = go.AddComponent<EscapeRevealTrigger>();

            SetRef(stage, "revealTrigger", trigger);
            log.AppendLine($"  + RevealTrigger at {centre:F1}, {depth:0.0} m out of the door x {TriggerLength} m along the corridor.");
        }

        // ── Nemesis mark of the reveal ──────────────────────────────────────
        EnsureMarker(stage, "nemesisRevealStart", stageRoot,
                     "Nemesis_RevealStart (appears in the fog, facing the player)",
                     corridorCentre - along * RevealStartDistance, along, log);

        // ── The last shot: camera first, then where the Nemesis runs from ───
        EscapeShotCamera gateCamera = so.FindProperty("gateShot").objectReferenceValue as EscapeShotCamera;
        if (gateCamera == null)
        {
            Vector3 look = gateCentre - approach * 0.5f + Vector3.up * 1.4f;
            Vector3 position = GateCameraPosition(gateCentre, approach, look);

            gateCamera = NewShotCamera(camerasRoot, "Cam_4_Gate (last shot: behind the Nemesis, at the gate)",
                                       position, Quaternion.LookRotation(look - position), 55f, 0.25f);
            so.FindProperty("gateShot").objectReferenceValue = gateCamera;
            log.AppendLine($"  + Cam_4_Gate at {position:F1}. Check it in the Scene view: the gate and " +
                           "Nemesis_GateStart in frame.");
        }

        // The Nemesis starts a couple of metres in front of the camera, so it is in frame from the
        // cut and runs away from it, at the gate.
        float cameraBack = Vector3.Dot(Flat(gateCentre - gateCamera.transform.position, 0f), approach);
        float startDistance = Mathf.Clamp(cameraBack - 2f, GateStopDistance + 2.5f, GateStartDistance);

        EnsureMarker(stage, "nemesisGateStart", stageRoot,
                     "Nemesis_GateStart (runs at the gate from here, last shot)",
                     gateCentre - approach * startDistance, approach, log);
        EnsureMarker(stage, "nemesisGateStop", stageRoot,
                     "Nemesis_GateStop (stuck at the gate, corridor side)",
                     gateCentre - approach * GateStopDistance, approach, log);

        // ── Restart checkpoint ──────────────────────────────────────────────
        if (Ref<Checkpoint>(stage, "chaseCheckpoint") == null)
        {
            Checkpoint checkpoint = playerSpot.GetComponent<Checkpoint>();
            if (checkpoint == null)
            {
                checkpoint = Undo.AddComponent<Checkpoint>(playerSpot.gameObject);

                // Never activates by itself: no trigger collider, and no puzzle to wait on. The
                // director activates it when the chase starts.
                SerializedObject cso = new SerializedObject(checkpoint);
                cso.FindProperty("activationMode").enumValueIndex = (int)Checkpoint.EActivationMode.PuzzleCompleted;
                cso.FindProperty("puzzleIds").arraySize = 0;
                cso.ApplyModifiedProperties();
            }

            SetRef(stage, "chaseCheckpoint", checkpoint);
            log.AppendLine($"  + Checkpoint on '{playerSpot.name}' (the chase restarts there).");
        }

        // ── The reveal's turn (the player's own camera: no shot of its own) ─
        if (so.FindProperty("playerTurn").objectReferenceValue == null)
        {
            EscapePlayerTurn turn = root.GetComponent<EscapePlayerTurn>();
            if (turn == null) turn = Undo.AddComponent<EscapePlayerTurn>(root.gameObject);

            so.FindProperty("playerTurn").objectReferenceValue = turn;
            log.AppendLine("  + EscapePlayerTurn on the director (the slow turn to the Nemesis).");
        }

        // ── Every other way out of the hub locks too ────────────────────────
        AddHubDoorsToLockDown(director, so, safeDoor, log);

        // ── Gate dust ───────────────────────────────────────────────────────
        if (so.FindProperty("gateSlam").objectReferenceValue == null)
        {
            Material smoke = AssetDatabase.LoadAssetAtPath<Material>(SmokeMaterialPath);
            Material debris = AssetDatabase.LoadAssetAtPath<Material>(DebrisMaterialPath);
            if (smoke == null || debris == null)
                Debug.LogWarning("[EscapeChaseSetup] The VFX materials are missing (" + SmokeMaterialPath +
                                 ", " + DebrisMaterialPath + "): run Tools ▸ VFX ▸ Module Explosion ▸ " +
                                 "Setup to build them, then assign them on GateDust.");

            Vector3 across = Vector3.Cross(Vector3.up, approach);
            Vector3 size = BoundsOf(gate.gameObject).size;
            float width = Mathf.Clamp(Mathf.Abs(across.x) * size.x + Mathf.Abs(across.z) * size.z, 1f, 8f);

            GameObject dust = NewObject(root, "GateDust (the gate's slam)", gateCentre + Vector3.up * 0.05f,
                                        Quaternion.LookRotation(approach));
            BuildDust(dust.transform, width, smoke, debris);

            EscapeGateSlam slam = dust.AddComponent<EscapeGateSlam>();
            SerializedObject sso = new SerializedObject(slam);
            sso.FindProperty("gate").objectReferenceValue = gate;
            sso.ApplyModifiedPropertiesWithoutUndo();

            so.FindProperty("gateSlam").objectReferenceValue = slam;
            log.AppendLine($"  + GateDust, {width:0.0} m wide.");
        }

        so.ApplyModifiedProperties();

        // ── What only the old opening used ──────────────────────────────────
        foreach (Transform child in ChildrenOf(stageRoot))
        {
            foreach (string prefix in RetiredStageMarkers)
            {
                if (!child.name.StartsWith(prefix)) continue;
                log.AppendLine($"  - {child.name}");
                Undo.DestroyObjectImmediate(child.gameObject);
                break;
            }
        }

        // Cam_3_Reveal too: the reveal is played on the player's own camera now.
        foreach (Transform child in ChildrenOf(camerasRoot))
        {
            if (!child.name.StartsWith("Cam_2A") && !child.name.StartsWith("Cam_3_Reveal")) continue;
            log.AppendLine($"  - {child.name}");
            Undo.DestroyObjectImmediate(child.gameObject);
        }

        foreach (Transform child in ChildrenOf(root))
        {
            if (!child.name.StartsWith("Shot2A_Setup")) continue;
            log.AppendLine($"  - {child.name}");
            Undo.DestroyObjectImmediate(child.gameObject);
        }

        int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root.gameObject);
        if (missing > 0)
        {
            Undo.RegisterCompleteObjectUndo(root.gameObject, "Remove missing scripts");
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root.gameObject);
            log.AppendLine($"  - {missing} component(s) with a deleted script on '{root.name}' (the old run and camera pan).");
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
        Selection.activeObject = director;

        log.Append("Check the RevealTrigger, Cam_4_Gate and the Nemesis marks in the Scene view, then save the scene.");
        Debug.Log(log.ToString(), director);
    }

    // ── Hub lock-down ───────────────────────────────────────────────────────

    /// <summary>
    /// The lock-down must leave the centre door as the ONLY way out of the hub: every other door on
    /// the hub's floor, within <see cref="HubDoorRadius"/> of the trigger puzzle's sockets, joins
    /// the EscapeCorridorLock list (after the ones already in it). The corridor doors were already
    /// there; the hub's other exits were not, and the player could simply walk out through them.
    /// </summary>
    private static void AddHubDoorsToLockDown(EscapeSequenceDirector director, SerializedObject so,
                                              DoorInteractable safeDoor, StringBuilder log)
    {
        EscapeCorridorLock lockDown = so.FindProperty("corridorLock").objectReferenceValue as EscapeCorridorLock;
        SO_EscapeSequenceConfig config = so.FindProperty("config").objectReferenceValue as SO_EscapeSequenceConfig;
        if (lockDown == null || config == null)
        {
            Debug.LogWarning("[EscapeChaseSetup] No corridor lock or no config on the director: the " +
                             "hub's other doors were not added to the lock-down.", director);
            return;
        }

        // The hub is where the three cores go in.
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (SocketInteractable socket in Object.FindObjectsByType<SocketInteractable>(FindObjectsInactive.Include))
        {
            if (socket.LinkedPuzzleId != config.TriggerPuzzleId) continue;
            sum += socket.transform.position;
            count++;
        }

        if (count == 0)
        {
            Debug.LogWarning($"[EscapeChaseSetup] No socket of '{config.TriggerPuzzleId}' in the scene: " +
                             "the hub's other doors were not added to the lock-down. Add them to " +
                             "EscapeCorridorLock by hand.", director);
            return;
        }

        Vector3 hub = sum / count;

        SerializedObject lso = new SerializedObject(lockDown);
        SerializedProperty doors = lso.FindProperty("doors");
        HashSet<Object> listed = new HashSet<Object>();
        for (int i = 0; i < doors.arraySize; i++) listed.Add(doors.GetArrayElementAtIndex(i).objectReferenceValue);

        foreach (DoorInteractable door in Object.FindObjectsByType<DoorInteractable>(FindObjectsInactive.Include))
        {
            if (door == safeDoor || listed.Contains(door)) continue;

            Vector3 p = door.transform.position;
            if (Mathf.Abs(p.y - hub.y) > HubSameFloor) continue;
            if (Flat(p - hub, 0f).magnitude > HubDoorRadius) continue;

            doors.arraySize++;
            doors.GetArrayElementAtIndex(doors.arraySize - 1).objectReferenceValue = door;
            log.AppendLine($"  + '{door.name}' joins the lock-down (another way out of the hub).");
        }

        lso.ApplyModifiedProperties();
    }

    // ── Objects ─────────────────────────────────────────────────────────────

    private static void EnsureMarker(SerializedProperty stage, string field, Transform parent, string name,
                                     Vector3 position, Vector3 facing, StringBuilder log)
    {
        if (Ref<Transform>(stage, field) != null) return;

        // On the NavMesh: the Nemesis warps to it, and a warp off the mesh is refused.
        bool onMesh = NavMesh.SamplePosition(position, out NavMeshHit hit, 2f, NavMesh.AllAreas);
        if (onMesh) position = hit.position;

        GameObject go = NewObject(parent, name, position, Quaternion.LookRotation(facing));
        SetRef(stage, field, go.transform);

        log.AppendLine($"  + {name.Split(' ')[0]} at {position:F1}" +
                       (onMesh ? "" : "  (NOT on the NavMesh: move it onto the walkable floor)"));
    }

    private static EscapeShotCamera NewShotCamera(Transform parent, string name, Vector3 position,
                                                  Quaternion rotation, float fov, float follow)
    {
        GameObject go = NewObject(parent, name, position, rotation);

        // -100: never live on its own. EscapeShotCamera raises it while its shot plays.
        CinemachineCamera cam = go.AddComponent<CinemachineCamera>();
        cam.Priority = -100;
        LensSettings lens = cam.Lens;
        lens.FieldOfView = fov;
        cam.Lens = lens;

        EscapeShotCamera shot = go.AddComponent<EscapeShotCamera>();
        SerializedObject sso = new SerializedObject(shot);
        sso.FindProperty("follow").floatValue = follow;
        sso.ApplyModifiedPropertiesWithoutUndo();
        return shot;
    }

    private static GameObject NewObject(Transform parent, string name, Vector3 position, Quaternion rotation)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation);
        Undo.RegisterCreatedObjectUndo(go, name);
        return go;
    }

    private static Transform ChildOrCreate(Transform parent, string name, StringBuilder log)
    {
        Transform child = parent.Find(name);
        if (child != null) return child;

        log.AppendLine($"  + {name}");
        return NewObject(parent, name, parent.position, parent.rotation).transform;
    }

    private static Transform[] ChildrenOf(Transform parent)
    {
        Transform[] children = new Transform[parent.childCount];
        for (int i = 0; i < children.Length; i++) children[i] = parent.GetChild(i);
        return children;
    }

    // ── Dust ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two sheets of dust rolling out along the floor, one to each side of the gate, and a spray of
    /// chips. The dust root faces down the corridor (its Z), so its X is the gate's width.
    /// </summary>
    private static void BuildDust(Transform root, float width, Material smoke, Material debris)
    {
        DustSheet(root, "Dust_Corridor", width, 180f, smoke);
        DustSheet(root, "Dust_FarSide", width, 0f, smoke);

        ParticleSystem chips = NewSystem(root, "Debris", debris, Quaternion.Euler(-90f, 0f, 0f));
        {
            var main = chips.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.18f, 0.17f, 0.16f, 1f),
                                                                new Color(0.4f, 0.38f, 0.35f, 1f));
            main.gravityModifier = 2.5f;

            var emission = chips.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)18) });

            // Rotated to point up: the chips spray upwards and out, then fall.
            var shape = chips.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(width, 0.2f, 0.05f);
            shape.randomDirectionAmount = 0.6f;
        }
    }

    private static void DustSheet(Transform root, string name, float width, float yaw, Material smoke)
    {
        // Box emitters throw along their Z: tipped up a little so the dust lifts off the floor.
        ParticleSystem ps = NewSystem(root, name, smoke, Quaternion.Euler(-12f, yaw, 0f));

        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 1f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        // Warm grey, the floor's dust. Never red: red is the Nemesis's alone.
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.36f, 0.33f, 0.29f, 0.6f),
                                                            new Color(0.52f, 0.48f, 0.42f, 0.45f));
        main.gravityModifier = -0.03f;

        var emission = ps.emission;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)26),
            new ParticleSystem.Burst(0.06f, (short)10),
        });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(width, 0.05f, 0.15f);
        shape.randomDirectionAmount = 0.35f;

        // Drag: it bursts out and rolls to a stop, instead of sliding away at launch speed.
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 30f;
        limit.dampen = 0f;
        limit.drag = 2.2f;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.7f));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        ps.GetComponent<ParticleSystemRenderer>().sortMode = ParticleSystemSortMode.Distance;
    }

    private static ParticleSystem NewSystem(Transform root, string name, Material material, Quaternion localRotation)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localRotation = localRotation;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.3f;
        main.loop = false;
        main.playOnAwake = false;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 120;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return ps;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static T Ref<T>(SerializedProperty stage, string field) where T : Object =>
        stage.FindPropertyRelative(field)?.objectReferenceValue as T;

    private static void SetRef(SerializedProperty stage, string field, Object value)
    {
        SerializedProperty property = stage.FindPropertyRelative(field);
        if (property != null) property.objectReferenceValue = value;
    }

    private static Bounds BoundsOf(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static Vector3 Flat(Vector3 v, float y) => new Vector3(v.x, y, v.z);

    /// <summary>
    /// Down the corridor from the gate and to the side with more room, as far back as
    /// <see cref="GateCameraBack"/> — closer if something (a door frame across the corridor) stands
    /// between that spot and the gate, or between it and where the Nemesis will start.
    /// </summary>
    private static Vector3 GateCameraPosition(Vector3 gateCentre, Vector3 approach, Vector3 look)
    {
        Vector3 side = Vector3.Cross(Vector3.up, approach);
        Vector3 fallback = Vector3.zero;

        for (float back = GateCameraBack; back >= GateStopDistance + 3f; back -= 0.5f)
        {
            Vector3 camBase = gateCentre - approach * back + Vector3.up * GateCameraHeight;

            // The side with more room, and never closer than a hand to the wall.
            float roomRight = Room(camBase, side);
            float roomLeft = Room(camBase, -side);
            Vector3 sideDir = roomRight >= roomLeft ? side : -side;
            float offset = Mathf.Clamp(Mathf.Max(roomRight, roomLeft) - 0.35f, 0f, GateCameraSide);

            Vector3 position = camBase + sideDir * offset;
            if (back == GateCameraBack) fallback = position;

            float startDistance = Mathf.Clamp(back - 2f, GateStopDistance + 2.5f, GateStartDistance);
            Vector3 nemesis = gateCentre - approach * startDistance + Vector3.up * 1.4f;

            if (Clear(position, look) && Clear(position, nemesis)) return position;
        }

        Debug.LogWarning("[EscapeChaseSetup] Found no spot down the corridor with a clear view of the " +
                         "gate: Cam_4_Gate was placed at the default distance, move it by hand.");
        return fallback;
    }

    private static bool Clear(Vector3 from, Vector3 to) =>
        !Physics.Linecast(from, to, ~0, QueryTriggerInteraction.Ignore);

    /// <summary>Free distance from a point along a direction, up to 3 m.</summary>
    private static float Room(Vector3 from, Vector3 direction)
    {
        const float Max = 3f;
        return Physics.Raycast(from, direction, out RaycastHit hit, Max, ~0, QueryTriggerInteraction.Ignore)
            ? hit.distance
            : Max;
    }
}
