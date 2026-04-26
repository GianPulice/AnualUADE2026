using UnityEngine;

public class DoorInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private SO_DoorData doorData;

    private bool isOpen;

    private void Awake()
    {
        if (doorData == null)
        {
            Debug.LogError($"DoorInteractable sin SO_DoorData en {gameObject.name}");
            return;
        }

        isOpen = PuzzleStateManager.Instance != null &&
                 PuzzleStateManager.Instance.IsDoorOpened(doorData.DoorId);

        ApplyVisualState();
    }

    public string GetInteractText()
    {
        if (doorData == null) return "Puerta sin configurar";

        if (isOpen) return string.Empty;

        if (doorData.RequiredKey != null)
            return $"Abrir con {doorData.RequiredKey.ItemName}";

        return doorData.OpenPrompt;
    }

    public bool CanInteract()
    {
        if (doorData == null) return false;
        if (isOpen) return false;

        if (doorData.RequiredKey != null &&
            !InventoryManager.Instance.HasItem(doorData.RequiredKey))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(doorData.RequiredCompletedPuzzleId) &&
            !PuzzleStateManager.Instance.IsPuzzleCompleted(doorData.RequiredCompletedPuzzleId))
        {
            return false;
        }

        return true;
    }

    public void Interact()
    {
        if (!CanInteract()) return;

        OpenDoor();
    }

    public void OpenDoor()
    {
        if (doorData == null) return;
        if (isOpen) return;

        isOpen = true;

        if (doorData.ConsumeKey && doorData.RequiredKey != null)
            InventoryManager.Instance.RemoveItem(doorData.RequiredKey);

        PuzzleStateManager.Instance.SetDoorOpened(doorData.DoorId);

        ApplyVisualState();

        Debug.Log($"Puerta abierta: {doorData.DoorId}");
    }

    private void ApplyVisualState()
    {
        if (!isOpen) return;

        transform.rotation = Quaternion.Euler(transform.eulerAngles.x, transform.eulerAngles.y + 90f, transform.eulerAngles.z);

        Collider collider = GetComponent<Collider>();
        if (collider != null)
            collider.enabled = false;
    }
}
