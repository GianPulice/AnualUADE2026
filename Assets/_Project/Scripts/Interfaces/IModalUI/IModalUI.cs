/// <summary>
/// The key that closes a modal. One per modal, so two keys never fight over the same screen:
/// every gameplay UI closes with TAB, and ESC belongs to the pause menu (and to Settings, which
/// is part of it). Stored nowhere: each modal returns its own value.
/// </summary>
public enum ModalCloseKey
{
    /// <summary>No key closes it (result screens, cinematics). It closes through its own buttons or code.</summary>
    None = 0,

    /// <summary>UI/Exit (ESC, gamepad Start / B). Only the pause menu and Settings.</summary>
    Escape = 1,

    /// <summary>UI/Close (TAB, gamepad Select / B). Every other UI. ESC over it opens the pause menu.</summary>
    Tab = 2,
}

/// <summary>
/// Any modal UI that must block gameplay (inventory, sequence panel, document reader,
/// settings, pause menu...) implements this interface and registers itself in the
/// UIStateManager when it opens.
///
/// The UIStateManager takes care of Time.timeScale, the cursor, and deciding which modal the
/// close keys reach (see <see cref="CloseKey"/>).
/// </summary>
public interface IModalUI
{
    /// <summary>Unique identifier of the modal. Used for logs and to avoid double Pushes.</summary>
    string ModalId { get; }

    /// <summary>
    /// Which key closes this modal while it is on top. The UIStateManager calls
    /// <see cref="RequestClose"/> when that key is pressed and ignores the other one: ESC over a
    /// <see cref="ModalCloseKey.Tab"/> modal opens the pause menu on top instead (unless
    /// <see cref="BlocksPause"/>).
    /// </summary>
    ModalCloseKey CloseKey { get; }

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
