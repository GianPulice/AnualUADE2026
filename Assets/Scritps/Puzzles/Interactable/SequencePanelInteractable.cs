using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Panel de secuencia (puzzle SP1). Hereda de BaseRangeInteractable para usar el mismo
/// flujo de deteccion por collider que el resto de interactuables.
/// Al interactuar, abre la UI del SequencePanelUIController. La logica del puzzle vive aca;
/// la UI es solo vista + input.
/// </summary>
public class SequencePanelInteractable : BaseRangeInteractable
{
    [Header("Datos del puzzle")]
    [SerializeField] private SO_SequencePuzzleData sequenceData;

    [Header("Configuracion del panel")]
    [Tooltip("Cantidad de botones que muestra la UI. Los IDs van de 1 a buttonCount.")]
    [SerializeField, Min(1)] private int buttonCount = 8;

    private readonly List<int> currentSequence = new List<int>();
    private bool isCompleted;

    public string PuzzleId => sequenceData != null ? sequenceData.PuzzleId : string.Empty;
    public int ButtonCount => buttonCount;
    public IReadOnlyList<int> CorrectSequence =>
        sequenceData != null ? sequenceData.CorrectSequence : new List<int>();
    public IReadOnlyList<int> EnteredSequence => currentSequence;
    public bool IsCompleted => isCompleted;

    /// <summary>Disparado cuando el jugador ingresa un boton (correcto o incorrecto).</summary>
    public event Action<int> OnButtonPressed;
    /// <summary>Disparado cuando la secuencia ingresada es incorrecta (resetea el input).</summary>
    public event Action OnSequenceFailed;
    /// <summary>Disparado cuando la secuencia se completa correctamente.</summary>
    public event Action OnSequenceCompleted;

    protected override void Awake()
    {
        base.Awake();

        if (sequenceData == null)
        {
            Debug.LogError($"SequencePanelInteractable sin SO_SequencePuzzleData en {gameObject.name}");
            return;
        }

        if (PuzzleStateManager.Instance != null &&
            PuzzleStateManager.Instance.IsPuzzleCompleted(sequenceData.PuzzleId))
        {
            isCompleted = true;
        }
    }

    public override string GetInteractText()
    {
        if (sequenceData == null) return "Panel sin configurar";
        if (isCompleted) return string.Empty;
        return sequenceData.PromptText;
    }

    public override string GetInfoText()
    {
        if (sequenceData == null || isCompleted) return string.Empty;
        if (!string.IsNullOrWhiteSpace(sequenceData.RequiredSocketId) &&
            (PuzzleStateManager.Instance == null ||
             !PuzzleStateManager.Instance.IsSocketInserted(sequenceData.RequiredSocketId)))
            return "Falta insertar el fusible";
        return string.Empty;
    }

    protected override bool CanInteractInCloseRange()
    {
        if (sequenceData == null) return false;
        if (isCompleted) return false;

        if (!string.IsNullOrWhiteSpace(sequenceData.RequiredSocketId) &&
            (PuzzleStateManager.Instance == null ||
             !PuzzleStateManager.Instance.IsSocketInserted(sequenceData.RequiredSocketId)))
        {
            return false;
        }

        return true;
    }

    protected override void OnInteract()
    {
        if (SequencePanelUIController.Instance == null)
        {
            Debug.LogError("[SequencePanelInteractable] No hay SequencePanelUIController en escena (LevelUI).");
            return;
        }

        SequencePanelUIController.Instance.Open(this);
    }

    public override bool IsRepeatable()
    {
        return !isCompleted;
    }

    /// <summary>
    /// Llamado desde la UI cuando el jugador presiona un boton del panel.
    /// Devuelve true si el boton es correcto en la posicion actual.
    /// </summary>
    public bool TryPressButton(int buttonId)
    {
        if (isCompleted) return false;
        if (sequenceData == null) return false;

        IReadOnlyList<int> correct = sequenceData.CorrectSequence;
        int step = currentSequence.Count;

        if (step >= correct.Count) return false;

        if (correct[step] != buttonId)
        {
            currentSequence.Clear();
            OnButtonPressed?.Invoke(buttonId);
            OnSequenceFailed?.Invoke();
            return false;
        }

        currentSequence.Add(buttonId);
        OnButtonPressed?.Invoke(buttonId);

        if (currentSequence.Count == correct.Count)
        {
            CompleteSequence();
        }

        return true;
    }

    public void ResetSequence()
    {
        if (currentSequence.Count == 0) return;
        currentSequence.Clear();
    }

    private void CompleteSequence()
    {
        isCompleted = true;

        if (PuzzleStateManager.Instance != null)
            PuzzleStateManager.Instance.SetPuzzleCompleted(sequenceData.PuzzleId);

        if (sequenceData.RewardItem != null && InventoryManager.Instance != null)
            InventoryManager.Instance.AddItem(sequenceData.RewardItem);

        OnSequenceCompleted?.Invoke();

        Debug.Log($"[SequencePanel] Puzzle completado: {sequenceData.PuzzleId}");
    }
}
