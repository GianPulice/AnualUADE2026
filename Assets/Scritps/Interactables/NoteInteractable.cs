using UnityEngine;

public class NoteInteractable : BaseRangeInteractable
{
    [SerializeField] private SO_DocumentData documentData;

    public override string GetInteractText() => "Read";

    protected override bool CanInteractInCloseRange() => documentData != null;

    protected override void OnInteract()
    {
        if (DocumentReaderController.Instance == null)
        {
            Debug.LogError("[NoteInteractable] There is no DocumentReaderController in the scene (LevelUI).");
            return;
        }

        if (AudioManager.Exists)
            AudioManager.Instance.PlaySFX("sfx_interaction_nota", transform.position);

        DocumentReaderController.Instance.Open(documentData);
    }

    public override bool IsRepeatable() => true;
}
