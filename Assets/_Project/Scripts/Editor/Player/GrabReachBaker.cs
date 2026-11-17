#if UNITY_EDITOR
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Copies the motion of the grab clip into <see cref="SO_GrabGestureConfig"/>, so the IK reach
/// (<see cref="PlayerGrabGesture"/>) moves the way the animation does: the hand's path and the
/// elbow's, not a straight line to the object.
///
/// For each moment of the reach it poses the Player prefab with the clip (its LEFT arm: the clip
/// animates it, and the right arm mirrors it at runtime) and reads, in the body's frame
/// (<see cref="ArmReachIK.BodyAxes"/>, the same one the player uses), as a share of the arm's length:
///   • the hand's offset from the shoulder, and
///   • how far the elbow sits off the shoulder-to-hand line.
/// It also writes the reach profile: how far the arm has turned from where it started toward where it
/// peaks, which says how much of the pull toward the object applies at each moment.
///
/// The prefab is loaded in isolation (<see cref="PrefabUtility.LoadPrefabContents"/>), so the open
/// scene is never touched. Re-runnable: run it again whenever the clip is edited or re-timed.
/// </summary>
public static class GrabReachBaker
{
    private const string ConfigPath = "Assets/_Project/ScriptableObjects/Player/SO_GrabGestureConfig.asset";
    private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
    private const string DefaultClipPath =
        "Assets/_Project/Art/Animations/Animations_Mixamo/Player Animations/Grabbing (Player Rig).anim";
    private const string LogTag = "[GrabReachBaker]";

    // About this many samples per second of reach: a few frames apart, smooth between them.
    private const float SamplesPerSecond = 36f;

    [MenuItem("Tools/Player/Bake Grab Reach From Clip")]
    private static void Bake()
    {
        var config = AssetDatabase.LoadAssetAtPath<SO_GrabGestureConfig>(ConfigPath);
        if (config == null)
        {
            Debug.LogError($"{LogTag} No SO_GrabGestureConfig at {ConfigPath}.");
            return;
        }

        var so = new SerializedObject(config);
        SerializedProperty clipProp = so.FindProperty("reachSourceClip");
        var clip = clipProp.objectReferenceValue as AnimationClip
                   ?? AssetDatabase.LoadAssetAtPath<AnimationClip>(DefaultClipPath);
        if (clip == null)
        {
            Debug.LogError($"{LogTag} No source clip: assign Reach Source Clip or restore {DefaultClipPath}.");
            return;
        }

        float seconds = so.FindProperty("reachSeconds").floatValue;
        string prefix = so.FindProperty("bonePrefix").stringValue ?? string.Empty;

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Animator animator = root.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.avatar != null);
            if (animator == null)
            {
                Debug.LogError($"{LogTag} No Animator with an avatar in {PlayerPrefabPath}.");
                return;
            }

            Transform upper = FindDeep(animator.transform, prefix + "LeftArm");
            Transform fore = FindDeep(animator.transform, prefix + "LeftForeArm");
            Transform hand = FindDeep(animator.transform, prefix + "LeftHand");
            if (upper == null || fore == null || hand == null)
            {
                Debug.LogError($"{LogTag} Left arm bones '{prefix}LeftArm/ForeArm/Hand' not found under '{animator.name}'.");
                return;
            }

            int count = Mathf.Max(8, Mathf.CeilToInt(seconds * SamplesPerSecond)) + 1;
            var handPath = new Vector3[count];
            var bendPath = new Vector3[count];
            var angles = new float[count];

            Vector3 firstDirection = Vector3.zero;
            float armLength = 0f;
            float maxAngle = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = Mathf.Min(seconds * i / (count - 1), clip.length);
                clip.SampleAnimation(animator.gameObject, t);

                ArmReachIK.BodyAxes(animator.transform, out Vector3 right, out Vector3 up, out Vector3 forward);
                Vector3 shoulder = upper.position;
                Vector3 elbow = fore.position;
                Vector3 wrist = hand.position;

                if (i == 0) armLength = (elbow - shoulder).magnitude + (wrist - elbow).magnitude;

                Vector3 toHand = wrist - shoulder;
                Vector3 direction = toHand.normalized;
                if (i == 0) firstDirection = direction;

                Vector3 bend = (elbow - shoulder) - direction * Vector3.Dot(elbow - shoulder, direction);

                handPath[i] = InFrame(toHand, right, up, forward) / armLength;
                bendPath[i] = InFrame(bend, right, up, forward) / armLength;
                angles[i] = Vector3.Angle(firstDirection, direction);
                maxAngle = Mathf.Max(maxAngle, angles[i]);
            }

            // How far into the reach the arm is, from where it started toward where it peaks.
            var profile = new AnimationCurve();
            int peak = 0;
            for (int i = 0; i < count; i++)
            {
                float w = maxAngle > 1e-3f ? angles[i] / maxAngle : 0f;
                profile.AddKey(new Keyframe(i / (float)(count - 1), w));
                if (angles[i] > angles[peak]) peak = i;
            }
            for (int i = 0; i < profile.length; i++) profile.SmoothTangents(i, 0f);

            Write(so, "reachHandPath", handPath);
            Write(so, "reachElbowBend", bendPath);
            so.FindProperty("reachProfile").animationCurveValue = profile;
            clipProp.objectReferenceValue = clip;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            var log = new StringBuilder();
            log.AppendLine($"{LogTag} Baked '{clip.name}' ({clip.length:F2} s) over {seconds:F2} s into {count} samples.");
            log.AppendLine($"  arm length {armLength:F3} m; the arm turns {maxAngle:F1} degrees from its start to its peak.");
            log.AppendLine($"  peak at {seconds * peak / (count - 1):F2} s ({peak / (float)(count - 1):P0} of the reach): " +
                           $"hand {handPath[peak] * armLength} m from the shoulder (right, up, forward), " +
                           $"{handPath[peak].magnitude:P0} of the arm, elbow {bendPath[peak].magnitude * armLength * 100f:F0} cm off the line.");
            log.AppendLine($"  start hand {handPath[0]}, end hand {handPath[count - 1]} (shares of the arm).");
            Debug.Log(log.ToString());
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Vector3 InFrame(Vector3 v, Vector3 right, Vector3 up, Vector3 forward) =>
        new Vector3(Vector3.Dot(v, right), Vector3.Dot(v, up), Vector3.Dot(v, forward));

    private static void Write(SerializedObject so, string field, Vector3[] values)
    {
        SerializedProperty array = so.FindProperty(field);
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            array.GetArrayElementAtIndex(i).vector3Value = values[i];
    }

    private static Transform FindDeep(Transform parent, string boneName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == boneName) return child;

            Transform deeper = FindDeep(child, boneName);
            if (deeper != null) return deeper;
        }
        return null;
    }
}
#endif
