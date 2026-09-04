/// <summary>
/// Any modal UI that must block gameplay (inventory, sequence panel, document reader,
/// settings, pause menu...) implements this interface and registers itself in the
/// UIStateManager when it opens.
///
/// The UIStateManager takes care of Time.timeScale, the cursor, and deciding who receives
/// the cancel input (ESC).
/// </summary>
public interface IModalUI
{
    /// <summary>Unique identifier of the modal. Used for logs and to avoid double Pushes.</summary>
    string ModalId { get; }

    /// <summary>
    /// true  -> this modal absorbs ESC and uses it to close inner layers or itself.
    /// false -> ESC passes through and opens the pause menu on top (e.g. the sequence panel).
    /// </summary>
    bool ConsumesEscape { get; }

    /// <summary>
    /// true  -> while this modal is open, the PauseManager CANNOT open on top of it.
    /// false -> allows the pause menu to overlap.
    /// The pause menu itself should set this to true.
    /// </summary>
    bool BlocksPause { get; }

    /// <summary>
    /// true  -> while on the stack, UIStateManager sets Time.timeScale = 0.
    /// false -> does not touch timeScale (the game keeps running while the modal is open).
    /// </summary>
    bool PausesGame { get; }

    /// <summary>External close request (for example, from the UIStateManager when unstacking).</summary>
    void RequestClose();
}
