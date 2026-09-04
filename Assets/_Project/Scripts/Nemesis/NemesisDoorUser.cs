using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Makes the Nemesis open doors.
///
/// Goes on the Nemesis root, next to <see cref="NemesisStateManager"/>. It does not touch the FSM
/// and does not care which state is running: while the agent is moving it looks where the agent
/// wants to go and, if a closed door is in the way, opens it. That covers patrol, investigation
/// and chase alike, and adds no new state to maintain.
///
/// Why the sweep follows <c>desiredVelocity</c> and not <c>transform.forward</c>: the agent
/// rotates smoothly towards where it is heading, so on a corner the forward vector keeps pointing
/// at the wall long after the path has already turned. desiredVelocity is the real direction of
/// the next step.
///
/// SETUP: <see cref="doorMask"/> must include the layer the door colliders live on — in this
/// project, Interactable (layer 6) and/or Default. Left at Nothing, the component warns once and
/// disables itself instead of failing silently.
/// </summary>
[RequireComponent(typeof(NemesisStateManager))]
public class NemesisDoorUser : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("How far ahead it looks for doors. Should be at least speed * the door's " +
             "OpenDuration, so the door has time to swing without the Nemesis having to stop.")]
    [SerializeField, Min(0.5f)] private float detectionDistance = 3f;

    [Tooltip("Thickness of the sweep. Roughly the agent radius, so it catches the door it is " +
             "about to brush past and not only the one dead ahead.")]
    [SerializeField, Min(0.1f)] private float detectionRadius = 0.6f;

    [Tooltip("Height above the pivot the sweep starts from. At floor level it catches the ground.")]
    [SerializeField] private float probeHeight = 1f;

    [Tooltip("Layers the door colliders live on.\n\n" +
             "In this project that is Default AND Interactable. The DoorWood prefabs carry the " +
             "DoorInteractable on a root in Interactable, but that root's own collider is " +
             "disabled — the colliders that actually exist are the leaf and frame meshes, and " +
             "those sit in Default. With Interactable alone the sweep matches only the disabled " +
             "collider and finds nothing, which reads as the Nemesis ignoring doors entirely.")]
    [SerializeField] private LayerMask doorMask;

    [Tooltip("Seconds between sweeps. A performance knob, not a design value: a door that takes " +
             "half a second to swing does not need a SphereCast every frame.")]
    [SerializeField, Min(0.02f)] private float checkInterval = 0.2f;

    [Header("Crossing")]
    [Tooltip("Safety margin when working out whether it would reach the door before the swing " +
             "finishes. If it still would, it waits just long enough and carries on.")]
    [SerializeField, Min(0f)] private float crossingSafetyMargin = 0.15f;

    /// <summary>
    /// Hits considered per sweep. The mask has to include Default, where the door meshes live, and
    /// Default is also where every crate, barrel and pallet in the level sits — so a single sweep
    /// routinely returns several hits that are not doors.
    ///
    /// Sized generously on purpose: SphereCastNonAlloc truncates in an unspecified order, NOT by
    /// distance. A buffer that fills up does not return "the nearest 16", it returns "some 16" —
    /// so an undersized buffer drops doors at random in a cluttered corridor, which is the worst
    /// possible failure mode to debug.
    /// </summary>
    private const int HitBufferSize = 48;

    private NemesisStateManager stateManager;
    private NavMeshAgent agent;
    private float checkTimer;
    private bool isWaitingForDoor;
    private bool hasWarnedAboutFullBuffer;

    private readonly RaycastHit[] hitBuffer = new RaycastHit[HitBufferSize];

    private void Awake()
    {
        stateManager = GetComponent<NemesisStateManager>();
        agent = GetComponent<NavMeshAgent>();

        if (agent == null && stateManager != null) agent = stateManager.NavAgent;

        if (doorMask.value != 0) return;

        Debug.LogError($"[{nameof(NemesisDoorUser)}] '{name}' has doorMask set to Nothing, so it " +
                       "will never find a door. Assign the layer the door colliders are on " +
                       "(Interactable, plus Default if the leaf collider ended up there). " +
                       "Component disabled.", this);
        enabled = false;
    }

    private void Update()
    {
        // Same gates as the rest of the Nemesis: dormant it interacts with nothing, and paused it
        // must not either — this Update is its own and the state manager's guard does not cover it.
        if (stateManager == null || !stateManager.IsActive) return;
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;
        if (isWaitingForDoor) return;

        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

        checkTimer -= Time.deltaTime;
        if (checkTimer > 0f) return;
        checkTimer = checkInterval;

        if (!TryFindDoorAhead(out DoorInteractable door, out float distanceToDoor)) return;
        if (!door.TryOpenForNemesis()) return;

        float waitTime = ComputeCrossingWait(door, distanceToDoor);
        if (waitTime > 0f) WaitForDoorAsync(waitTime).Forget();
    }

    /// <summary>
    /// Sweeps towards where the agent wants to go and returns the first closed door it finds.
    /// </summary>
    private bool TryFindDoorAhead(out DoorInteractable door, out float distance)
    {
        door = null;
        distance = 0f;

        Vector3 direction = agent.desiredVelocity;
        direction.y = 0f;

        // Standing at a waypoint there is nothing to look towards, and opening the door next to it
        // would be opening it for no reason. The forward vector only steps in as a fallback when
        // the Nemesis is genuinely moving but the agent has not resolved the step yet.
        if (direction.sqrMagnitude < 0.01f)
        {
            if (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance) return false;

            direction = transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return false;
        }

        direction.Normalize();
        Vector3 origin = transform.position + Vector3.up * probeHeight;

        // SphereCastNonAlloc and not SphereCast: the single-hit version returns whatever is
        // closest, and since the mask has to include Default, the closest thing is very often a
        // crate or a barrel. That hit resolves to no door, the sweep gives up, and the door two
        // metres behind the crate is never opened. Every hit gets examined instead, and the
        // nearest one that IS a door wins.
        //
        // QueryTriggerInteraction.Collide: on some doors the collider standing in for the leaf is
        // marked as a trigger so the interaction raycast picks it up.
        int hitCount = Physics.SphereCastNonAlloc(origin, detectionRadius, direction, hitBuffer,
                                                  detectionDistance, doorMask,
                                                  QueryTriggerInteraction.Collide);
        if (hitCount == 0) return false;

        // Saturated: hits were dropped, and since the truncation is not by distance the one that
        // got dropped may well be the door. Warned once rather than per sweep, because if it
        // happens at all it happens several times a second.
        if (hitCount == hitBuffer.Length && !hasWarnedAboutFullBuffer)
        {
            hasWarnedAboutFullBuffer = true;
            Debug.LogWarning($"[{nameof(NemesisDoorUser)}] '{name}': the sweep filled its " +
                             $"{hitBuffer.Length}-hit buffer, so some colliders were dropped and a " +
                             "door may be missed. Narrow doorMask, or shrink detectionDistance / " +
                             "detectionRadius.", this);
        }

        float nearest = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hitBuffer[i];
            if (hit.distance >= nearest) continue;

            // InParent and not GetComponent: the collider is on a descendant (the leaf mesh, or
            // the frame) while the DoorInteractable lives on the door's root.
            DoorInteractable found = hit.collider.GetComponentInParent<DoorInteractable>();
            if (found == null || found.IsOpen || found.IsAnimating || !found.NemesisCanOpen) continue;

            door = found;
            nearest = hit.distance;
        }

        distance = nearest;
        return door != null;
    }

    /// <summary>
    /// How long to hold back so it does not walk through the leaf mid-swing.
    ///
    /// Almost always zero: at patrol speed the three metres of detection take longer to cover than
    /// the door takes to open, so it never stops. The wait only shows up while running, which is
    /// when it would genuinely reach the door closed.
    /// </summary>
    private float ComputeCrossingWait(DoorInteractable door, float distanceToDoor)
    {
        float speed = Mathf.Max(0.01f, agent.speed);
        float timeToReach = distanceToDoor / speed;
        float timeToOpen = door.OpenDuration + crossingSafetyMargin;

        return Mathf.Max(0f, timeToOpen - timeToReach);
    }

    /// <summary>
    /// Holds the agent just long enough for the door to finish opening.
    ///
    /// UniTask and not a coroutine, per the project's async convention — and here it is also the
    /// correct choice rather than the conventional one. A coroutine is stopped dead when its
    /// MonoBehaviour is disabled, and Unity does not dispose the iterator, so the finally below
    /// would never run: the agent would stay stopped and the stuck suppression would leak, leaving
    /// the Nemesis frozen with its own escape hatch switched off. UniTask is driven by the
    /// PlayerLoop, so the continuation still arrives and the cleanup happens.
    ///
    /// Scaled time on purpose: this is a gameplay wait, and it should freeze with a pause menu the
    /// same way the door's own swing does.
    /// </summary>
    private async UniTaskVoid WaitForDoorAsync(float seconds)
    {
        isWaitingForDoor = true;
        stateManager.PushStuckSuppression();

        bool stopped = false;
        try
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                stopped = true;
            }

            await UniTask.Delay(TimeSpan.FromSeconds(seconds),
                                cancellationToken: this.GetCancellationTokenOnDestroy());
        }
        finally
        {
            if (stopped && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
                agent.isStopped = false;

            if (stateManager != null) stateManager.PopStuckSuppression();
            isWaitingForDoor = false;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position + Vector3.up * probeHeight;
        Vector3 direction = Application.isPlaying && agent != null && agent.hasPath
            ? agent.desiredVelocity.normalized
            : transform.forward;

        Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(origin, detectionRadius);
        Gizmos.DrawWireSphere(origin + direction * detectionDistance, detectionRadius);
        Gizmos.DrawLine(origin, origin + direction * detectionDistance);
    }
}
