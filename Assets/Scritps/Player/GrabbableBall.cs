using UnityEngine;

public class GrabbableBall : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";

    private Rigidbody rb;
    private Transform holdPoint;
    private bool playerNearby;
    private bool isGrabbed;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (!playerNearby && !isGrabbed) return;

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (!isGrabbed)
                Grab();
            else
                Release();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        Transform foundHoldPoint = null;

        foreach (Transform child in other.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "HoldPoint")
            {
                foundHoldPoint = child;
                break;
            }
        }

        if (foundHoldPoint == null)
        {
            Debug.LogWarning("No se encontró ningún HoldPoint dentro del player.");
            return;
        }

        holdPoint = foundHoldPoint;
        playerNearby = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        playerNearby = false;

        if (!isGrabbed)
            holdPoint = null;
    }

    private void Grab()
    {
        if (holdPoint == null) return;

        isGrabbed = true;

        rb.isKinematic = true;
        rb.useGravity = false;

        transform.SetParent(holdPoint);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    private void Release()
    {
        isGrabbed = false;

        transform.SetParent(null);

        rb.isKinematic = false;
        rb.useGravity = true;

        holdPoint = null;
    }
}
