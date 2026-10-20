#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Puzzles > Set Up Reward Boxes. Turns the box models (Caja Panel, Caja Teclado) into
/// working <see cref="PuzzleRewardBox"/>es and links the Zona 1 boxes to their puzzles.
///
/// Safe to re-run: every step only fills in what is missing, and the parts a person tunes by hand
/// (where the light, the shot and the item anchor stand, the light's range, the camera's lens) are
/// placed once, when they are created, and never moved again.
///   1. BoxController: one layer per moving part (Lid, LatchRight, LatchLeft), Closed → Open, with
///      the model's own clips. Rebuilt only when it does not have that shape.
///   2. SO_RewardBoxConfig, created if missing.
///   3. Each prefab: Interactable layer and a collider that fits the box, the Animator, the rig
///      (locks paired with their latches, glow material), ExcludeFromHighlight on every part with the
///      glow, the light + fog beacon, the item anchor with its Item Visual (off), the shot camera
///      and its target, the box and the crosshair highlight (its own softer profile,
///      SO_Highlight_RewardBox). Material slots left empty by the model's extraction are refilled
///      from the model.
///   4. The open scenes: each "BoxSupport P1_SPn" box with no puzzle gets SPn's id. The scene is
///      marked dirty, not saved.
/// </summary>
public static class RewardBoxSetup
{
    private const string LogTag = "[RewardBoxSetup]";

    private const string ModelPath = "Assets/_Project/Art/Models/Props/Caja/Caja.fbx";
    private const string ControllerPath = "Assets/_Project/Art/Animations/Caja/BoxController.controller";
    private const string GlowMaterialPath = "Assets/_Project/Art/Models/Props/Caja/Brillo.mat";
    private const string ConfigFolder = "Assets/_Project/ScriptableObjects/RewardBox";
    private const string ConfigPath = ConfigFolder + "/SO_RewardBoxConfig.asset";
    private const string CategoryConfigPath = "Assets/_Project/ScriptableObjects/CategoryConfig/ItemCategory.asset";
    private const string HighlightPath = InteractableHighlightSetup.ProfileFolder + "/SO_Highlight_RewardBox.asset";

    private static readonly string[] BoxPrefabs =
    {
        "Assets/_Project/Prefabs/Box/Caja Panel.prefab",
        "Assets/_Project/Prefabs/Box/Caja Teclado.prefab",
    };

    private const string LidClip = "Tapa.005|Tapa.005Action";
    private const string LatchRightClip = "CerraduraDER|CerraduraDERAction";
    private const string LatchLeftClip = "CerraduraIZQ|CerraduraIZQAction";

    private const string LidPart = "Tapa.005";
    private const string LatchRightPart = "CerraduraDER";
    private const string LatchLeftPart = "CerraduraIZQ";
    private const string LockPartKey = "zierCurve"; // BézierCurve, BézierCurve.001

    private static readonly (string support, string puzzleId)[] Zona1Boxes =
    {
        ("BoxSupport P1_SP1", "sp1_panel_electrico"),
        ("BoxSupport P1_SP2", "sp2_contenedores"),
        ("BoxSupport P1_SP3", "sp3_valvulas"),
    };

    [MenuItem("Tools/Puzzles/Set Up Reward Boxes")]
    private static void SetUp()
    {
        var log = new StringBuilder();

        AnimationClip lid = FindClip(LidClip, log);
        AnimationClip latchRight = FindClip(LatchRightClip, log);
        AnimationClip latchLeft = FindClip(LatchLeftClip, log);
        if (lid == null || latchRight == null || latchLeft == null)
        {
            Debug.LogError($"{LogTag} The model's clips are missing, nothing done.\n{log}");
            return;
        }

        AnimatorController controller = BuildController(lid, latchRight, latchLeft, log);
        SO_RewardBoxConfig config = LoadOrCreateConfig(log);
        var glow = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
        SO_HighlightProfile highlight = LoadOrCreateHighlight(log);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        foreach (string path in BoxPrefabs)
            SetUpPrefab(path, model, controller, lid, latchRight, latchLeft, config, glow, highlight, log);

        AssetDatabase.SaveAssets();
        WireOpenScenes(log);

        Debug.Log($"{LogTag} Done.\n{log}");
    }

    // ── Clips and controller ────────────────────────────────────────────────

    private static AnimationClip FindClip(string clipName, StringBuilder log)
    {
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                                          .FirstOrDefault(c => c.name == clipName);
        if (clip == null)
        {
            log.AppendLine($"MISSING clip '{clipName}' in {ModelPath}");
            return null;
        }

        string paths = string.Join(", ", AnimationUtility.GetCurveBindings(clip).Select(b => b.path).Distinct());
        log.AppendLine($"Clip '{clipName}': {clip.length:0.00}s, animates [{paths}]");
        return clip;
    }

    private static AnimatorController BuildController(AnimationClip lid, AnimationClip latchRight,
                                                      AnimationClip latchLeft, StringBuilder log)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        var wanted = new[] { ("Lid", lid), ("LatchRight", latchRight), ("LatchLeft", latchLeft) };
        if (HasShape(controller, wanted))
        {
            log.AppendLine("BoxController already built.");
            return controller;
        }

        while (controller.layers.Length > 0) controller.RemoveLayer(0);

        foreach ((string layerName, AnimationClip clip) in wanted)
        {
            controller.AddLayer(layerName);
            AnimatorStateMachine machine = controller.layers[controller.layers.Length - 1].stateMachine;

            // Write Defaults off: each layer drives its own part only, and a state that wrote
            // defaults would snap the other layers' parts back to closed.
            AnimatorState closed = machine.AddState("Closed");
            closed.writeDefaultValues = false;
            AnimatorState open = machine.AddState("Open");
            open.motion = clip;
            open.writeDefaultValues = false;
            machine.defaultState = closed;
        }

        AnimatorControllerLayer[] layers = controller.layers;
        for (int i = 0; i < layers.Length; i++) layers[i].defaultWeight = 1f;
        controller.layers = layers;

        EditorUtility.SetDirty(controller);
        log.AppendLine("Built BoxController (Lid, LatchRight, LatchLeft).");
        return controller;
    }

    private static bool HasShape(AnimatorController controller, (string name, AnimationClip clip)[] wanted)
    {
        AnimatorControllerLayer[] layers = controller.layers;
        if (layers.Length != wanted.Length) return false;

        for (int i = 0; i < wanted.Length; i++)
        {
            if (layers[i].name != wanted[i].name) return false;
            ChildAnimatorState[] states = layers[i].stateMachine.states;
            if (!states.Any(s => s.state.name == "Open" && s.state.motion == wanted[i].clip)) return false;
            if (!states.Any(s => s.state.name == "Closed")) return false;
        }
        return true;
    }

    private static SO_RewardBoxConfig LoadOrCreateConfig(StringBuilder log)
    {
        var config = AssetDatabase.LoadAssetAtPath<SO_RewardBoxConfig>(ConfigPath);
        if (config != null) return config;

        if (!AssetDatabase.IsValidFolder(ConfigFolder))
            AssetDatabase.CreateFolder("Assets/_Project/ScriptableObjects", "RewardBox");

        config = ScriptableObject.CreateInstance<SO_RewardBoxConfig>();
        AssetDatabase.CreateAsset(config, ConfigPath);

        var so = new SerializedObject(config);
        so.FindProperty("categoryConfig").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<SO_ItemCategoryConfig>(CategoryConfigPath);
        so.ApplyModifiedPropertiesWithoutUndo();

        log.AppendLine($"Created {ConfigPath}");
        return config;
    }

    /// <summary>
    /// The box's own crosshair highlight: a copy of the shared props profile with a third of its
    /// brightness on lit materials (litEmissionScale 6 → 2, the doors' value). The box's big flat lid
    /// washed out to white with the shared one, the same reason the doors have theirs.
    /// </summary>
    private static SO_HighlightProfile LoadOrCreateHighlight(StringBuilder log)
    {
        var profile = AssetDatabase.LoadAssetAtPath<SO_HighlightProfile>(HighlightPath);
        if (profile != null) return profile;

        var shared = AssetDatabase.LoadAssetAtPath<SO_HighlightProfile>(InteractableHighlightSetup.PropsProfilePath);
        profile = ScriptableObject.CreateInstance<SO_HighlightProfile>();
        if (shared != null) EditorUtility.CopySerialized(shared, profile);
        AssetDatabase.CreateAsset(profile, HighlightPath);

        var so = new SerializedObject(profile);
        so.FindProperty("litEmissionScale").floatValue = 2f;
        so.ApplyModifiedPropertiesWithoutUndo();

        log.AppendLine($"Created {HighlightPath}");
        return profile;
    }

    // ── Prefabs ─────────────────────────────────────────────────────────────

    private static void SetUpPrefab(string path, GameObject model, AnimatorController controller,
                                    AnimationClip lidClip, AnimationClip latchRightClip, AnimationClip latchLeftClip,
                                    SO_RewardBoxConfig config, Material glow, SO_HighlightProfile highlight,
                                    StringBuilder log)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            log.AppendLine($"— {path}");
            Transform box = root.transform;

            RefillMissingMaterials(box, model != null ? model.transform : null, log);

            Transform lid = FindDeep(box, LidPart);
            Transform latchRight = FindDeep(box, LatchRightPart);
            Transform latchLeft = FindDeep(box, LatchLeftPart);
            List<Transform> locks = box.GetComponentsInChildren<Transform>(true)
                                       .Where(t => t.name.Contains(LockPartKey)).ToList();
            if (lid == null || latchRight == null || latchLeft == null || locks.Count != 2)
            {
                log.AppendLine($"  SKIPPED: expected {LidPart}, {LatchRightPart}, {LatchLeftPart} and two locks.");
                return;
            }

            (Transform lockRight, Transform lockLeft) = PairLocks(box, locks, latchRight, latchLeft, log);

            Bounds all = LocalBounds(box, box);
            Bounds body = LocalBounds(box, FindDeep(box, "Base") ?? box);

            // Root: what the crosshair finds.
            int interactable = LayerMask.NameToLayer("Interactable");
            if (interactable >= 0) box.gameObject.layer = interactable;
            var collider = GetOrAdd<BoxCollider>(box.gameObject);
            collider.center = all.center;
            collider.size = all.size;

            var animator = GetOrAdd<Animator>(box.gameObject);
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.keepAnimatorStateOnDisable = true;

            foreach (Renderer renderer in box.GetComponentsInChildren<Renderer>(true))
            {
                if (glow != null && renderer.sharedMaterials.Contains(glow))
                    GetOrAdd<ExcludeFromHighlight>(renderer.gameObject);
            }

            // Light above the front edge: the lid opens backwards and never reaches it.
            Transform lightPoint = GetOrCreateChild(box, "RewardLight",
                new Vector3(all.center.x, all.max.y + 0.12f, all.max.z + 0.1f), out bool lightCreated);
            var light = GetOrAdd<Light>(lightPoint.gameObject);
            var beacon = GetOrAdd<FogBeacon>(lightPoint.gameObject);
            if (lightCreated)
            {
                light.type = LightType.Point;
                light.range = 2.5f;
                light.intensity = 1f;
                light.shadows = LightShadows.None;
                beacon.intensity = 2.5f;
                beacon.worldRadius = 0.04f;
                beacon.minPixelRadius = 4f;
                beacon.limitByFacing = false;
            }

            Transform anchor = GetOrCreateChild(box, "ItemAnchor",
                new Vector3(body.center.x, body.min.y + 0.03f, body.center.z), out _);
            // What shows inside once open. Off until then: the box's inspector fills it (Item Model).
            Transform visual = GetOrCreateChild(anchor, "ItemVisual", Vector3.zero, out bool visualCreated);
            if (visualCreated) visual.gameObject.SetActive(false);
            float inside = Mathf.Min(body.size.x, body.size.z) * 0.7f;
            float headroom = body.max.y - (body.min.y + 0.03f) - 0.02f;
            float fit = Mathf.Max(0.05f, Mathf.Min(inside, headroom));

            Transform target = GetOrCreateChild(box, "ShotTarget",
                new Vector3(all.center.x, all.center.y + all.extents.y * 0.3f, all.center.z), out _);
            Transform shotPoint = GetOrCreateChild(box, "Shot",
                new Vector3(all.center.x, all.max.y + 0.65f, all.max.z + 1.15f), out bool shotCreated);
            var camera = GetOrAdd<CinemachineCamera>(shotPoint.gameObject);
            var shot = GetOrAdd<EscapeShotCamera>(shotPoint.gameObject);
            if (shotCreated)
            {
                shotPoint.rotation = Quaternion.LookRotation(target.position - shotPoint.position);
                camera.Priority = -100; // never live on its own; EscapeShotCamera raises it
                LensSettings lens = camera.Lens;
                lens.FieldOfView = 45f;
                lens.NearClipPlane = 0.05f;
                camera.Lens = lens;
            }

            var rig = GetOrAdd<RewardBoxRig>(box.gameObject);
            var rigSo = new SerializedObject(rig);
            SetIfEmpty(rigSo, "animator", animator);
            rigSo.FindProperty("lidClip").objectReferenceValue = lidClip;
            rigSo.FindProperty("latchRightClip").objectReferenceValue = latchRightClip;
            rigSo.FindProperty("latchLeftClip").objectReferenceValue = latchLeftClip;
            SetIfEmpty(rigSo, "lockRight", lockRight);
            SetIfEmpty(rigSo, "lockLeft", lockLeft);
            SetIfEmpty(rigSo, "glowMaterial", glow);
            SetIfEmpty(rigSo, "boxLight", light);
            SetIfEmpty(rigSo, "beacon", beacon);
            SetIfEmpty(rigSo, "itemVisual", visual.gameObject);
            rigSo.FindProperty("itemFitSize").floatValue = fit;
            rigSo.ApplyModifiedPropertiesWithoutUndo();

            var rewardBox = GetOrAdd<PuzzleRewardBox>(box.gameObject);
            var boxSo = new SerializedObject(rewardBox);
            SetIfEmpty(boxSo, "config", config);
            SetIfEmpty(boxSo, "rig", rig);
            SetIfEmpty(boxSo, "shot", shot);
            SetIfEmpty(boxSo, "shotTarget", target);
            boxSo.ApplyModifiedPropertiesWithoutUndo();

            var proximity = GetOrAdd<ItemProximityHighlight>(box.gameObject);
            var proximitySo = new SerializedObject(proximity);
            // The shared props profile washed the box out: it gets its own, softer one.
            SerializedProperty profile = proximitySo.FindProperty("profile");
            var shared = AssetDatabase.LoadAssetAtPath<SO_HighlightProfile>(InteractableHighlightSetup.PropsProfilePath);
            if (profile.objectReferenceValue == null || profile.objectReferenceValue == shared)
                profile.objectReferenceValue = highlight;
            proximitySo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, path);
            log.AppendLine($"  Set up. Right lock = {lockRight.name}, item fits {fit:0.00} m, collider {all.size}.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// The lock that opens first is the one next to the right latch. If the two latches sit on top of
    /// each other (their meshes carry the offset), "right" is the right seen from the front: the box
    /// faces +Z, so that is the lock with the smaller X.
    /// </summary>
    private static (Transform right, Transform left) PairLocks(Transform box, List<Transform> locks,
                                                               Transform latchRight, Transform latchLeft,
                                                               StringBuilder log)
    {
        float LocalX(Transform part) => LocalBounds(box, part).center.x;

        float rightX = LocalX(latchRight);
        float leftX = LocalX(latchLeft);

        Transform first = locks[0], second = locks[1];
        bool firstIsRight;
        if (Mathf.Abs(rightX - leftX) > 0.05f)
        {
            firstIsRight = Mathf.Abs(LocalX(first) - rightX) < Mathf.Abs(LocalX(second) - rightX);
            log.AppendLine($"  Locks paired by latch (right latch at x={rightX:0.00}, left at x={leftX:0.00}).");
        }
        else
        {
            firstIsRight = LocalX(first) < LocalX(second);
            log.AppendLine("  Latches overlap: right lock = smaller X (the right seen from the front).");
        }

        return firstIsRight ? (first, second) : (second, first);
    }

    private static void RefillMissingMaterials(Transform box, Transform model, StringBuilder log)
    {
        if (model == null) return;

        foreach (Renderer renderer in box.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            if (!materials.Any(m => m == null)) continue;

            Transform source = FindDeep(model, renderer.name);
            Renderer sourceRenderer = source != null ? source.GetComponent<Renderer>() : null;
            if (sourceRenderer == null) continue;

            Material[] sourceMaterials = sourceRenderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length && i < sourceMaterials.Length; i++)
            {
                if (materials[i] != null || sourceMaterials[i] == null) continue;
                materials[i] = sourceMaterials[i];
                changed = true;
            }

            if (!changed) continue;
            renderer.sharedMaterials = materials;
            log.AppendLine($"  Refilled missing materials on {renderer.name}.");
        }
    }

    // ── Scene ───────────────────────────────────────────────────────────────

    private static void WireOpenScenes(StringBuilder log)
    {
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            bool changed = false;
            foreach (GameObject rootObject in scene.GetRootGameObjects())
            foreach (Transform t in rootObject.GetComponentsInChildren<Transform>(true))
            {
                foreach ((string support, string puzzleId) in Zona1Boxes)
                {
                    if (t.name != support) continue;

                    PuzzleRewardBox box = t.GetComponentInChildren<PuzzleRewardBox>(true);
                    if (box == null)
                    {
                        log.AppendLine($"  {scene.name}/{support}: no PuzzleRewardBox under it.");
                        continue;
                    }

                    var so = new SerializedObject(box);
                    SerializedProperty id = so.FindProperty("puzzleId");
                    SerializedProperty source = so.FindProperty("puzzleSource");
                    if (string.IsNullOrWhiteSpace(id.stringValue)) id.stringValue = puzzleId;
                    ScriptableObject found = PuzzleRewardBoxEditor.FindRewardSource(id.stringValue);
                    if (source.objectReferenceValue != found) source.objectReferenceValue = found;

                    if (so.ApplyModifiedProperties())
                    {
                        changed = true;
                        log.AppendLine($"  {scene.name}/{support}: puzzle '{id.stringValue}', source {(found != null ? found.name : "none")}.");
                    }
                }
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                log.AppendLine($"Scene '{scene.name}' changed — save it.");
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Bounds of every renderer under <paramref name="part"/>, in <paramref name="space"/>'s local space.</summary>
    private static Bounds LocalBounds(Transform space, Transform part)
    {
        Bounds bounds = default;
        bool any = false;
        Matrix4x4 toSpace = space.worldToLocalMatrix;

        foreach (MeshFilter filter in part.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Matrix4x4 m = toSpace * filter.transform.localToWorldMatrix;
            Bounds mesh = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mesh.center + Vector3.Scale(mesh.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        return bounds;
    }

    private static Transform FindDeep(Transform parent, string childName)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t != parent && t.name == childName) return t;
        return null;
    }

    private static Transform GetOrCreateChild(Transform parent, string childName, Vector3 localPosition, out bool created)
    {
        Transform child = parent.Find(childName);
        created = child == null;
        if (!created) return child;

        child = new GameObject(childName).transform;
        child.SetParent(parent, false);
        child.localPosition = localPosition;
        return child;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component =>
        go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

    private static void SetIfEmpty(SerializedObject so, string property, Object value)
    {
        SerializedProperty p = so.FindProperty(property);
        if (p != null && p.objectReferenceValue == null) p.objectReferenceValue = value;
    }
}
#endif
