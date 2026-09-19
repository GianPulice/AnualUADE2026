using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// One-click setup for the player's two stand-up animations (see PlayerStateManager.EStandUp):
///   - "Init Stand Up": the wake-up at the start of the level;
///   - "Standing Up": getting up at the checkpoint after the Nemesis caught the player.
///
/// The two FBX were exported from Mixamo with a different rig upload, so their bones are named
/// "mixamorig8:*" while the player's are "mixamorig:*". Played as they are, a Generic Animator
/// binds none of their curves and the player does not move. This builds a copy of each clip with the
/// paths renamed to the player's rig, next to the FBX:
///   - rotations are kept for every bone; positions only for the Hips (the other bones keep the
///     player's own lengths); scales and root-node curves are dropped;
///   - the Hips curves are scaled if the clip ends at a different height from Idle, and shifted so
///     the clip ends where Idle starts — no slide or pop when it blends into Idle.
/// Then adds a state for each to PlayerController, whose only way out is an exit-time transition
/// into Idle.
///
/// Idempotent: the clips are rewritten in place (same GUID) and the states are reused.
/// Nothing else in the controller is touched.
///
/// Runs by itself after a compile whenever PlayerController is missing either state, so the
/// stand-ups work without anyone having to find the menu item; the menu item re-runs it by hand.
/// </summary>
[InitializeOnLoad]
public static class PlayerStandUpSetup
{
    static PlayerStandUpSetup()
    {
        // delayCall: the AssetDatabase is not ready to load or write assets inside InitializeOnLoad.
        EditorApplication.delayCall += RunIfMissing;
    }

    private static void RunIfMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null || controller.layers.Length == 0) return;

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (StandUp standUp in StandUps)
        {
            if (FindState(machine, standUp.StateName) == null)
            {
                Debug.Log("[PlayerStandUpSetup] Stand-up states missing from PlayerController, setting them up.");
                Setup();
                return;
            }
        }
    }

    private const string AnimFolder = "Assets/_Project/Art/Animations/Animations_Mixamo/Player Animations/";
    private const string ControllerPath = AnimFolder + "PlayerController.controller";
    private const string IdleFbxPath = AnimFolder + "Idle.fbx";
    private const string PlayerPath = "Assets/_Project/Prefabs/Player.prefab";

    private const string IdleStateName = "Idle";
    private const string HipsName = "mixamorig:Hips";

    // Exit time into Idle, as a fraction of the clip, and the blend length in seconds. 1 = the blend
    // starts on the last frame, so the whole clip is seen and control comes back right as it ends.
    private const float ExitTime = 1f;
    private const float BlendSeconds = 0.2f;

    private static readonly Regex RigPrefix = new Regex(@"mixamorig\d+:");

    private struct StandUp
    {
        public string FbxName;
        public string StateName;
        public Vector3 StatePosition;
        public float Speed;
    }

    // State names must match PlayerStateManager.initStandUpState / captureStandUpState.
    // The capture one plays 30% faster: at 1x it drags right after the reveal.
    private static readonly StandUp[] StandUps =
    {
        new StandUp { FbxName = "Init Stand Up", StateName = "Init Stand Up", StatePosition = new Vector3(-20f, 520f, 0f), Speed = 1f },
        new StandUp { FbxName = "Standing Up",   StateName = "Standing Up",   StatePosition = new Vector3(240f, 520f, 0f), Speed = 1.3f },
    };

    [MenuItem("Tools/Player/Setup Stand-Up Animations")]
    public static void Setup()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[PlayerStandUpSetup] No AnimatorController at {ControllerPath}.");
            return;
        }

        Vector3 idleHips = ReadIdleHipsPosition();

        GameObject player = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            Animator animator = player != null ? player.GetComponentInChildren<Animator>(true) : null;
            Transform rigRoot = animator != null ? animator.transform : null;
            if (rigRoot == null)
                Debug.LogWarning("[PlayerStandUpSetup] Could not find the player's Animator; bone paths are not validated.");

            foreach (StandUp standUp in StandUps)
            {
                AnimationClip clip = BuildClip(standUp, idleHips, rigRoot);
                if (clip != null) AddState(controller, standUp, clip);
            }
        }
        finally
        {
            if (player != null) PrefabUtility.UnloadPrefabContents(player);
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[PlayerStandUpSetup] Done.");
    }

    // ── Clips ────────────────────────────────────────────────────────────────────────────

    private static AnimationClip BuildClip(StandUp standUp, Vector3 idleHips, Transform rigRoot)
    {
        string fbxPath = AnimFolder + standUp.FbxName + ".fbx";
        AnimationClip source = LoadFbxClip(fbxPath);
        if (source == null)
        {
            Debug.LogError($"[PlayerStandUpSetup] No animation clip in {fbxPath}.");
            return null;
        }

        AnimationClip clip = new AnimationClip { name = standUp.FbxName + " (Player Rig)", frameRate = source.frameRate };

        var hipsCurves = new Dictionary<string, AnimationCurve>();
        var unbound = new HashSet<string>();

        foreach (EditorCurveBinding sourceBinding in AnimationUtility.GetCurveBindings(source))
        {
            EditorCurveBinding binding = sourceBinding;
            binding.path = RigPrefix.Replace(binding.path, "mixamorig:");

            if (binding.type == typeof(Transform))
            {
                bool isHips = binding.path == HipsName || binding.path.EndsWith("/" + HipsName);

                // The root node would move the whole model, not a bone.
                if (string.IsNullOrEmpty(binding.path)) continue;
                if (binding.propertyName.StartsWith("m_LocalScale")) continue;
                if (binding.propertyName.StartsWith("m_LocalPosition") && !isHips) continue;

                // Checked through the Avatar and not with Transform.Find: the player's Animator binds
                // "mixamorig:Hips" through its Generic Avatar, while in the hierarchy the bone sits
                // under Armature_New — a plain path lookup reports every bone as missing.
                if (rigRoot != null && !BindsOnRig(rigRoot, binding.path)) unbound.Add(binding.path);

                if (isHips && binding.propertyName.StartsWith("m_LocalPosition"))
                {
                    hipsCurves[binding.propertyName] = AnimationUtility.GetEditorCurve(source, sourceBinding);
                    continue;
                }
            }

            AnimationUtility.SetEditorCurve(clip, binding, AnimationUtility.GetEditorCurve(source, sourceBinding));
        }

        WriteHipsCurves(clip, source, hipsCurves, idleHips, standUp.FbxName);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        if (unbound.Count > 0)
        {
            Debug.LogWarning($"[PlayerStandUpSetup] '{standUp.FbxName}': {unbound.Count} bone path(s) not found " +
                             $"on the player's rig, they will not animate. First: {First(unbound)}");
        }

        return SaveClip(clip, AnimFolder + clip.name + ".anim");
    }

    /// <summary>
    /// Writes the Hips position curves, scaled to Idle's height if the clip ends noticeably higher
    /// or lower, and shifted on X/Z so the last frame sits where Idle's first frame does.
    /// </summary>
    private static void WriteHipsCurves(AnimationClip clip, AnimationClip source,
                                        Dictionary<string, AnimationCurve> hipsCurves, Vector3 idleHips, string label)
    {
        hipsCurves.TryGetValue("m_LocalPosition.x", out AnimationCurve x);
        hipsCurves.TryGetValue("m_LocalPosition.y", out AnimationCurve y);
        hipsCurves.TryGetValue("m_LocalPosition.z", out AnimationCurve z);
        if (x == null && y == null && z == null) return;

        string hipsPath = FindHipsPath(source);
        float end = source.length;
        Vector3 endHips = new Vector3(x != null ? x.Evaluate(end) : 0f,
                                      y != null ? y.Evaluate(end) : 0f,
                                      z != null ? z.Evaluate(end) : 0f);

        float scale = 1f;
        if (!float.IsNaN(idleHips.y) && Mathf.Abs(endHips.y) > 0.0001f)
        {
            float ratio = idleHips.y / endHips.y;
            if (ratio > 0f && Mathf.Abs(ratio - 1f) > 0.1f)
            {
                scale = ratio;
                Debug.Log($"[PlayerStandUpSetup] '{label}': Hips scaled by {scale:F3} to match Idle's height.");
            }
        }

        Vector3 shift = Vector3.zero;
        if (!float.IsNaN(idleHips.x))
        {
            shift.x = idleHips.x - endHips.x * scale;
            shift.z = idleHips.z - endHips.z * scale;
        }

        SetHipsCurve(clip, hipsPath, "m_LocalPosition.x", x, scale, shift.x);
        SetHipsCurve(clip, hipsPath, "m_LocalPosition.y", y, scale, 0f);
        SetHipsCurve(clip, hipsPath, "m_LocalPosition.z", z, scale, shift.z);
    }

    private static void SetHipsCurve(AnimationClip clip, string path, string property, AnimationCurve curve,
                                     float scale, float offset)
    {
        if (curve == null) return;

        Keyframe[] keys = curve.keys;
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i].value = keys[i].value * scale + offset;
            keys[i].inTangent *= scale;
            keys[i].outTangent *= scale;
        }

        var result = new AnimationCurve(keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), result);
    }

    private static string FindHipsPath(AnimationClip source)
    {
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
        {
            string path = RigPrefix.Replace(binding.path, "mixamorig:");
            if (path == HipsName || path.EndsWith("/" + HipsName)) return path;
        }
        return HipsName;
    }

    /// <summary>Hips position on Idle's first frame. NaN components when Idle has no such curve.</summary>
    private static Vector3 ReadIdleHipsPosition()
    {
        Vector3 result = new Vector3(float.NaN, float.NaN, float.NaN);
        AnimationClip idle = LoadFbxClip(IdleFbxPath);
        if (idle == null) return result;

        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(idle))
        {
            string path = RigPrefix.Replace(binding.path, "mixamorig:");
            if (binding.type != typeof(Transform) || !(path == HipsName || path.EndsWith("/" + HipsName))) continue;

            float value = AnimationUtility.GetEditorCurve(idle, binding).Evaluate(0f);
            switch (binding.propertyName)
            {
                case "m_LocalPosition.x": result.x = value; break;
                case "m_LocalPosition.y": result.y = value; break;
                case "m_LocalPosition.z": result.z = value; break;
            }
        }
        return result;
    }

    private static AnimationClip LoadFbxClip(string fbxPath)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
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

    private static void AddState(AnimatorController controller, StandUp standUp, AnimationClip clip)
    {
        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimatorState idle = FindState(machine, IdleStateName);
        if (idle == null)
        {
            Debug.LogError($"[PlayerStandUpSetup] PlayerController has no '{IdleStateName}' state to return to.");
            return;
        }

        AnimatorState state = FindState(machine, standUp.StateName)
                              ?? machine.AddState(standUp.StateName, standUp.StatePosition);
        state.motion = clip;
        state.speed = standUp.Speed;
        state.writeDefaultValues = idle.writeDefaultValues;

        // Only an exit-time way out, so no parameter the FSM writes can cut the stand-up short.
        foreach (AnimatorStateTransition old in state.transitions) state.RemoveTransition(old);

        AnimatorStateTransition toIdle = state.AddTransition(idle);
        toIdle.hasExitTime = true;
        toIdle.exitTime = ExitTime;
        toIdle.hasFixedDuration = true;
        toIdle.duration = BlendSeconds;

        EditorUtility.SetDirty(state);
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string stateName)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state.name == stateName) return child.state;
        }
        return null;
    }

    /// <summary>
    /// True when a bone with the last name in <paramref name="path"/> exists anywhere under the rig.
    /// Loose on purpose: the Avatar resolves where it sits, this only catches a renamed rig.
    /// </summary>
    private static bool BindsOnRig(Transform rigRoot, string path)
    {
        string boneName = path.Substring(path.LastIndexOf('/') + 1);
        foreach (Transform bone in rigRoot.GetComponentsInChildren<Transform>(true))
        {
            if (bone.name == boneName) return true;
        }
        return false;
    }

    private static string First(HashSet<string> set)
    {
        foreach (string item in set) return item;
        return string.Empty;
    }
}
