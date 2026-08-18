using System.Collections;
using UnityEngine;

public class SocketInteractable : BaseRangeInteractable
{
    [SerializeField] private SO_SocketData socketData;

    [Header("Inserted Visual")]
    [Tooltip("Optional GameObject shown once the item is inserted (leave empty on variants " +
             "that do not have a 3D model yet). Should start inactive in the prefab.")]
    [SerializeField] private GameObject insertedVisual;

    public bool IsInserted =>
        socketData != null && PuzzleStateManager.Exists &&
        PuzzleStateManager.Instance.IsSocketInserted(socketData.SocketId);

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
        if (!InventoryManager.Exists) return string.Empty;
        if (!InventoryManager.Instance.HasItem(socketData.RequiredItem))
            return $"You need {socketData.RequiredItem.ItemName}";
        return string.Empty;
    }

    protected override bool CanInteractInCloseRange()
    {
        if (socketData == null) return false;
        if (IsInserted) return false;
        if (socketData.RequiredItem == null) return false;

        return InventoryManager.Exists && InventoryManager.Instance.HasItem(socketData.RequiredItem);
    }

    protected override void OnInteract()
    {
        if (!PuzzleStateManager.Exists)
        {
            // Recording the insert is the point of the interaction — without it the item would be
            // consumed for nothing and the linked puzzle would still read the socket as empty.
            Debug.LogWarning($"[{nameof(SocketInteractable)}] No PuzzleStateManager — inserting " +
                             $"into socket '{socketData.SocketId}' had no effect.", this);
            return;
        }

        if (socketData.ConsumeItem && InventoryManager.Exists)
            InventoryManager.Instance.ConsumeItem(socketData.RequiredItem);

        PuzzleStateManager.Instance.SetSocketInserted(socketData.SocketId);

        if (insertedVisual != null)
            insertedVisual.SetActive(true);

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


protected override void Awake()
    {
        base.Awake();
        if (insertedVisual != null)
            StartCoroutine(SyncInsertedVisual());
    }

    private IEnumerator SyncInsertedVisual()
    {
        yield return new WaitForSeconds(3);
        if (insertedVisual != null)
            insertedVisual.SetActive(IsInserted);
    }
}
