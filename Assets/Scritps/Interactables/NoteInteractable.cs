using UnityEngine;

public class NoteInteractable : MonoBehaviour, IInteractable
{
    [TextArea]
    [SerializeField] private string noteText;


    public string GetInteractText()
    {
        return "Leer nota";
    }

    public bool CanInteract()
    {
        return true;
    }

    public void Interact()
    {
        Debug.Log(noteText);
    }
    public bool IsRepeatable()
    {
        return false;
    }
}
