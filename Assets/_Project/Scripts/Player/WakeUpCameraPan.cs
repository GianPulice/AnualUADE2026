using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// The camera half of the wake-up cinematic. While the screen is black the rig waits low and to
/// the player's right, aimed near the floor. Once the eyes open it orbits right to left and rises
/// at the same time, ending behind the player looking at the nape — the end framing below —
/// exactly when ARC_01a ends (<see cref="WakeUpCinematicEvents"/>). Look input comes back from
/// that framing: nothing snaps back to the rig's authored axes.
///
/// It drives the rig's own OrbitalFollow axes and tracking pivot instead of blending from a second
/// camera, so the Deoccluder keeps working through the whole move and the last frame of the pan IS
/// the gameplay camera.
///
/// Look input is switched off by <see cref="PlayerCameraController"/> while
/// <see cref="WakeUpCinematicEvents.IsCameraLocked"/> is set, and that same controller asks
/// <see cref="TryGetPivotOffset"/> for the pivot height, since it owns the pivot (crouch dip).
///
/// SETUP: on the FreeLook Camera of the Player prefab (Tools ▸ Architect ▸ Setup Wake-Up Cinematic).
/// </summary>
[RequireComponent(typeof(CinemachineOrbitalFollow))]
public class WakeUpCameraPan : MonoBehaviour
{
    [Header("End framing (the nape) — input resumes here")]
    [Tooltip("Horizontal orbit angle at the end, in degrees, measured from straight behind the " +
             "player's body: 0 = right behind, negative = a little to its right. Relative to where " +
             "the player faces, so a Player rotated in the scene still ends looking at its nape.")]
    [SerializeField, Range(-180f, 180f)] private float endHorizontal = -3f;

    [Tooltip("Vertical orbit angle at the end, in degrees. Low values keep the camera near head " +
             "height, looking at the nape.")]
    [SerializeField, Range(-80f, 80f)] private float endVertical = 10f;

    [Header("Start")]
    [Tooltip("How far to the RIGHT of the end framing the pan starts, in degrees. It travels " +
             "leftwards back to it.")]
    [SerializeField, Range(0f, 180f)] private float startAngle = 110f;

    [Tooltip("Vertical orbit angle at the start. Lower = camera closer to the pivot's height. " +
             "Keep it inside SO_CameraConfig's Max Vertical Angle (39.1 today) or the rig clamps it.")]
    [SerializeField, Range(-80f, 80f)] private float startVertical = -35f;

    [Tooltip("Metres the aim pivot starts BELOW its standing height, so the shot begins near the " +
             "floor, where the player is lying. It rises back to 0 (the head) by the end.")]
    [SerializeField, Min(0f)] private float startPivotDrop = 1.3f;

    [Header("Timing")]
    [Tooltip("Shape of the orbit over the pan's duration.")]
    [SerializeField] private AnimationCurve orbitEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Shape of the rise (pivot and vertical angle) over the pan's duration.")]
    [SerializeField] private AnimationCurve riseEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Shoulder offset")]
    [Tooltip("The whole cinematic is framed from straight behind (no over-the-shoulder offset). " +
             "After it, the offset from SO_CameraConfig comes back over these seconds, starting " +
             "the first time the player moves or looks around. 0 = cut.")]
    [SerializeField, Min(0f)] private float shoulderBlendDuration = 1.2f;

    private enum Phase { Idle, Holding, Panning }
    private enum ShoulderState { Shoulder, Behind, WaitingForInput, Blending }

    private ShoulderState shoulderState = ShoulderState.Shoulder;
    private float shoulderWeight = 1f;

    /// <summary>
    /// How much of the over-the-shoulder offset applies right now: 0 = straight behind (cinematic),
    /// 1 = normal gameplay. <see cref="PlayerCameraController"/> multiplies the offset by it.
    /// </summary>
    public float ShoulderWeight => shoulderWeight;

    private CinemachineOrbitalFollow orbital;
    private Phase phase;
    private float panStartTime;
    private float panDuration;
    private float pivotOffset;

    private void Awake() => orbital = GetComponent<CinemachineOrbitalFollow>();

    private void OnEnable()
    {
        WakeUpCinematicEvents.OnPanStarted += HandlePanStarted;
        WakeUpCinematicEvents.OnFinished += HandleFinished;
    }

    private void OnDisable()
    {
        WakeUpCinematicEvents.OnPanStarted -= HandlePanStarted;
        WakeUpCinematicEvents.OnFinished -= HandleFinished;
    }

    /// <summary>
    /// While the cinematic owns the camera, the offset (metres, negative = lower) the pivot must sit
    /// at from its standing height. False the rest of the time.
    /// </summary>
    public bool TryGetPivotOffset(out float offset)
    {
        offset = pivotOffset;
        return phase != Phase.Idle;
    }

    // Update and not LateUpdate: CinemachineBrain reads the axes in its own LateUpdate.
    private void Update()
    {
        if (phase == Phase.Idle)
        {
            if (!WakeUpCinematicEvents.IsCameraLocked)
            {
                TickShoulderRelease();
                return;
            }
            phase = Phase.Holding;
            shoulderWeight = 0f;
            shoulderState = ShoulderState.Behind;
        }
        else if (!WakeUpCinematicEvents.IsCameraLocked)
        {
            // Finished while this component was disabled: never leave the rig mid-pan.
            HandleFinished();
            return;
        }

        float progress = 0f;
        if (phase == Phase.Panning)
        {
            // Unscaled, the same clock ArchitectVoiceController times ARC_01a with, so the pan and
            // the line end together.
            progress = panDuration > 0f ? Mathf.Clamp01((Time.unscaledTime - panStartTime) / panDuration) : 1f;
        }

        Apply(progress);
    }

    private void HandlePanStarted(float seconds)
    {
        phase = Phase.Panning;
        panStartTime = Time.unscaledTime;
        panDuration = seconds;
    }

    private void HandleFinished()
    {
        if (phase == Phase.Idle) return;
        Apply(1f);
        phase = Phase.Idle;
        pivotOffset = 0f;

        // Control comes back still framed from behind; the shoulder waits for the player.
        shoulderWeight = 0f;
        shoulderState = ShoulderState.WaitingForInput;
    }

    /// <summary>
    /// After the cinematic: holds the behind framing until the player first moves or looks around,
    /// then eases into the over-the-shoulder offset. Scaled time, so a pause holds it where it is.
    /// </summary>
    private void TickShoulderRelease()
    {
        switch (shoulderState)
        {
            case ShoulderState.WaitingForInput:
                if (PauseManager.IsGameplayInputBlocked || !PlayerGaveInput()) return;
                shoulderState = ShoulderState.Blending;
                break;

            case ShoulderState.Blending:
                shoulderWeight = shoulderBlendDuration > 0f
                    ? Mathf.MoveTowards(shoulderWeight, 1f, Time.deltaTime / shoulderBlendDuration)
                    : 1f;
                if (shoulderWeight >= 1f) shoulderState = ShoulderState.Shoulder;
                break;
        }
    }

    // Legacy axes, the same ones PlayerStateManager moves with; "Mouse X/Y" exist in the Input
    // Manager and the project runs both input backends.
    private static bool PlayerGaveInput() =>
        Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f ||
        Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.1f || Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.1f;

    /// <param name="progress">0 = start (right, low), 1 = end framing (the nape).</param>
    private void Apply(float progress)
    {
        float orbit = Mathf.Clamp01(orbitEase.Evaluate(progress));
        float rise = Mathf.Clamp01(riseEase.Evaluate(progress));

        // The rig binds in world space: horizontal 0 looks down world +Z, whatever the player faces.
        // Adding the body's yaw makes every angle here relative to the player instead — without it
        // a Player rotated -90 in the scene ended the pan looking at its side.
        // A lower value puts the camera further to the right. Unwrapped lerp, wrapped on write: the
        // pan goes the way startAngle says, not the shortest way round.
        float horizontal = PlayerYaw() + endHorizontal - startAngle * (1f - orbit);
        orbital.HorizontalAxis.Value = WrapToRange(horizontal, orbital.HorizontalAxis.Range);
        orbital.VerticalAxis.Value = Mathf.Lerp(startVertical, endVertical, rise);

        pivotOffset = -startPivotDrop * (1f - rise);
    }

    /// <summary>World yaw of the player's body (the model, which is what the camera frames).</summary>
    private static float PlayerYaw()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null) return 0f;
        Transform body = player.PlayerBody != null ? player.PlayerBody : player.transform;
        return body.eulerAngles.y;
    }

    private static float WrapToRange(float value, Vector2 range)
    {
        float span = range.y - range.x;
        return span > 0f ? range.x + Mathf.Repeat(value - range.x, span) : value;
    }
}
