using UnityEngine;

/// <summary>
/// Puppets the player for a cinematic: runs the (disabled) player in a straight line at a given
/// speed, playing the run animation, and stands them still when it arrives. Its one job — the
/// player-side twin of <see cref="NemesisCinematicActor"/>. It does not decide where, how fast or
/// why (<see cref="EscapeSequenceDirector"/> does).
///
/// The player's FSM sits in Disabled for the whole cinematic and writes nothing to the body or the
/// Animator, so this is the only thing driving them. The body keeps its Rigidbody, so gravity and
/// walls still apply; a wall in the way never lets it reach the mark, which is why a run also ends
/// on a time budget.
///
/// Sits on the escape sequence object, next to the actor: it finds the player through
/// <see cref="PlayerRegistry"/>, so the player prefab needs no edit.
/// </summary>
public class PlayerCinematicRunner : MonoBehaviour
{
    private static readonly int MoveSpeedHash = Animator.StringToHash("moveSpeed");

    // Seconds on top of the run's own length before it gives up on reaching the mark.
    private const float TimeGrace = 1f;

    // Within this of the mark (m) the run is over.
    private const float ArrivalDistance = 0.15f;

    private PlayerStateManager player;
    private Vector3 target;
    private Vector3 direction;
    private float speed;
    private float timeLeft;

    /// <summary>The player is running to a mark (false once it has arrived, or been stopped).</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Starts the run from wherever the player stands, at <paramref name="metersPerSecond"/>
    /// (the real, penalty-scaled ground speed), facing the mark.</summary>
    public void RunTo(PlayerStateManager who, Vector3 mark, float metersPerSecond)
    {
        if (who == null) return;

        player = who;
        target = mark;
        speed = Mathf.Max(0.1f, metersPerSecond);

        Vector3 to = Flat(mark - player.transform.position);
        direction = to.sqrMagnitude > 0.0001f ? to.normalized : Flat(player.transform.forward).normalized;
        timeLeft = to.magnitude / speed + TimeGrace;

        Face(direction);
        IsRunning = true;
    }

    /// <summary>Stands the player still where they are: no velocity, no run animation.</summary>
    public void Stop()
    {
        if (player != null) Stand();

        IsRunning = false;
        player = null;
    }

    private void Update()
    {
        if (!IsRunning) return;
        if (player == null)
        {
            IsRunning = false;
            return;
        }

        timeLeft -= Time.deltaTime;
        bool arrived = Vector3.Dot(Flat(target - player.transform.position), direction) <= ArrivalDistance;
        if (arrived || timeLeft <= 0f)
        {
            Stop();
            return;
        }

        Rigidbody body = player.RigBody;
        if (body != null)
            body.linearVelocity = new Vector3(direction.x * speed, body.linearVelocity.y, direction.z * speed);

        player.CurrentVelocity = speed;
        Face(direction);

        // The pre-penalty speed, like PlayerMovingState: a limping player still plays the run blend.
        if (player.AnimController != null)
        {
            float animSpeed = speed / Mathf.Max(player.MoveSpeedPenaltyFactor, 0.01f);
            player.AnimController.SetFloat(MoveSpeedHash, animSpeed);
        }
    }

    private void Face(Vector3 forward)
    {
        if (player.PlayerBody != null) player.PlayerBody.forward = forward;
        player.NextDirection = forward;
    }

    private void Stand()
    {
        player.CurrentVelocity = 0f;

        Rigidbody body = player.RigBody;
        if (body != null) body.linearVelocity = new Vector3(0f, body.linearVelocity.y, 0f);

        if (player.AnimController != null) player.AnimController.SetFloat(MoveSpeedHash, 0f);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
