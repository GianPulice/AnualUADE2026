using UnityEngine;

/// <summary>
/// The arm reach the player plays when they take something or use a door (<see cref="PlayerGrabGesture"/>).
///
/// The reach is IK (<see cref="ArmReachIK"/>) that FOLLOWS THE OLD CLIP'S OWN MOTION: the hand's path
/// and the elbow's, measured off the "Grabbing" clip on the real rig and stored here
/// (<see cref="ReachHandPath"/>, <see cref="ReachElbowBend"/>), so the arm lifts, swings out and comes
/// back in the same arc the animation drew. On top of that path the hand is pulled toward the spot
/// that was grabbed, by how far into the reach the arm is (<see cref="ReachProfile"/>): at the peak it
/// is on the object, anywhere else it is still on the clip's arc. The arm nearest the spot is used,
/// and the path is mirrored for the right one.
///
/// The arrays are not typed in: Tools > Player > Bake Grab Reach From Clip writes them (and the
/// profile) from <see cref="ReachSourceClip"/> on the Player prefab, and can be run again whenever the
/// clip is edited. With them empty the reach still works, as a plain straight-line IK.
///
/// If the player's bones cannot be found, or <see cref="ReachToObject"/> is off, the old clip plays
/// instead, on its own Animator layer, its weight faded in and out here, which is what keeps it from
/// snapping on and off.
/// </summary>
[CreateAssetMenu(fileName = "SO_GrabGestureConfig", menuName = "Scriptable Objects/Player/Grab Gesture Config")]
public class SO_GrabGestureConfig : ScriptableObject
{
    [Header("Reach (arm IK)")]
    [Tooltip("The arm goes out to the spot that was grabbed. Off = always the fixed left-arm clip below.")]
    [SerializeField] private bool reachToObject = true;

    [Tooltip("Seconds the whole reach lasts, from the arm leaving its pose to being back in it. The " +
             "old clip's motion lasts 0.76 s (the rest of its 0.81 s is the arm standing still).")]
    [SerializeField, Min(0.1f)] private float reachSeconds = 0.76f;

    [Tooltip("Seconds at each end of the reach over which the arm blends between the walking pose and " +
             "the clip's path, so it neither snaps out nor snaps back.")]
    [SerializeField, Min(0f)] private float reachBlendSeconds = 0.12f;

    [Header("Reach path (baked from the clip)")]
    [Tooltip("The clip whose motion is copied. Tools > Player > Bake Grab Reach From Clip reads it. It " +
             "animates the LEFT arm; the right one is its mirror image.")]
    [SerializeField] private AnimationClip reachSourceClip;

    [Tooltip("Where the hand is over the reach, from the shoulder, in the body's frame (x right, y up, " +
             "z forward) as a share of the arm's length. Left arm; evenly spaced over 'Reach Seconds'. " +
             "Written by the bake tool: do not type it.")]
    [SerializeField] private Vector3[] reachHandPath = new Vector3[0];

    [Tooltip("Where the elbow sits off the shoulder-to-hand line over the reach, same frame and units " +
             "as the hand path, so the arm bends the way the clip's does. Written by the bake tool.")]
    [SerializeField] private Vector3[] reachElbowBend = new Vector3[0];

    [Tooltip("How much the hand is pulled from the clip's path toward the spot that was grabbed, over " +
             "the reach's time (0 to 1): 0 = on the clip's own path, 1 = on the spot. It is the clip's own " +
             "motion measured on the rig (how far the arm had turned toward its peak): a small lift and " +
             "dip first, the main reach, a short hold at the top and a quick return. The bake tool " +
             "rewrites it.")]
    [SerializeField] private AnimationCurve reachProfile = DefaultReachProfile();

    [Header("Reach limits")]
    [Tooltip("How far the arm may stretch, as a share of its full length. An object farther than that " +
             "is reached toward, with the arm stretched, not touched. The old clip never went past " +
             "0.85 (elbow bent about 64 degrees at the peak): a straight arm looks stiff.")]
    [SerializeField, Range(0.5f, 1f)] private float maxReachFraction = 0.88f;

    [Tooltip("Metres. The nearest arm to the spot is used, but the right one wins unless the left is " +
             "closer by more than this: an object straight ahead is taken with the right hand.")]
    [SerializeField, Min(0f)] private float rightArmBias = 0.12f;

    [Tooltip("Degrees, to either side of where the body faces. The arm NEVER points further round than " +
             "this: the spot is clamped to it, so the arm cannot go behind the player. Past it (walking " +
             "through a door, the object ending up beside or behind the body) the reach fades out " +
             "over 'Let Go Degrees' and the arm goes back to the walking pose.")]
    [SerializeField, Range(30f, 85f)] private float maxAngleFromBody = 70f;

    [Tooltip("Degrees beyond 'Max Angle From Body' over which the reach fades to nothing.")]
    [SerializeField, Min(1f)] private float letGoDegrees = 25f;

    [Tooltip("The elbow's direction when the clip's own bend is too small to tell (the arm hanging at " +
             "the start and end of the reach): how much it drops. 2 : 0.5 is where the clip had the " +
             "elbow at its peak.")]
    [SerializeField, Min(0f)] private float elbowDown = 2f;

    [Tooltip("The same, how much the elbow flares away from the body.")]
    [SerializeField, Min(0f)] private float elbowOut = 0.5f;

    [Tooltip("Prefix of the rig's bone names. The arm bones are looked up as <prefix>RightArm, " +
             "<prefix>RightForeArm and <prefix>RightHand, and the same for the left.")]
    [SerializeField] private string bonePrefix = "mixamorig:";

    [Header("Clip fallback")]
    [Tooltip("Animator layer of PlayerController that holds the gesture (masked to the left arm).")]
    [SerializeField] private string layerName = "Grab Arm";

    [Tooltip("State on that layer that plays the reach.")]
    [SerializeField] private string stateName = "Grabbing";

    [Tooltip("Seconds the layer takes to come in. Longer = a softer start.")]
    [SerializeField, Min(0f)] private float fadeInSeconds = 0.18f;

    [Tooltip("Seconds the layer takes to hand the arm back, ending with the clip.")]
    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.15f;

    [Tooltip("Playback speed of the state. Above 1 shortens the whole gesture.")]
    [SerializeField, Min(0.1f)] private float speed = 1f;

    public bool ReachToObject => reachToObject;
    public float ReachSeconds => reachSeconds;
    public float ReachBlendSeconds => reachBlendSeconds;
    public AnimationClip ReachSourceClip => reachSourceClip;
    public Vector3[] ReachHandPath => reachHandPath;
    public Vector3[] ReachElbowBend => reachElbowBend;
    public AnimationCurve ReachProfile => reachProfile;
    public float MaxReachFraction => maxReachFraction;
    public float RightArmBias => rightArmBias;
    public float MaxAngleFromBody => maxAngleFromBody;
    public float LetGoDegrees => letGoDegrees;
    public float ElbowDown => elbowDown;
    public float ElbowOut => elbowOut;
    public string BonePrefix => bonePrefix;

    public string LayerName => layerName;
    public string StateName => stateName;
    public float FadeInSeconds => fadeInSeconds;
    public float FadeOutSeconds => fadeOutSeconds;
    public float Speed => speed;

    /// <summary>
    /// The old "Grabbing" clip's motion, as how far its arm had turned from where it started toward
    /// where it peaked, sampled every 0.054 s on the real rig over the first 0.756 s of its 0.81 s (the
    /// last 0.05 s the arm stands still) and spread evenly over 0 to 1. The peak is the 11th sample,
    /// at 0.54 s. Only the default: the bake tool replaces it with a finer one.
    /// </summary>
    private static AnimationCurve DefaultReachProfile()
    {
        float[] samples =
        {
            0.000f, 0.040f, 0.120f, 0.207f, 0.164f, 0.290f, 0.571f, 0.794f,
            0.900f, 0.947f, 1.000f, 0.944f, 0.556f, 0.043f, 0.000f,
        };

        var curve = new AnimationCurve();
        for (int i = 0; i < samples.Length; i++)
            curve.AddKey(new Keyframe(i / (float)(samples.Length - 1), samples[i]));
        for (int i = 0; i < curve.length; i++)
            curve.SmoothTangents(i, 0f);
        return curve;
    }
}
