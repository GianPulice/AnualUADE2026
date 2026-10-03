using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Puppets the Nemesis for a cinematic: while it has control the Nemesis's FSM is switched off and
/// this walks, runs and turns it between scene markers. Its one job. It does not decide anything
/// about the chase — that is <see cref="NemesisEscapePursuit"/> — and it does not know which beat
/// of which cinematic is calling it (<see cref="EscapeSequenceDirector"/> does).
///
/// WHY THE FSM IS SWITCHED OFF AND NOT PAUSED WITH <c>SetExternalHold</c>: the FSM writes the
/// agent's destination and speed every frame, so a hold would fight any order given here. The
/// component is disabled instead (its senses keep ticking but nothing acts on them), and enabled
/// again by <see cref="Release"/>, from where the puppeteering left the body.
///
/// Sits on the escape sequence object, not on the Nemesis: it finds the Nemesis in the scene, so
/// the Nemesis prefab needs no edit.
/// </summary>
public class NemesisCinematicActor : MonoBehaviour
{
    [Header("Cinematic speeds")]
    [Tooltip("Metros por segundo al salir por la puerta.")]
    [SerializeField, Min(0.1f)] private float walkSpeed = 1.6f;

    [Tooltip("Metros por segundo cuando arranca a correr en la cinemática. El trote del gameplay " +
             "es otro número: SO_EscapeSequenceConfig.")]
    [SerializeField, Min(0.1f)] private float runSpeed = 4.5f;

    [Tooltip("Grados por segundo al girar hacia un marcador (mirar a izquierda / derecha).")]
    [SerializeField, Min(1f)] private float turnSpeed = 220f;

    [Tooltip("Distancia (m) a la que da por llegado un destino.")]
    [SerializeField, Min(0.05f)] private float arrivalDistance = 0.3f;

    [Header("Attack")]
    [Tooltip("Estado del Animator del Nemesis con el golpe. No tiene transiciones: se llega por " +
             "CrossFade y se queda en su último frame hasta que algo lo mueva.")]
    [SerializeField] private string attackState = "E_Attack";

    [Tooltip("Segundos de crossfade hacia el golpe, y de vuelta a Idle cuando termina.")]
    [SerializeField, Min(0f)] private float attackBlendSeconds = 0.1f;

    private static readonly int IdleStateHash = Animator.StringToHash("Idle");

    /// <summary>Raised once when a walk or run ends on its marker.</summary>
    public event Action Arrived;

    private NemesisStateManager nemesis;
    private bool hasControl;
    private bool moving;
    private bool attacking;
    private bool warnedNoAttackState;
    private Transform facing;

    public bool HasControl => hasControl;

    /// <summary>Metres per second of a <see cref="RunTo"/>: a cinematic that times the Nemesis's
    /// arrival works it out from this.</summary>
    public float RunSpeed => runSpeed;

    /// <summary>The Nemesis is in its own Catch state, its machine running: a capture is being
    /// resolved and it must not be taken (its Catch would freeze, and the capture never end).</summary>
    public bool IsNemesisCatching
    {
        get
        {
            if (nemesis == null) nemesis = FindAnyObjectByType<NemesisStateManager>();
            return nemesis != null && nemesis.enabled &&
                   nemesis.CurrentStateKey == NemesisStateManager.ENemesisState.Catch;
        }
    }

    /// <summary>
    /// A capture that ended the run leaves the Nemesis parked in its Catch state, waiting for a
    /// respawn that never comes. Sends it back to Patrolling so it can be taken and released like
    /// any other time. Does nothing when it is not catching.
    /// </summary>
    public void AbortCapture()
    {
        if (!IsNemesisCatching) return;
        nemesis.TransitionToState(NemesisStateManager.ENemesisState.Patrolling);
    }

    /// <summary>The Nemesis is awake: not dormant, waiting for a script (or its puzzle) to wake it.
    /// </summary>
    public bool IsNemesisAwake
    {
        get
        {
            if (nemesis == null) nemesis = FindAnyObjectByType<NemesisStateManager>();
            return nemesis != null && nemesis.IsActive;
        }
    }

    /// <summary>Walking or running to a marker (false once it has arrived and stands).</summary>
    public bool IsMoving => hasControl && moving;

    /// <summary>The Nemesis's body, for a camera to follow. Null until it has been found.</summary>
    public Transform Body => nemesis != null ? nemesis.transform : null;

    /// <summary>
    /// Takes the Nemesis, waking it if it was still dormant. In Zona1 it always is the first time:
    /// it sleeps until this cinematic (NemesisController.WakeOnlyFromScript).
    /// </summary>
    /// <returns>false when there is no Nemesis in the scene: the cinematic then plays without it.
    /// </returns>
    public bool TryTakeControl()
    {
        if (hasControl) return true;

        if (nemesis == null) nemesis = FindAnyObjectByType<NemesisStateManager>();
        if (nemesis == null)
        {
            Debug.LogWarning($"[{nameof(NemesisCinematicActor)}] No Nemesis in the scene: the " +
                             "cinematic plays without it.", this);
            return false;
        }

        // Woken where it stands, not at a spawn point: every caller warps it onto a marker right
        // after this. The spawn search refuses anywhere near the player or in their sight, and
        // waiting for it would leave the cinematic without its Nemesis.
        if (!nemesis.IsActive) nemesis.ActivateInPlace();

        nemesis.enabled = false;
        nemesis.SetStoppingDistance(arrivalDistance);
        Stand();

        hasControl = true;
        return true;
    }

    /// <summary>Puts the Nemesis on the marker, facing where the marker faces.</summary>
    public void WarpTo(Transform marker)
    {
        if (marker == null) return;
        WarpTo(marker.position, marker.eulerAngles.y);
    }

    /// <summary>Puts the Nemesis on a point with no marker (a skip placing it where the run would
    /// have got to), facing the given yaw.</summary>
    public void WarpTo(Vector3 position, float yaw)
    {
        if (!hasControl) return;

        nemesis.WarpTo(position);
        nemesis.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        nemesis.ResetGaitSampling();
        Stand();
    }

    public void WalkTo(Transform marker) => MoveTo(marker, NemesisStateManager.EGait.Walking, walkSpeed);

    public void RunTo(Transform marker) => MoveTo(marker, NemesisStateManager.EGait.Running, runSpeed);

    /// <summary>
    /// Shows or hides the Nemesis's eyes (the two points that read through the fog), so a shot can
    /// keep it a shape in the dark until the moment its eyes open. Only the escape's own business:
    /// dormancy sets them again the next time the Nemesis sleeps or wakes.
    /// </summary>
    public void SetEyesVisible(bool visible)
    {
        NemesisEyes eyes = FindEyes();
        if (eyes != null) eyes.SetLightsEnabled(visible);
    }

    /// <summary>The point between the Nemesis's eyes, riding on its head, for a shot to be trained
    /// on. Null when there is no Nemesis or it has no eyes.</summary>
    public Transform EyesPoint
    {
        get
        {
            NemesisEyes eyes = FindEyes();
            return eyes != null ? eyes.Center : null;
        }
    }

    private NemesisEyes FindEyes()
    {
        if (nemesis == null) nemesis = FindAnyObjectByType<NemesisStateManager>();
        return nemesis != null ? nemesis.GetComponentInChildren<NemesisEyes>() : null;
    }

    /// <summary>Turns in place to look at the marker (standing still; ignored while moving).</summary>
    public void FaceTowards(Transform marker)
    {
        if (!hasControl) return;
        facing = marker;
    }

    /// <summary>
    /// Stops where it is and strikes at whatever it is facing (<see cref="FaceTowards"/> keeps
    /// turning it towards the marker meanwhile): plays the attack clip once, and stays on its last
    /// frame. The clip has no way out of its own, so anything that moves the Nemesis again
    /// (<see cref="WarpTo(Vector3, float)"/>, a walk or run, <see cref="Release"/>) takes it back to
    /// Idle first — left in the pose, the gait bools would never pull it out.
    ///
    /// By CrossFade on the state's name, with HasState and a warning: the same pattern as
    /// <see cref="NemesisStateManager.PlayTraversal"/>, for a controller that has no such state.
    /// </summary>
    /// <returns>false when the Animator has no attack state: the cinematic plays on without the
    /// strike.</returns>
    public bool Attack()
    {
        if (!hasControl) return false;

        Animator animator = nemesis.AnimController;
        int hash = Animator.StringToHash(attackState);

        if (animator == null || animator.runtimeAnimatorController == null || !animator.HasState(0, hash))
        {
            if (!warnedNoAttackState)
            {
                warnedNoAttackState = true;
                Debug.LogWarning($"[{nameof(NemesisCinematicActor)}] The Nemesis's Animator has no " +
                                 $"state '{attackState}': the cinematic plays without the strike.", this);
            }
            return false;
        }

        // Standing first: it halts the agent and drops the gait bools, and that call is also what
        // would end a strike already playing, so it has to come before this one starts.
        Stand();

        attacking = true;
        animator.CrossFadeInFixedTime(hash, attackBlendSeconds, 0, 0f);
        return true;
    }

    /// <summary>Takes the Nemesis out of the attack clip if it is in it. See <see cref="Attack"/>.</summary>
    private void EndAttack()
    {
        if (!attacking) return;
        attacking = false;

        Animator animator = nemesis != null ? nemesis.AnimController : null;
        if (animator == null || animator.runtimeAnimatorController == null || !animator.HasState(0, IdleStateHash))
            return;

        animator.CrossFadeInFixedTime(IdleStateHash, attackBlendSeconds, 0);
    }

    /// <summary>
    /// Gives the Nemesis back to its FSM, mid-stride if it is still moving: the state it re-enters
    /// (Chasing, with <see cref="NemesisEscapePursuit"/> on) sets its own gait on the same frame.
    /// </summary>
    public void Release()
    {
        if (!hasControl) return;
        EndAttack();
        hasControl = false;
        moving = false;
        facing = null;

        nemesis.SetStoppingDistance(nemesis.DefaultStoppingDistance);
        if (nemesis.IsAgentReady) nemesis.NavAgent.isStopped = false;

        nemesis.ResetGaitSampling();
        nemesis.enabled = true;
    }

    private void MoveTo(Transform marker, NemesisStateManager.EGait gait, float speed)
    {
        if (!hasControl || marker == null || !nemesis.IsAgentReady) return;

        EndAttack();
        facing = null;
        moving = true;

        NavMeshAgent agent = nemesis.NavAgent;
        agent.isStopped = false;
        nemesis.SetGait(gait, speed);
        agent.SetDestination(marker.position);
    }

    private void Stand()
    {
        EndAttack();
        moving = false;
        if (!nemesis.IsAgentReady) return;

        nemesis.NavAgent.velocity = Vector3.zero;
        nemesis.NavAgent.isStopped = true;
        nemesis.SetGait(NemesisStateManager.EGait.Idle, 0f);
    }

    private void Update()
    {
        if (!hasControl || !nemesis.IsAgentReady) return;

        if (moving)
        {
            NavMeshAgent agent = nemesis.NavAgent;
            if (agent.pathPending || agent.remainingDistance > agent.stoppingDistance + 0.05f) return;

            Stand();
            Arrived?.Invoke();
            return;
        }

        if (facing == null) return;

        Vector3 to = facing.position - nemesis.transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return;

        nemesis.transform.rotation = Quaternion.RotateTowards(
            nemesis.transform.rotation, Quaternion.LookRotation(to), turnSpeed * Time.deltaTime);
    }
}
