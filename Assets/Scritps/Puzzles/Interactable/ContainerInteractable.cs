using UnityEngine;

public class ContainerInteractable : BaseRangeInteractable
{
    [SerializeField] private SO_ContainerData containerData;
    [SerializeField] private ContainerSlot[] possibleSlots;

    private int currentSlotIndex;

    public string ContainerId => containerData != null ? containerData.ContainerId : string.Empty;
    public string LinkedPuzzleId => containerData != null ? containerData.LinkedPuzzleId : string.Empty;


    protected override void Awake()
    {
        base.Awake();

        if (containerData == null)
        {
            Debug.LogError($"ContainerInteractable without SO_ContainerData on {gameObject.name}");
            return;
        }

        currentSlotIndex = FindInitialSlotIndex();
        ApplySlotPosition();

        PuzzleStateManager.Instance.SetContainerSlot(
            containerData.ContainerId,
            possibleSlots[currentSlotIndex].SlotId
        );
    }

    public override string GetInteractText()
    {
        if (containerData == null) return "Unconfigured container";
        return containerData.PromptText;
    }

    protected override bool CanInteractInCloseRange()
    {
        if (containerData == null) return false;

        if (!string.IsNullOrWhiteSpace(containerData.LinkedPuzzleId) &&
            PuzzleStateManager.Instance.IsPuzzleCompleted(containerData.LinkedPuzzleId))
            return false;

        return possibleSlots != null && possibleSlots.Length > 0;
    }

    protected override void OnInteract()
    {
        currentSlotIndex++;

        if (currentSlotIndex >= possibleSlots.Length)
            currentSlotIndex = 0;

        ApplySlotPosition();

        PuzzleStateManager.Instance.SetContainerSlot(
            containerData.ContainerId,
            possibleSlots[currentSlotIndex].SlotId
        );

        NotifyPuzzleController();

        Debug.Log($"Container {containerData.ContainerId} moved to slot {possibleSlots[currentSlotIndex].SlotId}");
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
        ContainerPuzzleController[] controllers =
            FindObjectsByType<ContainerPuzzleController>(FindObjectsInactive.Exclude);

        foreach (ContainerPuzzleController controller in controllers)
        {
            if (controller.PuzzleId == containerData.LinkedPuzzleId)
            {
                controller.CheckContainers();
                return;
            }
        }
    }

    public override bool IsRepeatable()
    {
        return true;
    }
}
