using UnityEngine;

public class HubPuzzleController : MonoBehaviour
{
    [SerializeField] private SO_HubPuzzleData hubData;

    public string PuzzleId => hubData != null ? hubData.PuzzleId : string.Empty;


    public void CheckHubCompletion()
    {
        if (hubData == null) return;

        // One guard for the three call sites below, completion write included.
        if (!PuzzleStateManager.Exists) return;

        if (PuzzleStateManager.Instance.IsPuzzleCompleted(hubData.PuzzleId))
            return;

        foreach (string socketId in hubData.RequiredSocketIds)
        {
            if (!PuzzleStateManager.Instance.IsSocketInserted(socketId))
                return;
        }

        PuzzleStateManager.Instance.SetPuzzleCompleted(hubData.PuzzleId);

        Debug.Log($"Hub completed: {hubData.PuzzleId}");

        // TO DO HERE:
        // - play the cinematic
        // - open access to Floor 3
        // - activate the elevator / final door
    }
}
