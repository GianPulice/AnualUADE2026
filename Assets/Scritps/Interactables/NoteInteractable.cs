using UnityEngine;

public class NoteInteractable : BaseRangeInteractable
{
    [SerializeField] private SO_DocumentData documentData;

    public override string GetInteractText() => "Leer";

    protected override bool CanInteractInCloseRange() => documentData != null;

    protected override void OnInteract()
    {
        if (DocumentReaderController.Instance == null)
        {
            Debug.LogError("[NoteInteractable] No hay DocumentReaderController en escena (LevelUI).");
            return;
        }
        DocumentReaderController.Instance.Open(documentData);
    }

    public override bool IsRepeatable() => true;
}
