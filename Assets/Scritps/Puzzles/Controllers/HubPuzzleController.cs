using UnityEngine;

public class HubPuzzleController : MonoBehaviour
{
    [SerializeField] private SO_HubPuzzleData hubData;

    private void Update()
    {
        CheckHubCompletion();
    }

    public void CheckHubCompletion()
    {
        if (hubData == null) return;

        if (PuzzleStateManager.Instance.IsPuzzleCompleted(hubData.PuzzleId))
            return;

        foreach (string socketId in hubData.RequiredSocketIds)
        {
            if (!PuzzleStateManager.Instance.IsSocketInserted(socketId))
                return;
        }

        PuzzleStateManager.Instance.SetPuzzleCompleted(hubData.PuzzleId);

        Debug.Log($"Hub completado: {hubData.PuzzleId}");

        // Aca se conecta con cinemática / desbloqueo piso 3 desde otro sistema mas adelante cuando avancemos
    }
}
