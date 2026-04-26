using UnityEngine;

public class PickupInteractable : MonoBehaviour, IInteractable
{
    [Header("Item")]
    [SerializeField] private SO_InventoryItem itemToPick;

    [Header("UI")]
    [SerializeField] private GameObject imageFar;
    [SerializeField] private GameObject imageClose;

    [Header("Detection Colliders")]
    [SerializeField] private SphereCollider farTrigger;
    [SerializeField] private SphereCollider closeTrigger;

    [Header("Trigger Radius")]
    [SerializeField] private float farRadius = 3f;
    [SerializeField] private float closeRadius = 1.5f;

    private bool playerInFarRange;
    private bool playerInCloseRange;

    private void Start()
    {
        imageFar.SetActive(false);
        imageClose.SetActive(false);
        ApplyColliderSizes();
    }
    private void OnValidate()
    {
        ApplyColliderSizes();
    }
    private void ApplyColliderSizes()
    {
        if (farTrigger != null)
        {
            farTrigger.isTrigger = true;
            farTrigger.radius = farRadius;
        }

        if (closeTrigger != null)
        {
            closeTrigger.isTrigger = true;
            closeTrigger.radius = closeRadius;
        }
    }
    public string GetInteractText()
    {
        return itemToPick != null ? $"Recoger {itemToPick.ItemName}" : "Recoger";
    }

    public bool CanInteract()
    {
        return itemToPick != null;
    }

    public void Interact()
    {
        
        InventoryManager.Instance.AddItem(itemToPick);
        Destroy(gameObject);
    }
    private void UpdateUI()
    {
        imageFar.SetActive(playerInFarRange && !playerInCloseRange);
        imageClose.SetActive(playerInCloseRange);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        if (other == farTrigger) return;
        if (other == closeTrigger) return;

        UpdateZones(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        UpdateZones(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        playerInFarRange = false;
        playerInCloseRange = false;

        UpdateUI();
    }

    private void UpdateZones(Collider player)
    {
        playerInFarRange = farTrigger.bounds.Intersects(player.bounds);
        playerInCloseRange = closeTrigger.bounds.Intersects(player.bounds);

        UpdateUI();
    }

    public void SetInteractionRadius(float newFar, float newClose)
    {
        farRadius = newFar;
        closeRadius = newClose;

        ApplyColliderSizes();
    }
    public bool IsRepeatable()
    {
        return false;
    }
}
