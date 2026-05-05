using UnityEngine;

public class ContainerInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private SO_ContainerData containerData;
    [SerializeField] private ContainerSlot[] possibleSlots;

    private int currentSlotIndex;

    public string ContainerId => containerData != null ? containerData.ContainerId : string.Empty;
    public string LinkedPuzzleId => containerData != null ? containerData.LinkedPuzzleId : string.Empty;

    private void Awake()
    {
        if (containerData == null)
        {
            Debug.LogError($"ContainerInteractable sin SO_ContainerData en {gameObject.name}");
            return;
        }

        currentSlotIndex = FindInitialSlotIndex();
        ApplySlotPosition();

        PuzzleStateManager.Instance.SetContainerSlot(
            containerData.ContainerId,
            possibleSlots[currentSlotIndex].SlotId
        );
    }

    public string GetInteractText()
    {
        if (containerData == null) return "Contenedor sin configurar";
        return containerData.PromptText;
    }

    public bool CanInteract()
    {
        if (containerData == null) return false;

        if (!string.IsNullOrWhiteSpace(containerData.LinkedPuzzleId) &&
            PuzzleStateManager.Instance.IsPuzzleCompleted(containerData.LinkedPuzzleId))
            return false;

        return possibleSlots != null && possibleSlots.Length > 0;
    }

    public void Interact()
    {
        if (!CanInteract()) return;

        currentSlotIndex++;

        if (currentSlotIndex >= possibleSlots.Length)
            currentSlotIndex = 0;

        ApplySlotPosition();

        PuzzleStateManager.Instance.SetContainerSlot(
            containerData.ContainerId,
            possibleSlots[currentSlotIndex].SlotId
        );

        NotifyPuzzleController();

        Debug.Log($"Contenedor {containerData.ContainerId} movido a slot {possibleSlots[currentSlotIndex].SlotId}");
    }

    private int FindInitialSlotIndex()
    {
        if (possibleSlots == null || possibleSlots.Length == 0)
            return 0;

        for (int i = 0; i < possibleSlots.Length; i++)
        {
            if (possibleSlots[i] != null && possibleSlots[i].SlotId == containerData.InitialSlotId)
                return i;
        }

        return 0;
    }

    private void ApplySlotPosition()
    {
        if (possibleSlots == null || possibleSlots.Length == 0) return;
        if (possibleSlots[currentSlotIndex] == null) return;

        transform.position = possibleSlots[currentSlotIndex].transform.position;
        transform.rotation = possibleSlots[currentSlotIndex].transform.rotation;
    }

    private void NotifyPuzzleController()
    {
        ContainerPuzzleController[] controllers = FindObjectsByType<ContainerPuzzleController>(FindObjectsInactive.Exclude);

        foreach (ContainerPuzzleController controller in controllers)
        {
            if (controller.PuzzleId == containerData.LinkedPuzzleId)
            {
                controller.CheckContainers();
                return;
            }
        }
    }

    public bool IsRepeatable()
    {
        return true;
    }
}
