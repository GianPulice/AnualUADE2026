using System;
using System.Collections;
using UnityEngine;

public class SocketInteractable : BaseRangeInteractable, IPromptPresentation, IPuzzleInteractable
{
    [SerializeField] private SO_SocketData socketData;

    [Header("Inserted Visual")]
    [Tooltip("Optional GameObject shown once the item is inserted (leave empty on variants " +
             "that do not have a 3D model yet). Should start inactive in the prefab.")]
    [SerializeField] private GameObject insertedVisual;

    [Header("Audio")]
    [Tooltip("Id of the SO_SoundData to play when the item is inserted (must be registered in " +
             "AudioManager.sounds). Leave empty to skip audio on this socket.")]
    [SoundId]
    [SerializeField] private string insertSoundId = string.Empty;

    /// <summary>The player just put the item in (animate). Not raised by a load or a rollback.</summary>
    public event Action Inserted;

    /// <summary>
    /// The saved state was re-read a few seconds after load (see SyncInsertedVisual): true when the
    /// socket is already filled. Snap to the matching look, no animation.
    /// </summary>
    public event Action<bool> InsertedStateSynced;

    public bool IsInserted =>
        socketData != null && PuzzleStateManager.Exists &&
        PuzzleStateManager.Instance.IsSocketInserted(socketData.SocketId);

    public string SocketId => socketData != null ? socketData.SocketId : string.Empty;
    public string LinkedPuzzleId => socketData != null ? socketData.LinkedPuzzleId : string.Empty;

    // -- IPromptPresentation -------------------
    // Inserting is an item interaction too, so the prompt shows the item the socket is asking
    // for. That icon is the answer to "which one of the three do I need here".
    public InteractionPromptKind Kind => InteractionPromptKind.Item;
    public Sprite PromptIcon =>
        socketData != null && socketData.RequiredItem != null ? socketData.RequiredItem.ItemIcon : null;

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

    /// <summary>A filled socket has nothing more to take.</summary>
    public override bool IsFinished() => IsInserted;

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

        if (!string.IsNullOrEmpty(insertSoundId) && AudioManager.Exists)
            AudioManager.Instance.PlaySFX(insertSoundId, transform.position);

        Inserted?.Invoke();

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
        StartCoroutine(SyncInsertedVisual());
    }

    private IEnumerator SyncInsertedVisual()
    {
        yield return new WaitForSeconds(3);
        if (insertedVisual != null)
            insertedVisual.SetActive(IsInserted);
        InsertedStateSynced?.Invoke(IsInserted);
    }
}
