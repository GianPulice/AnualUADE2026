using UnityEngine;

public class SocketInteractable : BaseRangeInteractable
{
    [SerializeField] private SO_SocketData socketData;

    public bool IsInserted =>
        socketData != null && PuzzleStateManager.Instance.IsSocketInserted(socketData.SocketId);

    public string SocketId => socketData != null ? socketData.SocketId : string.Empty;
    public string LinkedPuzzleId => socketData != null ? socketData.LinkedPuzzleId : string.Empty;

    public override string GetInteractText()
    {
        if (socketData == null || socketData.RequiredItem == null) return string.Empty;
        if (IsInserted) return $"{socketData.RequiredItem.ItemName} inserted";
        return socketData.GetPromptText();
    }

    public override string GetInfoText()
    {
        if (socketData == null || socketData.RequiredItem == null) return string.Empty;
        if (IsInserted) return string.Empty;
        if (!InventoryManager.Instance.HasItem(socketData.RequiredItem))
            return $"You need {socketData.RequiredItem.ItemName}";
        return string.Empty;
    }

    protected override bool CanInteractInCloseRange()
    {
        if (socketData == null) return false;
        if (IsInserted) return false;
        if (socketData.RequiredItem == null) return false;

        return InventoryManager.Instance.HasItem(socketData.RequiredItem);
    }

    protected override void OnInteract()
    {
        if (socketData.ConsumeItem)
            InventoryManager.Instance.ConsumeItem(socketData.RequiredItem);

        PuzzleStateManager.Instance.SetSocketInserted(socketData.SocketId);

        NotifyLinkedPuzzle();

        Debug.Log($"Socket inserted: {socketData.SocketId}");
    }

    private void NotifyLinkedPuzzle()
    {
        if (socketData == null) return;
        if (string.IsNullOrWhiteSpace(socketData.LinkedPuzzleId)) return;

        HubPuzzleController[] hubs =
            FindObjectsByType<HubPuzzleController>(FindObjectsInactive.Exclude);

        foreach (HubPuzzleController hub in hubs)
        {
            if (hub.PuzzleId == socketData.LinkedPuzzleId)
            {
                hub.CheckHubCompletion();
                return;
            }
        }

        PuzzleController[] puzzleControllers =
            FindObjectsByType<PuzzleController>(FindObjectsInactive.Exclude);

        foreach (PuzzleController controller in puzzleControllers)
        {
            if (controller.PuzzleId == socketData.LinkedPuzzleId)
            {
                controller.StartPuzzle();
                return;
            }
        }
    }

    public override bool IsRepeatable()
    {
        return false;
    }
}
