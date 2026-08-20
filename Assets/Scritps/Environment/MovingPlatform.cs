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
/// </summary>
public class MovingPlatform : MonoBehaviour
{
    [SerializeField] private SO_MovingPlatform config;
    private string playerTag = "Player";

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

    public float RideDistance => config != null ? config.Distance : 0f;

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

        float step = config.Speed * Time.fixedDeltaTime;
        float remaining = config.Distance - traveled;
        if (step > remaining) step = remaining;

        Vector3 delta = (goingUp ? Vector3.up : Vector3.down) * step;
        transform.position += delta;
        traveled += step;

        if (passengerRb != null)
            passengerRb.MovePosition(passengerRb.position + delta);

        MoveExtraPassengers(delta);

        if (traveled >= config.Distance)
        {
            state = State.WaitingForExit;
            OnRideCompleted?.Invoke();
        }
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
