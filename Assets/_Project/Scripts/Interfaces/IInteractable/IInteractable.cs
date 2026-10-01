using UnityEngine;

public interface IInteractable
{
    string GetPromptText();
    string GetInfoText();
    bool CanInteract();
    void Interact();
    bool IsRepeatable();

    /// <summary>
    /// True once there is nothing left to do with this interactable, ever: the socket is filled,
    /// the puzzle it belongs to is solved, the box sits locked in its basket. Not the same as
    /// <see cref="CanInteract"/> being false, which also covers "not yet" — a locked door, a socket
    /// whose item the player has not found, a box out of reach — and those still answer the
    /// crosshair. The highlight reads this to stop lighting up props that are done.
    /// </summary>
    bool IsFinished();
}
