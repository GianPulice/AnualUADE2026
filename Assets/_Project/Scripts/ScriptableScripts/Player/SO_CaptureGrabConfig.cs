using UnityEngine;

/// <summary>
/// Authoring data for the grab that plays when the Nemesis captures the player: where the two
/// bodies stand while E_KillPlayer and Grabbed play (<see cref="CaptureGrabStaging"/>) and how the
/// shot frames them (<see cref="CaptureGrabCamera"/>). One asset read by both, so the pair and the
/// camera on it cannot drift apart.
///
/// Pair Distance and Player Yaw Offset are not typed in: Tools > Player > Setup Capture Grab
/// measures them off the two clips and the Nemesis prefab. Run it again after changing the
/// Nemesis's scale, either clip, or Grip Standoff.
/// </summary>
[CreateAssetMenu(fileName = "SO_CaptureGrabConfig", menuName = "Scriptable Objects/Player/Capture Grab Config")]
public class SO_CaptureGrabConfig : ScriptableObject
{
    [Header("The pair (written by Tools > Player > Setup Capture Grab)")]
    [Tooltip("Metres between the Nemesis's pivot and the player's while the grab plays: how far " +
             "the Nemesis's wrists reach in the hold pose, at the scale its prefab gives the " +
             "model, plus Grip Standoff. The capture itself happens anywhere inside CatchMaxReach " +
             "(1 m), which is closer than the arms are long.")]
    [SerializeField, Min(0.1f)] private float pairDistance = 1.69f;

    [Tooltip("Degrees the player's body is turned away from facing the Nemesis head on, as the " +
             "Grabbed clip was animated. 0 = straight at it.")]
    [SerializeField, Range(-180f, 180f)] private float playerYawOffset = 0f;

    [Tooltip("Metres the Nemesis's wrists stay in front of the player's pivot: the hands close on " +
             "the shoulders, not on the spine. The clips were animated with 0.27. Setup adds it " +
             "to the measured reach to get Pair Distance.")]
    [SerializeField, Min(0f)] private float gripStandoff = 0.27f;

    [Tooltip("Seconds the two bodies take to reach the pair's distance and facing once the grab " +
             "starts. Keep it under 0.6: that is when the hands close in E_KillPlayer.")]
    [SerializeField, Min(0f)] private float alignSeconds = 0.35f;

    [Header("Clips")]
    [Tooltip("Animator state of the player's half of the grab. Added to PlayerController by " +
             "Tools > Player > Setup Capture Grab.")]
    [SerializeField] private string grabbedState = "Grabbed";

    [Tooltip("Seconds of crossfade into Grabbed. Has to match the Any State -> Catch transition " +
             "of NemesisController (0.15), or the two clips start out of step.")]
    [SerializeField, Min(0f)] private float clipBlendSeconds = 0.15f;

    [Header("Shot")]
    [Tooltip("Seconds the grab stays on screen before the capture's black cover starts and the " +
             "respawn countdown begins. E_KillPlayer lasts 1.875 s (45 frames at 24 fps) and the " +
             "cover takes 0.6 s to close, so 1.275 goes black on the clip's last frame.")]
    [SerializeField, Min(0f)] private float shotSeconds = 1.275f;

    [Tooltip("Seconds the camera takes to travel from the gameplay framing to the shot. With " +
             "something in the way of that travel it cuts instead.")]
    [SerializeField, Min(0f)] private float cameraMoveSeconds = 0.7f;

    [Tooltip("Metres from the point between the two bodies to the camera. The most it goes: a " +
             "wall brings it in, and the lens opens to keep the pair the same size.")]
    [SerializeField, Min(0.5f)] private float cameraDistance = 3.4f;

    [Tooltip("Metres above the floor of the point the camera looks at. The hands hold at 1.65.")]
    [SerializeField, Min(0f)] private float focusHeight = 1.45f;

    [Tooltip("Metres the camera sits above (or below) the point it looks at.")]
    [SerializeField] private float cameraHeight = 0.2f;

    [Tooltip("Degrees round the pair, from behind the player's back: 90 is a side-on profile of " +
             "both, lower shows more of the Nemesis's face, higher more of the player's. This is " +
             "the angle it tries first; the shot that is used is the one that SEES the grab " +
             "(see CaptureGrabCamera), on whichever side the gameplay camera was already on.")]
    [SerializeField, Range(30f, 135f)] private float cameraYaw = 90f;

    [Tooltip("Field of view of the shot. 0 = whatever the gameplay camera had at the grab, which " +
             "is the sprint's widened one more often than not.")]
    [SerializeField, Range(0f, 120f)] private float fieldOfView = 50f;

    [Tooltip("Fog preset held while the shot is on screen. The area's own fog is measured from " +
             "the player and can start 2 m out, which leaves the Nemesis's arms in it. Empty = " +
             "the area's fog stays.")]
    [SerializeField] private SO_VisionFogConfig fogPreset;

    [Header("What the shot has to show (written by Tools > Player > Setup Capture Grab)")]
    [Tooltip("Metres above the floor of the Nemesis's wrists while it holds the player.")]
    [SerializeField, Min(0f)] private float gripHeight = 1.65f;

    [Tooltip("Metres from the pair's line to each wrist.")]
    [SerializeField, Min(0f)] private float gripHalfWidth = 0.31f;

    [Tooltip("Metres above the floor of the player's head while held (the clip lifts the body).")]
    [SerializeField, Min(0f)] private float playerHeadHeight = 1.85f;

    [Tooltip("Metres above the floor of the Nemesis's head while it holds the player.")]
    [SerializeField, Min(0f)] private float nemesisHeadHeight = 2.1f;

    [Tooltip("Metres the Nemesis's head leans forward of its pivot while it holds the player.")]
    [SerializeField, Min(0f)] private float nemesisHeadForward = 1f;

    public float PairDistance => pairDistance;
    public float PlayerYawOffset => playerYawOffset;
    public float GripStandoff => gripStandoff;
    public float AlignSeconds => alignSeconds;

    /// <summary>How far in front of its pivot the Nemesis's wrists hold, in metres.</summary>
    public float GripReach => Mathf.Max(0f, pairDistance - gripStandoff);

    public string GrabbedState => grabbedState;
    public float ClipBlendSeconds => clipBlendSeconds;

    public float ShotSeconds => shotSeconds;
    public float CameraMoveSeconds => cameraMoveSeconds;
    public float CameraDistance => cameraDistance;
    public float FocusHeight => focusHeight;
    public float CameraHeight => cameraHeight;
    public float CameraYaw => cameraYaw;
    public float FieldOfView => fieldOfView;
    public SO_VisionFogConfig FogPreset => fogPreset;

    public float GripHeight => gripHeight;
    public float GripHalfWidth => gripHalfWidth;
    public float PlayerHeadHeight => playerHeadHeight;
    public float NemesisHeadHeight => nemesisHeadHeight;
    public float NemesisHeadForward => nemesisHeadForward;

#if UNITY_EDITOR
    /// <summary>Editor only: what Setup Capture Grab measures off the two clips.</summary>
    public void SetMeasuredPair(float distance, float yawOffset, float wristHeight, float wristHalfWidth,
                                float heldHeadHeight, float monsterHeadHeight, float monsterHeadForward)
    {
        pairDistance = distance;
        playerYawOffset = yawOffset;
        gripHeight = wristHeight;
        gripHalfWidth = wristHalfWidth;
        playerHeadHeight = heldHeadHeight;
        nemesisHeadHeight = monsterHeadHeight;
        nemesisHeadForward = monsterHeadForward;
    }

    /// <summary>Editor only: the shot's fog, filled in by Setup Capture Grab when it is empty.</summary>
    public void SetFogPresetIfEmpty(SO_VisionFogConfig preset)
    {
        if (fogPreset == null) fogPreset = preset;
    }
#endif
}
