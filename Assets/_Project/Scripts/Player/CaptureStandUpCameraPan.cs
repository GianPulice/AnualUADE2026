using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Camera shot for the checkpoint stand-up after the Nemesis caught the player. While the player
/// lies at the checkpoint (behind the capture fade) the rig moves in front of it, low, facing it.
/// As the stand-up clip plays, it orbits round to the player's back and rises to the head, landing
/// on the default framing on the clip's last frame — the same frame control comes back.
///
/// Synced to <see cref="PlayerStateManager.StandUpProgress"/> (the clip's own normalized time),
/// not to a timer: a longer clip, a faster state or a pause all keep the shot in step.
///
/// Look input is already off for the whole stand-up (<see cref="PlayerCameraController"/>), and
/// that controller asks <see cref="TryGetPivotOffset"/> and <see cref="ShoulderWeight"/> while it
/// runs, same as for <see cref="WakeUpCameraPan"/>.
///
/// SETUP: on the FreeLook Camera of the Player prefab, next to WakeUpCameraPan
/// (Tools ▸ Architect ▸ Setup Wake-Up Cinematic adds it).
/// </summary>
[RequireComponent(typeof(CinemachineOrbitalFollow))]
public class CaptureStandUpCameraPan : MonoBehaviour
{
    [Header("Start (in front, low)")]
    [Tooltip("Horizontal angle at the start, from straight behind the player's body. 180 = right in " +
             "front, facing it.")]
    [SerializeField, Range(0f, 180f)] private float startAngle = 180f;

    [Tooltip("Which way round it orbits from the front to the back. On = passes by the player's " +
             "right side, off = by its left.")]
    [SerializeField] private bool orbitByRightSide = true;

    [Tooltip("Vertical orbit angle at the start. Keep it inside SO_CameraConfig's Max Vertical Angle.")]
    [SerializeField, Range(-80f, 80f)] private float startVertical = -10f;

    [Tooltip("Metres the aim pivot starts below its standing height: the player is on the floor.")]
    [SerializeField, Min(0f)] private float startPivotDrop = 1.1f;

    [Header("End (behind, where control comes back)")]
    [Tooltip("Horizontal angle at the end, from straight behind the player's body.")]
    [SerializeField, Range(-180f, 180f)] private float endHorizontal = 0f;

    [SerializeField, Range(-80f, 80f)] private float endVertical = 10f;

    [Header("Timing (over the stand-up clip, 0 = first frame, 1 = last)")]
    [Tooltip("Orbit from the front to the back. The default holds the front view for the first " +
             "part of the clip, so the player is seen getting up, then goes round.")]
    [SerializeField] private AnimationCurve orbitEase = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.35f, 0f), new Keyframe(1f, 1f));

    [Tooltip("Rise of the pivot and the vertical angle, from the floor to the head.")]
    [SerializeField] private AnimationCurve riseEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Shoulder offset")]
    [Tooltip("Seconds to ease the over-the-shoulder offset back in once control returns. The shot " +
             "itself is framed from straight behind.")]
    [SerializeField, Min(0f)] private float shoulderBlendDuration = 0.8f;

    private CinemachineOrbitalFollow orbital;
    private bool active;
    private float pivotOffset;
    private float shoulderWeight = 1f;

    /// <summary>0 = straight behind (during the shot), 1 = normal over-the-shoulder offset.</summary>
    public float ShoulderWeight => shoulderWeight;

    /// <summary>While the shot runs, the offset (metres, negative = lower) of the aim pivot.</summary>
    public bool TryGetPivotOffset(out float offset)
    {
        offset = pivotOffset;
        return active;
    }

    private void Awake() => orbital = GetComponent<CinemachineOrbitalFollow>();

    // Update, before CinemachineBrain reads the axes in its LateUpdate.
    private void Update()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        bool standingUp = player != null && player.IsCaptureStandingUp;

        if (standingUp)
        {
            active = true;
            shoulderWeight = 0f;
            Apply(player.StandUpProgress);
            return;
        }

        if (active)
        {
            // The clip ended (or was cut): land exactly on the end framing.
            Apply(1f);
            active = false;
            pivotOffset = 0f;
        }

        if (shoulderWeight < 1f)
        {
            // Scaled: a pause holds it where it is.
            shoulderWeight = shoulderBlendDuration > 0f
                ? Mathf.MoveTowards(shoulderWeight, 1f, Time.deltaTime / shoulderBlendDuration)
                : 1f;
        }
    }

    private void Apply(float progress)
    {
        float orbit = Mathf.Clamp01(orbitEase.Evaluate(progress));
        float rise = Mathf.Clamp01(riseEase.Evaluate(progress));

        // Relative to the player's body, like WakeUpCameraPan. A lower horizontal value is further
        // to the player's right, so going round by the right means starting below the end angle.
        float sign = orbitByRightSide ? -1f : 1f;
        float horizontal = WakeUpCameraPan.PlayerYaw() + endHorizontal + sign * startAngle * (1f - orbit);

        orbital.HorizontalAxis.Value = WakeUpCameraPan.WrapToRange(horizontal, orbital.HorizontalAxis.Range);
        orbital.VerticalAxis.Value = Mathf.Lerp(startVertical, endVertical, rise);
        pivotOffset = -startPivotDrop * (1f - rise);
    }
}
