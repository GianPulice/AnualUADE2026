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

    /// <summary>Raised once when a walk or run ends on its marker.</summary>
    public event Action Arrived;

    private NemesisStateManager nemesis;
    private bool hasControl;
    private bool moving;
    private Transform facing;

    public bool HasControl => hasControl;

    /// <summary>
    /// Takes the Nemesis, waking it if it was still dormant.
    /// </summary>
    /// <returns>false when there is no Nemesis (or it could not appear): the cinematic then plays
    /// without it.</returns>
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

        if (!nemesis.IsActive) nemesis.Activate();
        if (!nemesis.IsActive)
        {
            Debug.LogWarning($"[{nameof(NemesisCinematicActor)}] The Nemesis is dormant and found " +
                             "no safe spawn: the cinematic plays without it.", this);
            return false;
        }

        nemesis.enabled = false;
        nemesis.SetStoppingDistance(arrivalDistance);
        Stand();

        hasControl = true;
        return true;
    }

    /// <summary>Puts the Nemesis on the marker, facing where the marker faces.</summary>
    public void WarpTo(Transform marker)
    {
        if (!hasControl || marker == null) return;

        nemesis.WarpTo(marker.position);
        nemesis.transform.rotation = Quaternion.Euler(0f, marker.eulerAngles.y, 0f);
        nemesis.ResetGaitSampling();
        Stand();
    }

    public void WalkTo(Transform marker) => MoveTo(marker, NemesisStateManager.EGait.Walking, walkSpeed);

    public void RunTo(Transform marker) => MoveTo(marker, NemesisStateManager.EGait.Running, runSpeed);

    /// <summary>Turns in place to look at the marker (standing still; ignored while moving).</summary>
    public void FaceTowards(Transform marker)
    {
        if (!hasControl) return;
        facing = marker;
    }

    /// <summary>
    /// Gives the Nemesis back to its FSM, mid-stride if it is still moving: the state it re-enters
    /// (Chasing, with <see cref="NemesisEscapePursuit"/> on) sets its own gait on the same frame.
    /// </summary>
    public void Release()
    {
        if (!hasControl) return;
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

        facing = null;
        moving = true;

        NavMeshAgent agent = nemesis.NavAgent;
        agent.isStopped = false;
        nemesis.SetGait(gait, speed);
        agent.SetDestination(marker.position);
    }

    private void Stand()
    {
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
