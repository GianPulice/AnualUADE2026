using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// The fuse box shutting itself once its fuse is in: the lever lifts, then the door swings closed.
/// One job — those two moves. The sound, the lamps and the puzzle are not its business.
///
/// Both parts are authored in the prefab in their BEFORE pose (door open, lever down); each one turns
/// from there by an angle around an axis, so nothing about the AFTER pose is stored twice. The axes are
/// in the part's parent space, the box root: for the lever Z (through the side wall it hangs on), for
/// the door Y (its hinge — the door's pivot already sits on the hinge edge).
///
/// Put it on the socket's root, next to <see cref="SocketInteractable"/>. It listens to
/// <see cref="SocketInteractable.Inserted"/> (animate) and
/// <see cref="SocketInteractable.InsertedStateSynced"/> (snap, after a load or checkpoint). The socket
/// only takes the item it asks for, so an insert is always the right fuse.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SocketInteractable))]
public class FuseBoxCloseSequence : MonoBehaviour
{
    [Header("Lever")]
    [Tooltip("The lever on the side of the box (Cube.004). Its pose in the prefab is the one before the fuse goes in.")]
    [SerializeField] private Transform lever;

    [Tooltip("Axis the lever turns around, in the box root's space.")]
    [SerializeField] private Vector3 leverAxis = Vector3.forward;

    [Tooltip("Degrees it turns from its authored pose. With the axis above, negative lifts the tip.")]
    [SerializeField] private float leverAngle = -45f;

    [Tooltip("Seconds the lever takes to lift.")]
    [SerializeField, Min(0f)] private float leverSeconds = 0.45f;

    [Header("Door")]
    [Tooltip("The door (Puerta). Its pose in the prefab is the one before the fuse goes in: open.")]
    [SerializeField] private Transform door;

    [Tooltip("Axis the door turns around, in the box root's space: the hinge.")]
    [SerializeField] private Vector3 doorAxis = Vector3.up;

    [Tooltip("Degrees it turns from its authored pose to shut. It is open at 127.15 degrees in the model.")]
    [SerializeField] private float doorAngle = -127.15f;

    [Tooltip("Seconds the door takes to close.")]
    [SerializeField, Min(0f)] private float doorSeconds = 0.8f;

    [Header("Timing")]
    [Tooltip("Seconds between the fuse going in and the lever starting to lift.")]
    [SerializeField, Min(0f)] private float startDelay = 0.35f;

    [Tooltip("Seconds between the lever landing and the door starting to close.")]
    [SerializeField, Min(0f)] private float doorDelay = 0.2f;

    private SocketInteractable _socket;
    private Quaternion _leverRest;
    private Quaternion _doorRest;

    // The run in flight, or null. Whoever started a run clears it when that run ends.
    private CancellationTokenSource _run;

    private void Awake()
    {
        _socket = GetComponent<SocketInteractable>();

        if (lever != null) _leverRest = lever.localRotation;
        if (door != null) _doorRest = door.localRotation;
    }

    private void OnEnable()
    {
        _socket.Inserted            += HandleInserted;
        _socket.InsertedStateSynced += HandleStateSynced;
    }

    private void OnDisable()
    {
        _socket.Inserted            -= HandleInserted;
        _socket.InsertedStateSynced -= HandleStateSynced;
    }

    private void OnDestroy() => CancelRun();

    private void HandleInserted()
    {
        CancelRun();

        _run = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
        CloseAsync(_run).Forget();
    }

    private void HandleStateSynced(bool inserted)
    {
        // Inserted during the sync delay: the animation owns the pose.
        if (_run != null) return;

        Pose(inserted ? 1f : 0f, inserted ? 1f : 0f);
    }

    private async UniTaskVoid CloseAsync(CancellationTokenSource run)
    {
        CancellationToken token = run.Token;

        try
        {
            await WaitAsync(startDelay, token);
            await EaseAsync(leverSeconds, PoseLever, token);
            await WaitAsync(doorDelay, token);
            await EaseAsync(doorSeconds, PoseDoor, token);
        }
        finally
        {
            if (_run == run) _run = null;
            run.Dispose();
        }
    }

    private static UniTask WaitAsync(float seconds, CancellationToken token) =>
        seconds > 0f
            ? UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: token)
            : UniTask.CompletedTask;

    /// <summary>Feeds <paramref name="pose"/> a smoothstep from 0 (authored pose) to 1 (turned all the
    /// way) over <paramref name="seconds"/>, and ends on exactly 1.</summary>
    private static async UniTask EaseAsync(float seconds, Action<float> pose, CancellationToken token)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, token);

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / seconds);
            pose(t * t * (3f - 2f * t));
        }

        pose(1f);
    }

    /// <summary>Puts both parts at once: 0 = authored pose, 1 = turned all the way.</summary>
    private void Pose(float leverT, float doorT)
    {
        PoseLever(leverT);
        PoseDoor(doorT);
    }

    private void PoseLever(float t) => Turn(lever, _leverRest, leverAxis, leverAngle * t);

    private void PoseDoor(float t) => Turn(door, _doorRest, doorAxis, doorAngle * t);

    private static void Turn(Transform part, Quaternion rest, Vector3 axis, float degrees)
    {
        if (part == null || axis.sqrMagnitude < 1e-6f) return;

        // The turn is in the parent's space, so it goes in front of the authored rotation.
        part.localRotation = Quaternion.AngleAxis(degrees, axis.normalized) * rest;
    }

    private void CancelRun()
    {
        if (_run == null) return;

        _run.Cancel();
        _run = null;
    }
}
