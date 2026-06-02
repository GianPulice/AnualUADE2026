using System.Threading;
using UnityEditor.Rendering;
using UnityEngine;

public class GrabbableBall : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";

    private Rigidbody rb;
    private Transform currentTriggerTransform;
    private PlayerStateManager player;
    private bool playerNearby;
    private bool isGrabbed;

    private float spamProtectionTimer = 0.2f;
    private float currentTimer = 0;

    public string PlayerTag { get => playerTag; set => playerTag = value; }
    public Transform CurrentTriggerTransform { get => currentTriggerTransform; set => currentTriggerTransform = value; }
    public PlayerStateManager Player { get => player; set => player = value; }
    public bool PlayerNearby { get => playerNearby; set => playerNearby = value; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (!playerNearby && !isGrabbed) return;

        if (currentTimer >= spamProtectionTimer)
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                currentTimer = 0;
                if (!isGrabbed)
                    Grab();
                else
                    Release();
            }
        }
        else currentTimer += Time.deltaTime;
    }

    /*private void OnTriggerEnter(Collider other)
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

        //holdPoint = foundHoldPoint;
        player = other.GetComponent<PlayerStateManager>();
        playerNearby = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        playerNearby = false;
        player = null;

        if (!isGrabbed) ;
            //holdPoint = null;
    }*/

    private void Grab()
    {
        if (player == null) return;

        isGrabbed = true;

        rb.mass = 1;
        //rb.isKinematic = true;
        //rb.useGravity = false;

        player.IsInteracting = true;
        Vector3 tempDir = new Vector3(transform.position.x - currentTriggerTransform.position.x, 0, transform.position.z - currentTriggerTransform.position.z).normalized;
        player.SetPlayerPositionAndDirection(currentTriggerTransform.position, tempDir);

        //transform.SetParent(player.transform,true);
        //transform.localPosition = Vector3.zero;
        //transform.localRotation = Quaternion.identity;
    }

    private void Release()
    {
        player.IsInteracting = false;

        isGrabbed = false;

        //transform.SetParent(null);

        rb.mass = 1000;
        //rb.isKinematic = false;
        //rb.useGravity = true;

        //holdPoint = null;
    }
}
