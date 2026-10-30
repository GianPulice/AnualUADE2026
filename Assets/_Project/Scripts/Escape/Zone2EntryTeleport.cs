using System;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Zona 2's way in: puts the player on <see cref="target"/>, standing, facing its forward, with
/// their camera behind them and control in their hands. Its one job. Two callers, one path
/// (<see cref="TeleportNow"/>):
///
///   The escape's end   while this is enabled with <see cref="continueAfterEscape"/> on, the escape's
///                      last shot (the gate slamming in the Nemesis's face) no longer ends in the
///                      win screen: <see cref="EscapeSequenceDirector"/> gives back everything the
///                      ending held (the shot, the fog, the player, the module clock, the sirens)
///                      and calls this instead. Disable the component, or switch that box off, and
///                      the win screen comes back exactly as before.
///   F6                 the <c>DevLevelKeys</c> debug key, any time in Play (editor and dev builds).
///
/// What it does NOT do, on purpose: move the respawn point (a capture in Zona 2 sends the player to
/// the active <see cref="Checkpoint"/> — put one with a trigger collider around the target and the
/// arrival activates it like any other; without it the respawn is the last Zona 1 checkpoint, with
/// the puzzles rolled back to its snapshot), wake or move the Nemesis (the ending leaves it parked
/// against the gate in Zona 1, its FSM off), open or lock anything, or fade the screen (the escape
/// cuts hard everywhere, and so does this).
///
/// Refuses, with a warning, while the player is inside a hiding spot or being captured: those own
/// the body and the camera, and a teleport under them leaves both in a state nothing undoes.
///
/// SETUP: on <c>Zona_2_Spawnpoint</c> (target empty = this transform), its blue arrow pointing where
/// the player should face on arrival, standing on the floor of Zona 2.
/// </summary>
[DisallowMultipleComponent]
public class Zone2EntryTeleport : MonoBehaviour
{
    [Tooltip("Where the player appears, facing this transform's Z axis (the blue arrow). Only the " +
             "horizontal turn counts. Empty = this object.")]
    [SerializeField] private Transform target;

    [Tooltip("On: when the escape's final cinematic ends (the gate slamming down) the player is " +
             "brought here instead of the win screen. Off: the win screen as always (F6 still " +
             "works). Read when the component is enabled.")]
    [SerializeField] private bool continueAfterEscape = true;

    private Action continueRun;

    /// <summary>Where the player lands: <see cref="target"/>, or this transform when it is empty.</summary>
    public Transform Target => target != null ? target : transform;

    private void Awake() => continueRun = HandleEscapeEnded;

    // Enabled-scoped like GameResultManager.GameOverPresenter's owner, and not Awake/OnDestroy like a
    // static event: this is a slot that changes how the run ends, and a disabled component must not
    // keep the win screen away.
    private void OnEnable()
    {
        if (continueAfterEscape) EscapeSequenceDirector.ContinuePastEnding = continueRun;
    }

    private void OnDisable()
    {
        if (EscapeSequenceDirector.ContinuePastEnding == continueRun)
            EscapeSequenceDirector.ContinuePastEnding = null;
    }

    private void HandleEscapeEnded()
    {
        if (TeleportNow())
            Debug.Log($"[{nameof(Zone2EntryTeleport)}] The escape is over: the player is at " +
                      $"'{Target.name}'.", this);
        else
            Debug.LogWarning($"[{nameof(Zone2EntryTeleport)}] The escape is over but the player " +
                             "could not be moved: they keep control where the ending left them.", this);
    }

    /// <summary>
    /// Puts the player on <see cref="Target"/> now: the body there and turned to its yaw, momentum
    /// dropped (<see cref="PlayerStateManager.TeleportTo"/>, the checkpoint respawn's own path), the
    /// look camera behind them at the rig's resting height, snapped rather than swung across the map.
    /// </summary>
    /// <returns>false when there is no player, or it is hidden or being captured (logged).</returns>
    public bool TeleportNow()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null)
        {
            Debug.LogWarning($"[{nameof(Zone2EntryTeleport)}] No player registered: nothing to move.", this);
            return false;
        }

        bool inHidingSpot = player.CurrentHidingSpot != null;
        if (inHidingSpot || player.IsRecoveringFromCapture)
        {
            Debug.LogWarning($"[{nameof(Zone2EntryTeleport)}] The player is " +
                             (inHidingSpot ? "inside a hiding spot" : "being captured") +
                             ": not moved. Try again once they have control.", this);
            return false;
        }

        Transform destination = Target;
        float yaw = destination.eulerAngles.y;

        // Yaw only: a spawn marker tilted by accident must not lay the capsule over.
        player.TeleportTo(destination.position, Quaternion.Euler(0f, yaw, 0f));
        PlaceCameraBehind(yaw);
        return true;
    }

    /// <summary>The look camera behind the player, along <paramref name="yaw"/>, at the height the rig
    /// rests at, and told to forget where it was: a cut, not a swing across the level.</summary>
    private static void PlaceCameraBehind(float yaw)
    {
        PlayerCameraController look = FindAnyObjectByType<PlayerCameraController>();
        if (look == null) return;

        look.FaceYaw(yaw);

        CinemachineOrbitalFollow orbital = look.GetComponent<CinemachineOrbitalFollow>();
        if (orbital != null)
            orbital.VerticalAxis.Value = orbital.VerticalAxis.ClampValue(orbital.VerticalAxis.Center);

        CinemachineCamera cam = look.GetComponent<CinemachineCamera>();
        if (cam != null) cam.PreviousStateIsValid = false;
    }

    private void OnDrawGizmos()
    {
        Transform point = Target;

        Gizmos.color = new Color(0.25f, 0.6f, 1f, 0.9f);
        Gizmos.DrawWireSphere(point.position + Vector3.up * 0.9f, 0.35f);
        Gizmos.DrawLine(point.position, point.position + Vector3.up * 1.8f);

        Vector3 forward = point.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.0001f)
            Gizmos.DrawRay(point.position + Vector3.up * 0.9f, forward.normalized * 1.2f);
    }
}
