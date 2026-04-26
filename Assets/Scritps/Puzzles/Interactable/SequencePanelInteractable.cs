using System.Collections.Generic;
using UnityEngine;

public class SequencePanelInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private SO_SequencePuzzleData sequenceData;

    private readonly List<int> currentSequence = new List<int>();

    public string GetInteractText()
    {
        if (sequenceData == null) return "Panel sin configurar";
        return sequenceData.PromptText;
    }

    public bool CanInteract()
    {
        if (sequenceData == null) return false;

        if (PuzzleStateManager.Instance.IsPuzzleCompleted(sequenceData.PuzzleId))
            return false;

        if (!string.IsNullOrWhiteSpace(sequenceData.RequiredSocketId) &&
            !PuzzleStateManager.Instance.IsSocketInserted(sequenceData.RequiredSocketId))
            return false;

        return true;
    }

    public void Interact()
    {
        if (!CanInteract()) return;

        Debug.Log("Panel listo. Llamar PressButton(int buttonId) desde botones/UI.");
    }

    public void PressButton(int buttonId)
    {
        if (!CanInteract()) return;

        currentSequence.Add(buttonId);

        IReadOnlyList<int> correctSequence = sequenceData.CorrectSequence;

        for (int i = 0; i < currentSequence.Count; i++)
        {
            if (i >= correctSequence.Count || currentSequence[i] != correctSequence[i])
            {
                ResetSequence();
                Debug.Log("Secuencia incorrecta.");
                return;
            }
        }

        if (currentSequence.Count == correctSequence.Count)
        {
            CompleteSequencePuzzle();
        }
    }

    private void CompleteSequencePuzzle()
    {
        PuzzleStateManager.Instance.SetPuzzleCompleted(sequenceData.PuzzleId);

        if (sequenceData.RewardItem != null)
            InventoryManager.Instance.AddItem(sequenceData.RewardItem);

        Debug.Log($"Puzzle de secuencia completado: {sequenceData.PuzzleId}");
    }

    private void ResetSequence()
    {
        currentSequence.Clear();
    }
}
