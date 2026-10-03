using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click setup for the grab that plays on a capture (CaptureGrabStaging, CaptureGrabCamera):
///
///   1. Builds "Grabbed (Player Rig).anim" from the E_Grabbed take of Player.fbx. That take was
///      animated standing in front of the Nemesis, so its root node carries the whole placement:
///      1.69 m forward, turned round to face it, rising 25 cm as the hands lift. Played as it is,
///      those curves land on the player's model root and drag it out from under its own capsule in
///      every state. The copy drops them, keeps rotations for every bone and positions only for
///      the Hips (same rule as PlayerStandUpSetup), and folds the root's rise into the Hips so the
///      body is still lifted.
///   2. Adds a "Grabbed" state for it to PlayerController, with no way out: CaptureGrabStaging
///      plays it, and the stand-up at the respawn (or that component, when there is none) leaves it.
///   3. Measures the pair off the two clips and writes it to SO_CaptureGrabConfig: the distance
///      between the two bodies FROM THE NEMESIS'S ARMS — how far its wrists reach in the hold
///      pose, at the scale the prefab gives the model, plus Grip Standoff — the yaw the player was
///      animated at, and the points of the grab the shot is scored on.
///   4. Creates the config and the shot's fog preset when they are missing, and puts the two
///      components on the Player prefab's root.
///
/// Idempotent: the clip is rewritten in place (same GUID), the state and the components are
/// reused, and the fog preset is only created, never overwritten. Run it again after changing the
/// Nemesis's scale, either clip, or Grip Standoff.
/// </summary>
public static class CaptureGrabSetup
{
    private const string AnimFolder = "Assets/_Project/Art/Animations/Animations_Mixamo/Player Animations/";
    private const string ControllerPath = AnimFolder + "PlayerController.controller";
    private const string ClipPath = AnimFolder + "Grabbed (Player Rig).anim";

    private const string PlayerFbxPath = "Assets/_Project/Art/Models/Characters/Player/Player.fbx";
    private const string NemesisFbxPath = "Assets/_Project/Art/Models/Characters/Nemesis/New/TLLStalker.fbx";
    private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
    private const string NemesisPrefabPath = "Assets/_Project/Prefabs/Nemesis.prefab";

    private const string ConfigPath = "Assets/_Project/ScriptableObjects/Player/SO_CaptureGrabConfig.asset";
    private const string FogPath = "Assets/_Project/ScriptableObjects/Rendering/Fog/SO_VisionFog_CaptureGrab.asset";
    private const string AreaFogPath = "Assets/_Project/ScriptableObjects/Rendering/Fog/SO_VisionFog_Dark.asset";

    private const string GrabbedTake = "E_Grabbed";
    private const string KillTake = "E_KillPlayer";
    private const string IdleStateName = "Idle";

    private const string HipsName = "mixamorig:Hips";
    private const string PlayerHeadName = "mixamorig:Head";
    private const string PlayerHeadTopName = "mixamorig:HeadTop_End";
    private const string NemesisHeadName = "Face.Upper";
    private static readonly string[] NemesisWristNames = { "hand.L", "hand.R" };

    // The part of both clips where the Nemesis is holding: the hands have closed and lifted well
    // before half way, and nothing lets go until the end.
    private const float HoldFrom = 0.5f;
    private const int HoldSamples = 12;

    // The shot's fog: the area's own preset with the band pushed out past the Nemesis's arms,
    // which open 2.8 m from the player. Only used to create the asset.
    private const float ShotFogStart = 4f;
    private const float ShotFogEnd = 9f;
    private const float ShotFogTransition = 0.35f;

    private static readonly Vector3 StatePosition = new Vector3(500f, 520f, 0f);

    [MenuItem("Tools/Player/Setup Capture Grab")]
    public static void Setup()
    {
        var log = new StringBuilder("[CaptureGrabSetup]\n");

        AnimationClip source = LoadFbxClip(PlayerFbxPath, GrabbedTake);
        AnimationClip kill = LoadFbxClip(NemesisFbxPath, KillTake);
        if (source == null || kill == null)
        {
            Debug.LogError($"[CaptureGrabSetup] Missing a clip: '{GrabbedTake}' in {PlayerFbxPath} " +
                           $"({(source != null ? "found" : "NOT found")}), '{KillTake}' in {NemesisFbxPath} " +
                           $"({(kill != null ? "found" : "NOT found")}). Nothing was changed.");
            return;
        }

        AnimationClip clip = BuildClip(source, log);
        AddState(clip, log);

        SO_CaptureGrabConfig config = LoadOrCreateConfig(log);
        MeasurePair(source, kill, config, log);
        config.SetFogPresetIfEmpty(LoadOrCreateFog(log));
        EditorUtility.SetDirty(config);

        WirePlayerPrefab(config, log);

        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    // ── Clip ─────────────────────────────────────────────────────────────────────────────

    private static AnimationClip BuildClip(AnimationClip source, StringBuilder log)
    {
        var clip = new AnimationClip { name = "Grabbed (Player Rig)", frameRate = source.frameRate };

        AnimationCurve hipsY = null;
        AnimationCurve rootY = null;
        string hipsPath = HipsName;

        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);

            if (binding.type == typeof(Transform))
            {
                // The root node is where the take stands relative to the Nemesis. On the player it
                // would move the whole model; only its rise is kept, on the Hips.
                if (string.IsNullOrEmpty(binding.path))
                {
                    if (binding.propertyName == "m_LocalPosition.y") rootY = curve;
                    continue;
                }

                if (binding.propertyName.StartsWith("m_LocalScale")) continue;

                bool isHips = binding.path == HipsName || binding.path.EndsWith("/" + HipsName);
                if (binding.propertyName.StartsWith("m_LocalPosition") && !isHips) continue;

                if (isHips && binding.propertyName == "m_LocalPosition.y")
                {
                    hipsY = curve;
                    hipsPath = binding.path;
                    continue;
                }
            }

            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        if (hipsY != null)
        {
            AnimationCurve lifted = rootY != null ? Sum(hipsY, rootY, source.length, source.frameRate) : hipsY;
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(hipsPath, typeof(Transform), "m_LocalPosition.y"), lifted);
        }

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
        settings.loopTime = false;
        settings.startTime = 0f;
        settings.stopTime = source.length;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        float rise = rootY != null ? rootY.keys.Max(k => k.value) - rootY.keys.Min(k => k.value) : 0f;
        log.AppendLine($"clip: {ClipPath} — {source.length:F2} s at {source.frameRate:F0} fps, root curves dropped, " +
                       $"{rise * 100f:F0} cm of rise folded into the Hips.");

        return SaveClip(clip, ClipPath);
    }

    /// <summary>One key per frame of <paramref name="a"/> + <paramref name="b"/>.</summary>
    private static AnimationCurve Sum(AnimationCurve a, AnimationCurve b, float length, float frameRate)
    {
        int frames = Mathf.Max(1, Mathf.RoundToInt(length * frameRate));
        var keys = new Keyframe[frames + 1];

        for (int i = 0; i <= frames; i++)
        {
            float t = length * i / frames;
            keys[i] = new Keyframe(t, a.Evaluate(t) + b.Evaluate(t));
        }

        var sum = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++) sum.SmoothTangents(i, 0f);
        return sum;
    }

    private static AnimationClip LoadFbxClip(string fbxPath, string take)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__") && clip.name.EndsWith(take))
                return clip;
        }
        return null;
    }

    /// <summary>Overwrites an existing asset in place, so its GUID — and the state using it — survive.</summary>
    private static AnimationClip SaveClip(AnimationClip clip, string path)
    {
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        EditorUtility.CopySerialized(clip, existing);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    // ── Controller ───────────────────────────────────────────────────────────────────────

    private static void AddState(AnimationClip clip, StringBuilder log)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null || controller.layers.Length == 0)
        {
            Debug.LogError($"[CaptureGrabSetup] No AnimatorController at {ControllerPath}: the state was not added.");
            return;
        }

        string stateName = DefaultStateName();
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idle = FindState(machine, IdleStateName);

        AnimatorState state = FindState(machine, stateName);
        bool added = state == null;
        if (added) state = machine.AddState(stateName, StatePosition);

        state.motion = clip;
        state.speed = 1f;
        if (idle != null) state.writeDefaultValues = idle.writeDefaultValues;

        // No way out: no parameter the FSM writes may cut the grab short, and an exit-time blend
        // into Idle would put the player back on their feet in the monster's hands.
        foreach (AnimatorStateTransition old in state.transitions) state.RemoveTransition(old);

        EditorUtility.SetDirty(state);
        EditorUtility.SetDirty(controller);
        log.AppendLine($"state: '{stateName}' {(added ? "added to" : "updated in")} PlayerController, no transitions out.");
    }

    private static string DefaultStateName()
    {
        SO_CaptureGrabConfig config = AssetDatabase.LoadAssetAtPath<SO_CaptureGrabConfig>(ConfigPath);
        return config != null && !string.IsNullOrEmpty(config.GrabbedState) ? config.GrabbedState : "Grabbed";
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string stateName)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state.name == stateName) return child.state;
        }
        return null;
    }

    // ── Measuring ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Poses both rigs through the hold and reads the pair off them, in metres with the Nemesis's
    /// pivot at the origin facing +Z — the frame both takes were animated in.
    /// </summary>
    private static void MeasurePair(AnimationClip grabbed, AnimationClip kill, SO_CaptureGrabConfig config, StringBuilder log)
    {
        // The Nemesis, as the prefab scales it: the reach of the arms is the scaled one.
        Vector3 wrist = Vector3.zero;
        Vector3 nemesisHead = Vector3.zero;
        float halfWidth = 0f;

        GameObject nemesis = PrefabUtility.LoadPrefabContents(NemesisPrefabPath);
        try
        {
            nemesis.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Animator animator = nemesis.GetComponentInChildren<Animator>(true);
            Transform[] bones = animator.GetComponentsInChildren<Transform>(true);
            Transform[] wrists = NemesisWristNames.Select(n => bones.FirstOrDefault(b => b.name == n)).ToArray();
            Transform head = bones.FirstOrDefault(b => b.name == NemesisHeadName);

            if (wrists.Any(w => w == null) || head == null)
            {
                Debug.LogError("[CaptureGrabSetup] The Nemesis rig has no '" + string.Join("' / '", NemesisWristNames) +
                               $"' or no '{NemesisHeadName}': the pair was not measured, the config keeps its values.");
                return;
            }

            for (int i = 0; i < HoldSamples; i++)
            {
                kill.SampleAnimation(animator.gameObject, HoldTime(kill, i));

                foreach (Transform w in wrists)
                {
                    wrist += new Vector3(0f, w.position.y, w.position.z);
                    halfWidth += Mathf.Abs(w.position.x);
                }
                nemesisHead += head.position;
            }

            wrist /= HoldSamples * wrists.Length;
            halfWidth /= HoldSamples * wrists.Length;
            nemesisHead /= HoldSamples;

            log.AppendLine($"Nemesis (model scale {animator.transform.localScale.ToString("F3")}): wrists hold " +
                           $"{wrist.z:F3} m in front of its pivot, {wrist.y:F2} m up, {halfWidth * 2f:F2} m apart.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(nemesis);
        }

        // The player, off the take as animated: its root is where the animator stood it.
        Vector3 authoredRoot = Vector3.zero;
        float authoredYaw = 180f;
        float headHeight = 0f;

        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerFbxPath));
        try
        {
            SceneManager.MoveGameObjectToScene(player, preview);
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            Transform[] bones = player.GetComponentsInChildren<Transform>(true);
            Transform head = bones.FirstOrDefault(b => b.name == PlayerHeadName);
            Transform headTop = bones.FirstOrDefault(b => b.name == PlayerHeadTopName);

            for (int i = 0; i < HoldSamples; i++)
            {
                grabbed.SampleAnimation(player, HoldTime(grabbed, i));

                authoredRoot += player.transform.position;
                authoredYaw = player.transform.eulerAngles.y;
                if (head != null) headHeight += headTop != null ? (head.position.y + headTop.position.y) * 0.5f : head.position.y;
            }

            authoredRoot /= HoldSamples;
            headHeight = head != null ? headHeight / HoldSamples : config.PlayerHeadHeight;
        }
        finally
        {
            Object.DestroyImmediate(player);
            EditorSceneManager.ClosePreviewScene(preview);
        }

        float distance = wrist.z + config.GripStandoff;
        float yawOffset = Mathf.DeltaAngle(180f, authoredYaw);

        config.SetMeasuredPair(distance, yawOffset, wrist.y, halfWidth, headHeight, nemesisHead.y, nemesisHead.z);

        log.AppendLine($"pair distance: {wrist.z:F3} (wrist reach) + {config.GripStandoff:F2} (grip standoff) = {distance:F3} m. " +
                       $"The take was animated at {authoredRoot.z:F3} m, which puts the wrists " +
                       $"{authoredRoot.z - wrist.z:F2} m in front of the player's pivot.");
        log.AppendLine($"player: turned {yawOffset:F0} deg off facing the Nemesis, head at {headHeight:F2} m while held " +
                       $"(root rises {authoredRoot.y:F2} m on average over the hold). Nemesis head at " +
                       $"{nemesisHead.y:F2} m, {nemesisHead.z:F2} m forward.");
    }

    private static float HoldTime(AnimationClip clip, int sample) =>
        clip.length * Mathf.Lerp(HoldFrom, 1f, sample / (HoldSamples - 1f));

    // ── Assets ───────────────────────────────────────────────────────────────────────────

    private static SO_CaptureGrabConfig LoadOrCreateConfig(StringBuilder log)
    {
        SO_CaptureGrabConfig config = AssetDatabase.LoadAssetAtPath<SO_CaptureGrabConfig>(ConfigPath);
        if (config != null) return config;

        config = ScriptableObject.CreateInstance<SO_CaptureGrabConfig>();
        AssetDatabase.CreateAsset(config, ConfigPath);
        log.AppendLine($"config: created {ConfigPath}.");
        return config;
    }

    private static SO_VisionFogConfig LoadOrCreateFog(StringBuilder log)
    {
        SO_VisionFogConfig fog = AssetDatabase.LoadAssetAtPath<SO_VisionFogConfig>(FogPath);
        if (fog != null) return fog;

        // The area's own look, with only the band moved: the shot should read as the same place.
        SO_VisionFogConfig area = AssetDatabase.LoadAssetAtPath<SO_VisionFogConfig>(AreaFogPath);
        fog = area != null ? Object.Instantiate(area) : ScriptableObject.CreateInstance<SO_VisionFogConfig>();
        fog.name = System.IO.Path.GetFileNameWithoutExtension(FogPath);
        fog.visionStart = ShotFogStart;
        fog.visionEnd = ShotFogEnd;
        fog.transitionDuration = ShotFogTransition;

        AssetDatabase.CreateAsset(fog, FogPath);
        log.AppendLine($"fog: created {FogPath} ({(area != null ? area.name : "defaults")} with the band at " +
                       $"{ShotFogStart:F0}-{ShotFogEnd:F0} m).");
        return fog;
    }

    // ── Player prefab ────────────────────────────────────────────────────────────────────

    private static void WirePlayerPrefab(SO_CaptureGrabConfig config, StringBuilder log)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            bool changed = false;
            changed |= Ensure<CaptureGrabStaging>(root, config);
            changed |= Ensure<CaptureGrabCamera>(root, config);

            if (changed) PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            log.AppendLine(changed
                ? "prefab: CaptureGrabStaging and CaptureGrabCamera are on the Player root, with the config."
                : "prefab: Player already had both components wired.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <returns>True when the prefab had to change.</returns>
    private static bool Ensure<T>(GameObject root, SO_CaptureGrabConfig config) where T : Component
    {
        bool changed = false;

        T component = root.GetComponent<T>();
        if (component == null)
        {
            component = root.AddComponent<T>();
            changed = true;
        }

        var serialized = new SerializedObject(component);
        SerializedProperty property = serialized.FindProperty("config");
        if (property != null && property.objectReferenceValue != config)
        {
            property.objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }

        return changed;
    }
}
