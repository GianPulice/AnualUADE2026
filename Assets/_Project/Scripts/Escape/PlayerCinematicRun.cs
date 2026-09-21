using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Runs the player to a marker during a cinematic, with the real sprint animation, so it reads as
/// "this is where you take control". One job: the run. The player is frozen by the cinematic
/// (<see cref="PlayerStateManager.IsDisabled"/>), so its FSM is not moving it: this drives the
/// Rigidbody itself, through the same <see cref="PlayerStateManager.ApplyMoveVelocity"/> the
/// moving state uses, and feeds the Animator the sprint speed.
///
/// The route is a NavMesh path, not a straight line: the corridor has props in it (a generator, a
/// spool, a pallet stack), and a straight run walks into them and stops dead. Following the path
/// makes the player weave around them the same way the player is about to do with the controls. The
/// start may be off the mesh (the safe room is an unbaked hub): the run then walks to the doorway
/// first and picks the path up there. If there is no path at all, it falls back to the straight
/// line so the sequence still ends somewhere sane.
///
/// Speed is the player's own sprint speed (SO_Movement move speed x sprint multiplier), not a
/// number here: the run looks and sounds like the one the player is about to do. To make the run
/// take a given time, place the start marker that many seconds of sprint away *along the path*
/// (the path is longer than the straight line, so measure the path, not the gap).
///
/// Left out on purpose: no obstacle avoidance of its own, no re-pathing while running (the route is
/// computed once, at the start), and no speed ramp — the cinematic is short and the props do not
/// move.
///
/// Sits on the escape sequence object.
/// </summary>
public class PlayerCinematicRun : MonoBehaviour
{
    private static readonly int MoveSpeedParam = Animator.StringToHash("moveSpeed");

    [Tooltip("How fast the body swings onto a new heading at a path corner, in degrees per second. " +
             "The straight-line run never turns, so this only shows at the corners.")]
    [SerializeField] private float turnDegreesPerSecond = 540f;

    [Tooltip("How close to a corner counts as reaching it. Too small and the run circles the corner; " +
             "too large and it cuts through the prop the corner was avoiding.")]
    [SerializeField] private float cornerRadius = 0.45f;

    // Not a field initializer: NavMeshPath cannot be built in a MonoBehaviour constructor.
    private NavMeshPath path;

    private PlayerStateManager player;
    private Transform destination;
    private Vector3[] corners;
    private int corner;
    private bool running;

    public bool IsRunning => running;

    /// <summary>Starts running to the marker. It stops on arrival, standing exactly on it.</summary>
    public void RunTo(Transform marker)
    {
        player = PlayerRegistry.Current;
        if (player == null || marker == null) return;

        destination = marker;
        corners = BuildRoute(player.transform.position, marker.position);
        corner = 0;
        running = true;
    }

    /// <summary>Stops the run and puts the player on the marker, facing where it faces. Used when
    /// the run ends on its own, and when a skip cuts it short.</summary>
    public void Finish(Transform marker)
    {
        PlayerStateManager current = PlayerRegistry.Current;
        if (current != null) player = current;

        running = false;
        corners = null;
        if (player == null || marker == null) return;

        StandStill();
        player.TeleportTo(marker.position, marker.rotation);
    }

    /// <summary>The route the run will follow, corners first to last. The player's own position is
    /// not in it: the run starts wherever the player is standing.</summary>
    private Vector3[] BuildRoute(Vector3 from, Vector3 to)
    {
        path ??= new NavMeshPath();

        bool onMesh = NavMesh.SamplePosition(from, out NavMeshHit start, 4f, NavMesh.AllAreas) &&
                      NavMesh.SamplePosition(to, out NavMeshHit end, 3f, NavMesh.AllAreas) &&
                      NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) &&
                      path.status == NavMeshPathStatus.PathComplete &&
                      path.corners.Length > 1;

        if (!onMesh)
        {
            Debug.LogWarning($"[{nameof(PlayerCinematicRun)}] No NavMesh path from {from} to {to}: " +
                             "the run goes straight and may walk into the corridor props. Check that " +
                             "the destination marker stands on the baked NavMesh.", this);
            return new[] { to };
        }

        List<Vector3> route = new List<Vector3>(path.corners.Length);

        // The run can start OFF the mesh: the safe room the player comes out of is a hub that is
        // not baked. The path can only start at the doorway, so walk there first — otherwise the
        // first leg aims at a corner down the corridor and cuts diagonally through the wall.
        Vector3 entry = start.position;
        if (Flat(entry - from).magnitude > 0.5f) route.Add(entry);

        for (int i = 1; i < path.corners.Length; i++) route.Add(path.corners[i]);

        // The marker places the destination more precisely than the NavMesh sample does.
        route[route.Count - 1] = to;
        return route.ToArray();
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    private void Update()
    {
        if (!running || player == null || destination == null || corners == null) return;

        float speed = SprintSpeed();
        bool last = corner >= corners.Length - 1;

        Vector3 to = corners[corner] - player.transform.position;
        to.y = 0f;

        float reached = last ? Mathf.Max(speed * Time.deltaTime, 0.1f) : cornerRadius;
        if (to.magnitude <= reached)
        {
            if (last) { Finish(destination); return; }
            corner++;
            return;
        }

        Vector3 direction = to.normalized;
        Face(direction);

        player.CurrentVelocity = speed;
        player.ApplyMoveVelocity(direction * speed + Vector3.up * player.RigBody.linearVelocity.y);
        player.AnimController.SetFloat(MoveSpeedParam, speed);
    }

    private void Face(Vector3 direction)
    {
        if (player.PlayerBody == null) return;

        float step = turnDegreesPerSecond * Mathf.Deg2Rad * Time.deltaTime;
        player.PlayerBody.forward = Vector3.RotateTowards(player.PlayerBody.forward, direction, step, 0f);
    }

    private float SprintSpeed()
    {
        SO_Movement movement = player.Movement;
        if (movement == null) return 4f;

        // The same product the moving state uses: EffectiveMoveSpeed carries the legs penalty and
        // SprintPenaltyFactor the chest one, so a penalised player runs the cinematic at the speed
        // they are about to have with the controls.
        return player.EffectiveMoveSpeed * movement.SprintSpeedMultiplier * player.SprintPenaltyFactor;
    }

    private void StandStill()
    {
        player.CurrentVelocity = 0f;
        player.ApplyMoveVelocity(Vector3.up * player.RigBody.linearVelocity.y);
        player.AnimController.SetFloat(MoveSpeedParam, 0f);
    }
}
