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

            // No manager: fall back to the configured starting position, which is the same answer
            // it would give for a valve nobody has turned yet.
            if (!PuzzleStateManager.Exists) return valveData.InitialPosition;

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
            Debug.LogError($"ValveInteractable without SO_ValveData on {gameObject.name}");
            return;
        }

        StartCoroutine(InitializeValveState());
    }

    private IEnumerator InitializeValveState()
    {
        yield return new WaitForSeconds(3);

        if (!PuzzleStateManager.Exists)
        {
            Debug.LogWarning($"[{nameof(ValveInteractable)}] No PuzzleStateManager — the starting " +
                             $"position of valve '{valveData.ValveId}' was not published.", this);
            yield break;
        }

        PuzzleStateManager.Instance.SetValvePosition(
            valveData.ValveId,
            CurrentPosition
        );
    }

    public override string GetInteractText()
    {
        if (valveData == null) return "Unconfigured valve";
        return valveData.PromptText;
    }

    protected override bool CanInteractInCloseRange()
    {
        if (valveData == null) return false;

        if (!string.IsNullOrWhiteSpace(valveData.LinkedPuzzleId) &&
            PuzzleStateManager.Exists &&
            PuzzleStateManager.Instance.IsPuzzleCompleted(valveData.LinkedPuzzleId))
            return false;

        return true;
    }

    protected override void OnInteract()
    {
        int nextPosition = CurrentPosition + 1;

        if (nextPosition >= valveData.MaxPositions)
            nextPosition = 0;

        if (!PuzzleStateManager.Exists)
        {
            // Turning the valve is the whole interaction, so there is nothing left to do without
            // somewhere to store it — notifying the puzzle controller below would read the old
            // position back and never complete.
            Debug.LogWarning($"[{nameof(ValveInteractable)}] No PuzzleStateManager — turning " +
                             $"valve '{valveData.ValveId}' had no effect.", this);
            return;
        }

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

        Debug.Log($"Valve {valveData.ValveId} at position {nextPosition}");
    }

    public override bool IsRepeatable()
    {
        return true;
    }
}

