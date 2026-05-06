using System.Collections;
using UnityEngine;

public class ValveInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private SO_ValveData valveData;

    public string ValveId => valveData != null ? valveData.ValveId : string.Empty;
    public string LinkedPuzzleId => valveData != null ? valveData.LinkedPuzzleId : string.Empty;

    public int CurrentPosition
    {
        get
        {
            if (valveData == null) return 0;
            return PuzzleStateManager.Instance.GetValvePosition(valveData.ValveId, valveData.InitialPosition);
        }
    }

    // Cambiarlo a Awake cuando se haga la escena Data
    private void Awake()
    {
        if (valveData == null)
        {
            Debug.LogError($"ValveInteractable sin SO_ValveData en {gameObject.name}");
            return;
        }

        StartCoroutine(wait());

        //PuzzleStateManager.Instance.SetValvePosition(valveData.ValveId, CurrentPosition);
    }

    private IEnumerator wait()
    {
        yield return new WaitForSeconds(3);

        PuzzleStateManager.Instance.SetValvePosition(valveData.ValveId, CurrentPosition);

    }


    public string GetInteractText()
    {
        if (valveData == null) return "Válvula sin configurar";
        return valveData.PromptText;
    }

    public bool CanInteract()
    {
        if (valveData == null) return false;

        if (!string.IsNullOrWhiteSpace(valveData.LinkedPuzzleId) &&
            PuzzleStateManager.Instance.IsPuzzleCompleted(valveData.LinkedPuzzleId))
            return false;

        return true;
    }

    public void Interact()
    {
        if (!CanInteract()) return;

        int nextPosition = CurrentPosition + 1;

        if (nextPosition >= valveData.MaxPositions)
            nextPosition = 0;

        PuzzleStateManager.Instance.SetValvePosition(valveData.ValveId, nextPosition);

        ValvePuzzleController[] controllers = FindObjectsByType<ValvePuzzleController>(FindObjectsInactive.Exclude);

        foreach (ValvePuzzleController controller in controllers)
        {
            if (controller.PuzzleId == valveData.LinkedPuzzleId)
            {
                controller.CheckValves();
                break;
            }
        }

        Debug.Log($"Válvula {valveData.ValveId} en posición {nextPosition}");
    }
    public bool IsRepeatable()
    {
        return false;
    }
}

