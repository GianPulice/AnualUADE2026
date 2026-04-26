using UnityEngine;

public class SocketInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private SO_SocketData socketData;

    public bool IsInserted =>
        socketData != null && PuzzleStateManager.Instance.IsSocketInserted(socketData.SocketId);

    public string SocketId => socketData != null ? socketData.SocketId : string.Empty;
    public string LinkedPuzzleId => socketData != null ? socketData.LinkedPuzzleId : string.Empty;

    public string GetInteractText()
    {
        if (socketData == null) return "Socket sin configurar";
        if (IsInserted) return "Ya insertado";

        return socketData.GetPromptText();
    }

    public bool CanInteract()
    {
        if (socketData == null) return false;
        if (IsInserted) return false;
        if (socketData.RequiredItem == null) return false;

        return InventoryManager.Instance.HasItem(socketData.RequiredItem);
    }

    public void Interact()
    {
        if (!CanInteract()) return;

        if (socketData.ConsumeItem)
            InventoryManager.Instance.ConsumeItem(socketData.RequiredItem);

        PuzzleStateManager.Instance.SetSocketInserted(socketData.SocketId);

        if (!string.IsNullOrWhiteSpace(socketData.LinkedPuzzleId))
        {
            PuzzleController[] puzzleControllers = FindObjectsByType<PuzzleController>(FindObjectsInactive.Exclude);

            foreach (PuzzleController controller in puzzleControllers)
            {
                if (controller.PuzzleId == socketData.LinkedPuzzleId)
                {
                    controller.StartPuzzle();
                    break;
                }
            }
        }

        Debug.Log($"Socket insertado: {socketData.SocketId}");
    }
    public bool IsRepeatable()
    {
        return false;
    }
}
