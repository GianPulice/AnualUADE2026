using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// The reveal's turn: the player turns round, slowly, to face what is behind them — the Nemesis's
/// eyes in the fog — and their own camera turns with them, from behind, settling at its neutral
/// height. One job: the turn. WHEN it happens and what is there are the director's
/// (<see cref="EscapeSequenceDirector"/>).
///
/// It drives the gameplay rig's own axes, so there is no cut in or out: the last frame of the turn
/// IS the gameplay camera, and control comes back looking where the turn ended. Levelling the
/// camera matters: it arrives at the corridor tilted wherever the player last looked (down at the
/// socket, often), and the reveal used to open on the floor.
///
/// The player is frozen by the cinematic, so nothing else turns the body: this rotates the model
/// (<see cref="PlayerStateManager.PlayerBody"/>) every frame, and the root once, at the end. Look
/// input is off for the whole cinematic (<see cref="CinematicState"/>), so nothing fights the
/// camera either.
///
/// Left out on purpose: no turn-in-place animation (the rig has none), and no stepping.
///
/// Sits on the escape sequence object; it finds the camera rig at runtime.
/// </summary>
public class EscapePlayerTurn : MonoBehaviour
{
    [Tooltip("Forma del giro a lo largo de su duración: arranca y termina suave.")]
    [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private CinemachineOrbitalFollow orbital;
    private PlayerStateManager player;

    private float startBodyYaw;
    private float startCameraYaw;
    private float targetYaw;
    private float startVertical;
    private float endVertical;

    private float elapsed;
    private float duration;
    private bool active;

    public bool IsTurning => active;

    /// <summary>Starts turning the player to face <paramref name="lookAt"/> over
    /// <paramref name="seconds"/>, the camera following behind them.</summary>
    public void Begin(PlayerStateManager who, Vector3 lookAt, float seconds)
    {
        if (who == null) return;

        Vector3 to = lookAt - who.transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return;

        player = who;
        targetYaw = Quaternion.LookRotation(to).eulerAngles.y;
        startBodyYaw = player.PlayerBody != null ? player.PlayerBody.eulerAngles.y : player.transform.eulerAngles.y;

        if (FindRig())
        {
            startCameraYaw = orbital.HorizontalAxis.Value;
            startVertical = orbital.VerticalAxis.Value;
            endVertical = orbital.VerticalAxis.Center;
        }

        duration = Mathf.Max(0.05f, seconds);
        elapsed = 0f;
        active = true;

        Apply(0f);
    }

    /// <summary>Lands on the end of the turn at once. What a skip needs.</summary>
    public void Finish()
    {
        if (!active) return;

        Apply(1f);
        Complete();
    }

    /// <summary>Stops where it is, without landing (the run ended under it).</summary>
    public void Stop() => active = false;

    // Update and not LateUpdate: CinemachineBrain reads the axes in its own LateUpdate.
    private void Update()
    {
        if (!active) return;

        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);
        Apply(progress);

        if (progress >= 1f) Complete();
    }

    private void Apply(float progress)
    {
        float k = Mathf.Clamp01(ease.Evaluate(progress));

        if (player != null && player.PlayerBody != null)
            player.PlayerBody.forward = Quaternion.Euler(0f, Mathf.LerpAngle(startBodyYaw, targetYaw, k), 0f) * Vector3.forward;

        if (orbital == null) return;

        // The camera's horizontal value is the world yaw it looks along: at the end, the player's.
        float yaw = Mathf.LerpAngle(startCameraYaw, targetYaw, k);
        orbital.HorizontalAxis.Value = WakeUpCameraPan.WrapToRange(yaw, orbital.HorizontalAxis.Range);
        orbital.VerticalAxis.Value = Mathf.Lerp(startVertical, endVertical, k);
    }

    private void Complete()
    {
        active = false;

        // The root too, so the next move and the next teleport agree with where the body faces.
        if (player != null) player.TeleportTo(player.transform.position, Quaternion.Euler(0f, targetYaw, 0f));
    }

    private bool FindRig()
    {
        if (orbital != null) return true;

        PlayerCameraController controller = FindAnyObjectByType<PlayerCameraController>();
        orbital = controller != null ? controller.GetComponent<CinemachineOrbitalFollow>() : null;

        if (orbital == null)
            Debug.LogWarning($"[{nameof(EscapePlayerTurn)}] No player camera rig found: the player " +
                             "turns, the camera does not.", this);
        return orbital != null;
    }
}
