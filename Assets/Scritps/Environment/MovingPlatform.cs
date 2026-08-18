using UnityEngine;

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

        if (traveled >= config.Distance)
        {
            state = State.WaitingForExit;
        }
    }
}
