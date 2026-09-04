using UnityEngine;

public class GrabbableBall : MonoBehaviour
{
    private string playerTag = "Player";
    [Tooltip("Seconds the box takes to slide to the centre of its matching basket after the " +
             "BasketTrigger confirms it is the correct one. Only X/Z are tweened; Y is preserved.")]
    [SerializeField, Min(0f)] private float snapDuration = 0.35f;

    private Rigidbody rb;
    private Transform currentTriggerTransform;
    private PlayerStateManager player;
    private bool playerNearby;
    private bool isGrabbed;
    private bool locked;

    private float spamProtectionTimer = 0.5f;
    private float currentTimer = 0;

    public string PlayerTag { get => playerTag; set => playerTag = value; }
    public Transform CurrentTriggerTransform { get => currentTriggerTransform; set => currentTriggerTransform = value; }
    public PlayerStateManager Player { get => player; set => player = value; }
    public bool PlayerNearby { get => playerNearby; set => playerNearby = value; }
    public bool IsLocked => locked;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        // Required guard: Time.timeScale = 0 does NOT stop Input.GetKeyDown, so without this
        // a crate could be grabbed or dropped through the open inventory or the pause menu.
        // See docs/CLAUDE.md, Gameplay Input Guard.
        if (PauseManager.IsGameplayInputBlocked) return;

        if (locked) return;
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
            Debug.LogWarning("No HoldPoint was found inside the player.");
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
        player.IsInteracting = true;
        Vector3 tempDir = new Vector3(transform.position.x - currentTriggerTransform.position.x, 0, transform.position.z - currentTriggerTransform.position.z).normalized;
        player.SetPlayerPositionAndDirection(currentTriggerTransform.position, tempDir);
    }

    private void Release()
    {
        if (player == null) return;
        player.IsInteracting = false;
        isGrabbed = false;
        rb.mass = 1000;
    }

    // Same effect as Release, but survives a null Player. Used when the box is torn out of the
    // player's hands from the outside (e.g. it snapped into its basket): the player reference may
    // already be gone, and we still need isGrabbed / mass in a sane state.
    private void ForceRelease()
    {
        if (player != null) player.IsInteracting = false;
        isGrabbed = false;
        if (rb != null) rb.mass = 1000;
    }

    /// <summary>
    /// Called by <see cref="BasketTrigger"/> when THIS box has just entered the basket it is meant
    /// for. Detaches the player from any push interaction, disables further grabs, freezes physics
    /// input, and slides the box to (target.x, currentY, target.z) over <see cref="snapDuration"/>.
    /// Y is never touched. Safe to call more than once — subsequent calls are ignored.
    /// </summary>
    public void LockAtBasket(Transform target)
    {
        if (locked) return;
        if (target == null)
        {
            Debug.LogWarning($"[{nameof(GrabbableBall)}] '{name}' LockAtBasket called with a null " +
                             "target. Ignoring.", this);
            return;
        }

        if (isGrabbed) ForceRelease();

        locked = true;

        // Freeze physics so no residual push or collision can drift the box off-centre while the
        // tween runs, and so the player cannot bump into it any more.
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // Kill the child trigger that lets the player latch on. Without this, walking near the
        // locked box would still flip PlayerNearby and light up the "E" prompt.
        foreach (PushBoxTriggerLogic t in GetComponentsInChildren<PushBoxTriggerLogic>(true))
            t.enabled = false;

        Vector3 from = transform.position;
        Vector3 to = new Vector3(target.position.x, from.y, target.position.z);

        if (snapDuration <= 0f)
        {
            transform.position = to;
            return;
        }

        LeanTween.move(gameObject, to, snapDuration).setEaseOutCubic();
    }
}
