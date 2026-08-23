using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Freight elevator / shuttle platform. The player steps on, StartDelay passes, it travels
/// Distance vertically and stays there until the passenger steps off; the next trip goes the other
/// way.
///
/// Besides the player (who boards on their own, by trigger and by tag) it accepts explicit
/// passengers with no Rigidbody through <see cref="AddPassenger"/>, and can be called from code
/// with <see cref="RequestRide"/>. That is what lets the Nemesis use it: a NavMeshAgent has no
/// Rigidbody and the NavMesh does not travel with the platform, so its component switches the
/// agent off for the trip and lets the platform carry it as a loose passenger.
///
/// Nobody but code (the Nemesis) can call it back once it is parked away from them — there is no
/// physical call button anywhere in the project. <see cref="autoReturnToBottom"/> is the fix for
/// that: left idle at the top with nobody on it, the platform calls itself back down so the
/// player is never permanently locked out of a floor because the Nemesis rode up and walked away.
/// </summary>
public class MovingPlatform : MonoBehaviour
{
    [SerializeField] private SO_MovingPlatform config;
    private string playerTag = "Player";

    [Header("Auto-return")]
    [Tooltip("When parked at the top with nobody on it, call itself back down to the bottom " +
             "after autoReturnDelay seconds.\n\n" +
             "Exists because only code can summon this platform (NemesisElevatorUser.RequestRide) " +
             "— there is no button or trigger anywhere that lets the PLAYER call it. Without this, " +
             "a Nemesis that rides up and leaves strands the cabin there, and the player has no " +
             "way to bring it back down for the rest of the run.")]
    [SerializeField] private bool autoReturnToBottom = false;

    [Tooltip("Seconds idle and unoccupied at the top before it calls itself back down.\n\n" +
             "Must comfortably exceed how long boarding and stepping off actually take at this " +
             "shaft (NemesisElevatorUser's boardingSpeed, ~1-2s for a typical gap). Too short and " +
             "the cabin can start sinking out from under a passenger still walking off the ride " +
             "point onto the landing — cosmetic, not gameplay-breaking, but it looks broken. The " +
             "5s default clears that with room to spare.")]
    [SerializeField, Min(0.5f)] private float autoReturnDelay = 5f;

    private float autoReturnTimer;

    /// <summary>Whether the CURRENT trip was started by the auto-return timer rather than by a
    /// passenger. Read on arrival to decide whether anyone is coming to release it.</summary>
    private bool isAutoReturning;

    private enum State { Idle, Waiting, Moving, WaitingForExit }

    private State state = State.Idle;
    private bool goingUp = true;
    private float traveled;
    private float waitTimer;
    private Rigidbody passengerRb;

    /// <summary>Passengers with no Rigidbody that ride along. They receive the same delta as the
    /// platform, applied straight to their Transform.</summary>
    private readonly List<Transform> extraPassengers = new List<Transform>();

    /// <summary>Parked, top or bottom, ready to be called.</summary>
    public bool IsIdle => state == State.Idle;

    /// <summary>Travelling right now (or waiting out StartDelay before setting off).</summary>
    public bool IsMoving => state == State.Moving || state == State.Waiting;

    /// <summary>Reached the far end and waiting for the passenger to step off.</summary>
    public bool HasArrived => state == State.WaitingForExit;

    /// <summary>Which way the next trip would go.</summary>
    public bool GoingUp => goingUp;

    /// <summary>Set by <see cref="SetRideDistance"/>. Negative means "nobody overrode it, use the
    /// config".</summary>
    private float distanceOverride = -1f;

    /// <summary>
    /// How far this trip travels. The override wins when one has been set.
    ///
    /// The override exists because the config is a ScriptableObject and therefore SHARED: one
    /// SO_MovingPlatform asset serves every platform in the project. Two lifts spanning different
    /// storey heights cannot both be right, and the symptom is silent — the lift overshoots or
    /// undershoots its landing by a metre or two and the Nemesis steps off into the air, or into
    /// the slab. The shipped asset says 8 while the two elevators in the project need 4.95 and
    /// 6.63.
    ///
    /// So the config's distance is the fallback for plain shuttle platforms, and a freight
    /// elevator gets its real one measured from its own landings — see
    /// <see cref="NemesisElevatorLink"/>.
    /// </summary>
    public float RideDistance => distanceOverride >= 0f ? distanceOverride
                               : config != null ? config.Distance
                               : 0f;

    /// <summary>
    /// Overrides the travel distance for this platform, in world units.
    ///
    /// Meant to be called once, from Awake, before the first trip. Changing it mid-ride would
    /// move the finish line the current trip is measuring itself against.
    /// </summary>
    public void SetRideDistance(float distance) => distanceOverride = Mathf.Max(0f, distance);

    /// <summary>Raised when the trip ends, before anyone steps off.</summary>
    public event Action OnRideCompleted;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        passengerRb = other.attachedRigidbody;

        if (state == State.Idle)
        {
            state = State.Waiting;
            waitTimer = 0f;
            autoReturnTimer = 0f;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        if (other.attachedRigidbody == passengerRb)
            passengerRb = null;

        if (state == State.WaitingForExit)
        {
            state = State.Idle;
            goingUp = !goingUp;
        }
    }

    // ── API for passengers driven from code (the Nemesis) ───────────────────

    /// <summary>
    /// Calls the platform without anyone having to enter the trigger.
    /// </summary>
    /// <returns>false when it was already travelling or waiting to be vacated — the caller has to
    /// wait until it is <see cref="IsIdle"/>.</returns>
    public bool RequestRide()
    {
        if (state != State.Idle) return false;

        state = State.Waiting;
        waitTimer = 0f;
        autoReturnTimer = 0f;
        return true;
    }

    /// <summary>Adds a passenger with no Rigidbody. Idempotent.</summary>
    public void AddPassenger(Transform passenger)
    {
        if (passenger == null || extraPassengers.Contains(passenger)) return;
        extraPassengers.Add(passenger);
    }

    public void RemovePassenger(Transform passenger) => extraPassengers.Remove(passenger);

    /// <summary>
    /// The code equivalent of stepping off: frees the platform and flips the direction of the next
    /// trip. It is needed because a code-driven passenger raises no OnTriggerExit — without this
    /// the platform stays wedged in WaitingForExit forever.
    /// </summary>
    public void ReleaseAfterRide()
    {
        if (state != State.WaitingForExit) return;

        state = State.Idle;
        goingUp = !goingUp;
    }

    private void FixedUpdate()
    {
        if (config == null) return;

        if (state == State.Idle)
        {
            TickAutoReturn();
            return;
        }

        if (state == State.Waiting)
        {
            waitTimer += Time.fixedDeltaTime;
            if (waitTimer >= config.StartDelay)
            {
                state = State.Moving;
                traveled = 0f;
            }
            return;
        }

        if (state != State.Moving) return;

        // Read once: RideDistance resolves the override every call, and the two comparisons below
        // have to agree on the same value.
        float rideDistance = RideDistance;

        float step = config.Speed * Time.fixedDeltaTime;
        float remaining = rideDistance - traveled;
        if (step > remaining) step = remaining;

        Vector3 delta = (goingUp ? Vector3.up : Vector3.down) * step;
        transform.position += delta;
        traveled += step;

        if (passengerRb != null)
            passengerRb.MovePosition(passengerRb.position + delta);

        MoveExtraPassengers(delta);

        if (traveled >= rideDistance)
        {
            state = State.WaitingForExit;
            OnRideCompleted?.Invoke();

            // An auto-return trip has no passenger, so nothing will ever raise OnTriggerExit or
            // call ReleaseAfterRide() for it — left alone it would sit in WaitingForExit forever,
            // parked at the bottom but reporting itself as still occupied. Releasing it here, in
            // the same tick it arrives, is that trip's own step-off.
            if (isAutoReturning)
            {
                isAutoReturning = false;
                ReleaseAfterRide();
            }
        }
    }

    /// <summary>
    /// Counts down to calling itself back to the bottom when parked at the top with nobody on it.
    /// See the class doc and the autoReturnToBottom tooltip for why this exists.
    /// </summary>
    private void TickAutoReturn()
    {
        // goingUp true means parked at the BOTTOM (the next trip would go up) — nothing to return
        // from. Off, or already home: no countdown running.
        if (!autoReturnToBottom || goingUp)
        {
            autoReturnTimer = 0f;
            return;
        }

        // Occupied: a player standing in the trigger, or a Nemesis mid-boarding via AddPassenger.
        // Either way this is not "idle and forgotten", it is about to be used properly.
        if (passengerRb != null || extraPassengers.Count > 0)
        {
            autoReturnTimer = 0f;
            return;
        }

        autoReturnTimer += Time.fixedDeltaTime;
        if (autoReturnTimer < autoReturnDelay) return;

        autoReturnTimer = 0f;
        isAutoReturning = RequestRide();
    }

    /// <summary>
    /// Iterated backwards because it clears destroyed passengers as it goes: a Nemesis destroyed
    /// mid-trip would otherwise leave a null entry that throws on the next frame.
    /// </summary>
    private void MoveExtraPassengers(Vector3 delta)
    {
        for (int i = extraPassengers.Count - 1; i >= 0; i--)
        {
            Transform passenger = extraPassengers[i];
            if (passenger == null)
            {
                extraPassengers.RemoveAt(i);
                continue;
            }

            passenger.position += delta;
        }
    }
}
