using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Pulls the camera away from the player and widens the lens while sprinting, so the extra speed
/// reads on screen and not only in the run animation.
///
/// Drives <c>CinemachineOrbitalFollow.RadialAxis</c>, which Cinemachine applies as a plain
/// multiplier over the orbit spline. Scaling <c>Orbits</c> instead would look identical but
/// invalidates Cinemachine's spline cache, and rebuilding it allocates three arrays — every frame,
/// for an effect that changes every frame.
///
/// Place this on the same GameObject as the CinemachineOrbitalFollow (the player's camera rig).
/// </summary>
[RequireComponent(typeof(CinemachineOrbitalFollow))]
public class CameraSprintEffect : MonoBehaviour
{
    [Header("Pull back")]
    [Tooltip("Camera distance while sprinting, as a multiple of the normal orbit distance. " +
             "1 disables the pull back.")]
    [SerializeField, Min(1f)] private float _sprintDistance = 1.25f;

    [Header("Lens")]
    [Tooltip("Degrees added to the configured field of view while sprinting. 0 disables the kick.")]
    [SerializeField, Min(0f)] private float _sprintFovBoost = 6f;

    [Header("Response")]
    [Tooltip("Roughly how long the camera takes to settle into the sprint framing.")]
    [SerializeField, Min(0f)] private float _easeInTime = 0.3f;

    [Tooltip("Roughly how long the camera takes to settle back once sprint is released. Slower " +
             "than the ease in, otherwise stopping reads as a snap.")]
    [SerializeField, Min(0f)] private float _easeOutTime = 0.5f;

    private CinemachineOrbitalFollow _orbital;
    private CinemachineCamera _camera;
    private PlayerStateManager _player;

    // The FOV the rig is meant to sit at. Captured on the first LateUpdate and not in Start
    // because PlayerCameraController pushes SO_CameraConfig's FOV from its own Start, and the
    // order between two Start() calls is undefined. Every Start() of the frame has already run
    // by the first LateUpdate, so the value read here is the configured one.
    private float _baseFov;
    private bool _baseFovCaptured;

    private float _sprint01;
    private float _damperVelocity;

    private void Awake()
    {
        _orbital = GetComponent<CinemachineOrbitalFollow>();
        _camera  = GetComponent<CinemachineCamera>();
    }

    // The registry rather than GetComponentInParent: the rig happens to be parented under the
    // player today, but Cinemachine cameras are routinely pulled out of the character hierarchy
    // and this keeps working if that happens.
    private void OnEnable()  => PlayerRegistry.SubscribeAndCatchUp(HandlePlayerRegistered);
    private void OnDisable() => PlayerRegistry.Unsubscribe(HandlePlayerRegistered);

    private void HandlePlayerRegistered(PlayerStateManager player) => _player = player;

    private void LateUpdate()
    {
        if (!_baseFovCaptured)
        {
            _baseFov = _camera != null ? _camera.Lens.FieldOfView : 0f;
            _baseFovCaptured = true;
        }

        // SpeedMultiplier is the runtime truth for sprinting: PlayerMovingState raises it above 1
        // only while the sprint button is held, and crouching drives it below 1. Reading the state
        // instead of the button keeps this component out of the input layer, and the framing stays
        // put in the states that ignore sprint (crouch, hidden, interacting, disabled).
        bool sprinting = _player != null && _player.SpeedMultiplier > 1.01f;
        float target = sprinting ? 1f : 0f;

        // Scaled deltaTime on purpose: while paused the framing holds wherever it was.
        _sprint01 = Mathf.SmoothDamp(_sprint01, target, ref _damperVelocity,
                                     sprinting ? _easeInTime : _easeOutTime,
                                     Mathf.Infinity, Time.deltaTime);

        // SmoothDamp only ever approaches its target, so settle it by hand. Besides keeping the
        // rig from idling a hair away from its authored distance, this stops the writes below:
        // Cinemachine reads a changing axis value as user input, so a value that never quite
        // arrives would hold the axis in "being touched" forever and suppress auto-recentering
        // if the rig is ever configured to use it (all three axes have it off today).
        if (Mathf.Abs(target - _sprint01) < 0.001f)
        {
            _sprint01 = target;
            _damperVelocity = 0f;
        }

        ApplyFraming();
    }

    private void ApplyFraming()
    {
        // The radial axis clamps itself to its own Range, so a rig left at the default [1, 1]
        // would silently swallow the whole effect.
        _orbital.RadialAxis.Range.x = Mathf.Min(_orbital.RadialAxis.Range.x, 1f);
        _orbital.RadialAxis.Range.y = Mathf.Max(_orbital.RadialAxis.Range.y, _sprintDistance);
        _orbital.RadialAxis.Value   = Mathf.Lerp(1f, _sprintDistance, _sprint01);

        if (_camera != null)
            _camera.Lens.FieldOfView = _baseFov + _sprintFovBoost * _sprint01;
    }
}
