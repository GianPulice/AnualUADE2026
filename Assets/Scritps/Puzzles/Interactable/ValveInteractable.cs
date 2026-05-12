using System.Collections;
using UnityEngine;

public class ValveInteractable : BaseRangeInteractable
{
    [SerializeField] private SO_ValveData valveData;

    public string ValveId => valveData != null ? valveData.ValveId : string.Empty;
    public string LinkedPuzzleId => valveData != null ? valveData.LinkedPuzzleId : string.Empty;

    public int CurrentPosition
    {
        get
        {
            if (valveData == null) return 0;
            return PuzzleStateManager.Instance.GetValvePosition(
                valveData.ValveId,
                valveData.InitialPosition
            );
        }
    }

    protected override void Awake()
    {
        base.Awake();

        if (valveData == null)
        {
            Debug.LogError($"ValveInteractable sin SO_ValveData en {gameObject.name}");
            return;
        }

        StartCoroutine(InitializeValveState());
    }

    private IEnumerator InitializeValveState()
    {
        yield return new WaitForSeconds(3);

        PuzzleStateManager.Instance.SetValvePosition(
            valveData.ValveId,
            CurrentPosition
        );
    }

    public override string GetInteractText()
    {
        if (valveData == null) return "Válvula sin configurar";
        return valveData.PromptText;
    }

    protected override bool CanInteractInCloseRange()
    {
        if (valveData == null) return false;

        if (!string.IsNullOrWhiteSpace(valveData.LinkedPuzzleId) &&
            PuzzleStateManager.Instance.IsPuzzleCompleted(valveData.LinkedPuzzleId))
            return false;

        return true;
    }

    protected override void OnInteract()
    {
        int nextPosition = CurrentPosition + 1;

        if (nextPosition >= valveData.MaxPositions)
            nextPosition = 0;

        PuzzleStateManager.Instance.SetValvePosition(
            valveData.ValveId,
            nextPosition
        );

        ValvePuzzleController[] controllers =
            FindObjectsByType<ValvePuzzleController>(FindObjectsInactive.Exclude);

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

    public override bool IsRepeatable()
    {
        return false;
    }
}

