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
///   1) Implement IModalUI on your controller (declare ConsumesEscape and BlocksPause).
///   2) Call UIStateManager.Instance.Push(this) on open and Pop(this) on close.
///   3) Remove any Time.timeScale and Cursor manipulation from your controller:
///      this manager governs them.
///   4) (Optional, Wave 2) Declare your UI's ActionMap name in your IModalUI so the
///      manager enables/disables Input System maps automatically.
///
/// The rest of the game queries:
///   - UIStateManager.IsAnyModalOpen  -> block interaction, movement, camera.
///   - UIStateManager.IsBlockingPause -> the PauseManager asks before pausing.
///   - UIStateManager.TopConsumesEscape -> ESC is eaten by the modal or goes up to the PauseManager.
/// </summary>
public class UIStateManager : Singleton<UIStateManager>
{
    public static event Action<IModalUI> OnModalPushed;
    public static event Action<IModalUI> OnModalPopped;

    [Header("Input")]
    [Tooltip("UI/Exit action of InputSystem_Actions. Fires RequestClose() on the top modal that ConsumesEscape.")]
    [SerializeField] private InputActionReference exitAction;

    private Action<InputAction.CallbackContext> exitHandler;

    private readonly Stack<IModalUI> stack = new Stack<IModalUI>();
    private int topPushedFrame = -1;   // frame in which the current top was pushed

    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private float previousTimeScale = 1f;
    private bool snapshotTaken;

    private void Awake()
    {
        CreateSingleton(true);
    }

    private void OnEnable()
    {
        if (exitAction != null)
        {
            exitAction.action.Enable();
            exitHandler = _ => OnExitPressed();
            exitAction.action.performed += exitHandler;
        }
    }

    private void OnDisable()
    {
        if (exitAction != null && exitHandler != null)
        {
            exitAction.action.performed -= exitHandler;
            exitHandler = null;
        }
    }

    /// <summary>
    /// The user pressed UI/Exit (ESC by default). If the top modal declares that it consumes
    /// ESC, we ask it to close. If not, we do nothing (the PauseManager listens to the same
    /// key with Player/Pause and will open the pause menu on top).
    ///
    /// Important: if the top modal was pushed in this SAME frame, we ignore the event. This
    /// avoids the race condition when Player/Pause and UI/Exit share the same key and both
    /// fire in the same frame when opening the pause menu.
    /// </summary>
    private void OnExitPressed()
    {
        if (stack.Count == 0) return;
        if (topPushedFrame == Time.frameCount) return;
        IModalUI top = stack.Peek();
        if (top == null) return;
        if (!top.ConsumesEscape) return;
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

    /// <summary>True if the top modal wants to eat the ESC. False -> the ESC goes to the PauseManager.</summary>
    public bool TopConsumesEscape
    {
        get
        {
            if (stack.Count == 0) return false;
            IModalUI top = stack.Peek();
            return top != null && top.ConsumesEscape;
        }
    }

    public IModalUI Peek() => stack.Count > 0 ? stack.Peek() : null;

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
        if (anyPauses) Time.timeScale = 0f;

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
