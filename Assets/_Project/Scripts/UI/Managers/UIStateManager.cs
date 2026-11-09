using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Centralized stack of modal UIs. Single source of truth for:
///   - Who has UI focus (Peek of the stack).
///   - Whether gameplay is blocked (IsAnyModalOpen).
///   - Time.timeScale (0 when there is >= 1 modal) and the cursor (free + visible with a modal).
///
/// How to add a new modal UI without breaking anything:
///   1) Implement IModalUI on your controller (declare CloseKey and BlocksPause).
///   2) Call UIStateManager.Instance.Push(this) on open and Pop(this) on close.
///   3) Remove any Time.timeScale and Cursor manipulation from your controller:
///      this manager governs them.
///   4) (Optional, Wave 2) Declare your UI's ActionMap name in your IModalUI so the
///      manager enables/disables Input System maps automatically.
///
/// The rest of the game queries:
///   - UIStateManager.IsAnyModalOpen  -> block interaction, movement, camera.
///   - UIStateManager.IsBlockingPause -> the PauseManager asks before pausing.
///   - UIStateManager.ClosedByKeyThisFrame -> a close key was spent this frame (do not reuse it).
///
/// The close keys: every gameplay UI closes with UI/Close (TAB), only the pause menu and Settings
/// with UI/Exit (ESC). Each modal says which in IModalUI.CloseKey, and the top modal is the only
/// one a key reaches. ESC over a TAB modal reaches nobody here, so the PauseManager opens the
/// pause menu on top.
/// </summary>
public class UIStateManager : Singleton<UIStateManager>, ISessionResettable
{
    public static event Action<IModalUI> OnModalPushed;
    public static event Action<IModalUI> OnModalPopped;

    [Header("Input")]
    [Tooltip("UI/Exit action of InputSystem_Actions (ESC). Fires RequestClose() on the top modal " +
             "whose CloseKey is Escape: the pause menu and Settings.")]
    [SerializeField] private InputActionReference exitAction;

    [Tooltip("Path of the action that closes every other UI (TAB), looked up in the project-wide " +
             "Input Actions. It is not a reference so no scene needs re-wiring.")]
    [SerializeField] private string closeActionPath = "UI/Close";

    private Action<InputAction.CallbackContext> exitHandler;
    private Action<InputAction.CallbackContext> closeHandler;
    private InputAction closeAction;
    private bool warnedNoCloseAction;

    // Frame in which a close key last closed something. Gamepad B is bound to both actions, and
    // the inventory reads the same TAB to open: one press closes one screen, never two.
    private int lastKeyCloseFrame = -1;

    private readonly Stack<IModalUI> stack = new Stack<IModalUI>();
    private int topPushedFrame = -1;   // frame in which the current top was pushed

    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private float previousTimeScale = 1f;
    private bool snapshotTaken;

    private void Awake()
    {
        CreateSingleton(true);
        GameSession.Register(this);
    }

    private void OnDestroy()
    {
        GameSession.Unregister(this);
    }

    /// <summary>
    /// <see cref="ISessionResettable"/> — dispatched by <see cref="GameSession.BeginNewSession"/>.
    ///
    /// A modal left on the stack by the previous run (its controller destroyed with the level
    /// before it could Pop) would keep IsAnyModalOpen true forever, and that alone blocks movement,
    /// camera and interaction in the next one. The stack and the snapshot are dropped without
    /// calling RequestClose: those modals belong to a level that is already gone. The cursor is
    /// left alone — the menu frees it and the PlayerCameraController locks it again. Time.timeScale
    /// is reset by GameSession itself.
    /// </summary>
    public void ResetForNewSession()
    {
        stack.Clear();
        topPushedFrame = -1;
        snapshotTaken = false;
    }

    private void OnEnable()
    {
        if (exitAction != null)
        {
            exitAction.action.Enable();
            exitHandler = _ => OnCloseKeyPressed(ModalCloseKey.Escape);
            exitAction.action.performed += exitHandler;
        }

        closeAction = InputSystem.actions != null ? InputSystem.actions.FindAction(closeActionPath) : null;
        if (closeAction != null)
        {
            closeAction.Enable();
            closeHandler = _ => OnCloseKeyPressed(ModalCloseKey.Tab);
            closeAction.performed += closeHandler;
        }
        else if (!warnedNoCloseAction)
        {
            warnedNoCloseAction = true;
            Debug.LogWarning($"[UIStateManager] No '{closeActionPath}' action in the project-wide Input " +
                             "Actions: the UIs that close with TAB cannot be closed by key.");
        }
    }

    private void OnDisable()
    {
        if (exitAction != null && exitHandler != null)
        {
            exitAction.action.performed -= exitHandler;
            exitHandler = null;
        }

        if (closeAction != null && closeHandler != null)
        {
            closeAction.performed -= closeHandler;
            closeHandler = null;
        }
    }

    /// <summary>
    /// The user pressed a close key: UI/Exit (ESC) or UI/Close (TAB). The top modal closes only
    /// if that is its <see cref="IModalUI.CloseKey"/>. Otherwise nothing happens here: an ESC over
    /// a TAB modal is the PauseManager's (Player/Pause, same key), which opens the pause on top.
    ///
    /// Ignored when the top modal was pushed in this SAME frame: Player/Pause and UI/Exit share
    /// ESC, and Player/Inventory and UI/Close share TAB, so the press that opened a modal would
    /// otherwise close it at once. Ignored too after a key already closed something this frame
    /// (gamepad B sits on both actions).
    /// </summary>
    private void OnCloseKeyPressed(ModalCloseKey key)
    {
        if (stack.Count == 0) return;
        if (ScreenManager.IsInputLocked) return;
        if (topPushedFrame == Time.frameCount) return;
        if (lastKeyCloseFrame == Time.frameCount) return;
        IModalUI top = stack.Peek();
        if (top == null) return;
        if (top.CloseKey != key) return;
        lastKeyCloseFrame = Time.frameCount;
        top.RequestClose();
    }

    // -- Queries -----------------------------------------------------------

    public bool IsAnyModalOpen => stack.Count > 0;
    public int ModalCount => stack.Count;

    /// <summary>True if the top modal declares that it blocks the pause menu from opening.</summary>
    public bool IsBlockingPause
    {
        get
        {
            if (stack.Count == 0) return false;
            foreach (IModalUI m in stack)
            {
                if (m != null && m.BlocksPause) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// True during the frame a close key closed a modal. Anything that opens a UI on one of those
    /// keys (the inventory on TAB) checks it, so the press is spent once.
    /// </summary>
    public bool ClosedByKeyThisFrame => lastKeyCloseFrame == Time.frameCount;

    public IModalUI Peek() => stack.Count > 0 ? stack.Peek() : null;

    /// <summary>
    /// True if at least one open modal has an id outside <paramref name="ignoredIds"/>. With an
    /// empty or null list it is exactly <see cref="IsAnyModalOpen"/>. Lets a HUD element stay on
    /// screen over a specific modal (the module timer over the skill check) and still hide for the
    /// rest.
    /// </summary>
    public bool IsAnyModalOpenExcept(IReadOnlyList<string> ignoredIds)
    {
        if (stack.Count == 0) return false;
        if (ignoredIds == null || ignoredIds.Count == 0) return true;

        foreach (IModalUI m in stack)
        {
            if (m == null) continue;
            bool ignored = false;
            for (int i = 0; i < ignoredIds.Count; i++)
            {
                if (ignoredIds[i] == m.ModalId) { ignored = true; break; }
            }
            if (!ignored) return true;
        }
        return false;
    }

    public bool Contains(IModalUI modal)
    {
        if (modal == null) return false;
        foreach (IModalUI m in stack)
        {
            if (ReferenceEquals(m, modal)) return true;
        }
        return false;
    }

    // -- API ---------------------------------------------------------------

    /// <summary>
    /// Registers the modal as active. If it is the first one, freezes Time.timeScale and frees the cursor.
    /// Idempotent: pushing the same modal twice does nothing.
    /// </summary>
    public void Push(IModalUI modal)
    {
        if (modal == null) return;
        if (Contains(modal))
        {
            Debug.LogWarning($"[UIStateManager] Duplicate Push ignored: {modal.ModalId}");
            return;
        }

        if (stack.Count == 0) TakeSnapshot();

        stack.Push(modal);
        topPushedFrame = Time.frameCount;
        ApplyModalEnvironment();
        OnModalPushed?.Invoke(modal);
    }

    /// <summary>
    /// Removes the modal. Only restores Time.timeScale and the cursor when the stack becomes empty.
    /// If the modal is not on top, it is removed anyway (out-of-order close case).
    /// </summary>
    public void Pop(IModalUI modal)
    {
        if (modal == null || stack.Count == 0) return;

        if (ReferenceEquals(stack.Peek(), modal))
        {
            stack.Pop();
        }
        else
        {
            // Out-of-order close: we rebuild the stack without that element.
            Stack<IModalUI> tmp = new Stack<IModalUI>(stack.Count);
            bool removed = false;
            while (stack.Count > 0)
            {
                IModalUI m = stack.Pop();
                if (!removed && ReferenceEquals(m, modal)) { removed = true; continue; }
                tmp.Push(m);
            }
            while (tmp.Count > 0) stack.Push(tmp.Pop());
            if (!removed) return;
        }

        if (stack.Count == 0) RestoreSnapshot();
        else ApplyModalEnvironment();

        OnModalPopped?.Invoke(modal);
    }

    /// <summary>Closes every modal (for example on a scene change or on losing).</summary>
    public void CloseAll()
    {
        while (stack.Count > 0)
        {
            IModalUI top = stack.Pop();
            top?.RequestClose();
        }
        RestoreSnapshot();
    }

    /// <summary>
    /// Frees the cursor and discards the pending snapshot.
    ///
    /// This exists for the gameplay -> menu context switch: the snapshot TakeSnapshot() takes
    /// during gameplay stores Locked + invisible, and RestoreSnapshot() reapplies it when the
    /// last modal closes. Since the PlayerCameraController (the only thing that frees the
    /// cursor during gameplay) is destroyed along with the level, without this the menu is
    /// left with no mouse. Discarding the snapshot prevents a later Pop/CloseAll from
    /// overwriting it again.
    /// </summary>
    public void SetCursorFree()
    {
        snapshotTaken = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // -- Environment (Time/Cursor) ----------------------------------------

    private void TakeSnapshot()
    {
        if (snapshotTaken) return;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        previousTimeScale = Time.timeScale;
        snapshotTaken = true;
    }

    private void ApplyModalEnvironment()
    {
        bool anyPauses = false;
        foreach (IModalUI m in stack)
        {
            if (m != null && m.PausesGame) { anyPauses = true; break; }
        }

        // Assigned both ways, not only on the way down. This used to be `if (anyPauses)
        // Time.timeScale = 0f`, which could never unfreeze: with a non-pausing modal left on the
        // stack after a pausing one is popped — the pause menu closing over an open inventory —
        // Pop calls back in here, finds anyPauses false, and did nothing at all, leaving the game
        // frozen with only the inventory showing. It was unreachable while every modal in the
        // project paused; the inventory not pausing is what made it reachable.
        //
        // The snapshot guard is for SetCursorFree, which discards the snapshot on the
        // gameplay -> menu switch and leaves previousTimeScale describing a level that is gone.
        Time.timeScale = anyPauses ? 0f : (snapshotTaken ? previousTimeScale : 1f);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void RestoreSnapshot()
    {
        if (!snapshotTaken) return;
        // Restore EXACTLY the previous state. Without reinterpreting:
        // - In gameplay: the cursor was Locked + invisible -> it stays that way.
        // - In the main menu: the cursor was None + visible -> it stays that way.
        // - Time.timeScale: usually 1 in both the main menu and gameplay, so we also
        //   restore the exact saved value.
        Time.timeScale = previousTimeScale;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
        snapshotTaken = false;
    }
}
