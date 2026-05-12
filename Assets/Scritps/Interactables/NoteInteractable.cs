using UnityEngine;

public class NoteInteractable : BaseRangeInteractable
{
    [TextArea]
    [SerializeField] private string noteText;


    public override string GetInteractText()
    {
        return "Leer nota";
    }

    protected override bool CanInteractInCloseRange()
    {
        return true;
    }

    protected override void OnInteract()
    {
        Debug.Log(noteText);
    }

    public override bool IsRepeatable()
    {
        return false;
    }
}
