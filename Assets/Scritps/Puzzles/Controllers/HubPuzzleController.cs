using UnityEngine;

public class HubPuzzleController : MonoBehaviour
{
    [SerializeField] private SO_HubPuzzleData hubData;

    public string PuzzleId => hubData != null ? hubData.PuzzleId : string.Empty;


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

        // ACA DESPUES:
        // - reproducir cinemática
        // - abrir acceso Piso 3
        // - activar ascensor / puerta final
    }
}
