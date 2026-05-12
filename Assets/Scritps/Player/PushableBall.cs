using UnityEngine;

public class PushableBall : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float pushDistance = 1.3f;
    [SerializeField] private float moveSpeed = 8f;

    private Rigidbody rb;
    private Transform player;
    private bool playerNearby;
    private bool isPushing;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (!playerNearby && !isPushing) return;

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (!isPushing)
                StartPushing();
            else
                StopPushing();
        }
    }

    private void FixedUpdate()
    {
        if (!isPushing || player == null) return;

        Vector3 targetPosition = player.position + player.forward * pushDistance;
        targetPosition.y = transform.position.y;

        Vector3 newPosition = Vector3.Lerp(
            rb.position,
            targetPosition,
            moveSpeed * Time.fixedDeltaTime
        );

        rb.MovePosition(newPosition);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        player = other.transform;
        playerNearby = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        playerNearby = false;

        if (!isPushing)
            player = null;
    }

    private void StartPushing()
    {
        if (player == null) return;

        isPushing = true;

        rb.useGravity = true;
        rb.isKinematic = false;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    private void StopPushing()
    {
        isPushing = false;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        if (!playerNearby)
            player = null;
    }
}
