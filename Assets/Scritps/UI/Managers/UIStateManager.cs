using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Stack centralizado de UIs modales. Fuente unica de verdad sobre:
///   - Quien tiene foco de UI (Peek del stack).
///   - Si el gameplay esta bloqueado (IsAnyModalOpen).
///   - Time.timeScale (0 cuando hay >= 1 modal) y cursor (libre + visible cuando hay modal).
///
/// Como agregar una UI modal nueva sin romper nada:
///   1) Implementar IModalUI en tu controller (declarar ConsumesEscape y BlocksPause).
///   2) Llamar UIStateManager.Instance.Push(this) al abrir y Pop(this) al cerrar.
///   3) Quitar de tu controller cualquier manipulacion de Time.timeScale y Cursor:
///      las gobierna este manager.
///   4) (Opcional, Ola 2) Declarar el ActionMap name de tu UI en tu IModalUI para que
///      el manager habilite/deshabilite maps del Input System automaticamente.
///
/// El resto del juego consulta:
///   - UIStateManager.IsAnyModalOpen  -> bloquear interaccion, movimiento, camara.
///   - UIStateManager.IsBlockingPause -> el PauseManager pregunta antes de pausar.
///   - UIStateManager.TopConsumesEscape -> ESC se lo come la modal o sube al PauseManager.
/// </summary>
public class UIStateManager : Singleton<UIStateManager>
{
    public static event Action<IModalUI> OnModalPushed;
    public static event Action<IModalUI> OnModalPopped;

    [Header("Input")]
    [Tooltip("Action UI/Exit del InputSystem_Actions. Dispara RequestClose() sobre la modal del top que ConsumesEscape.")]
    [SerializeField] private InputActionReference exitAction;

    private Action<InputAction.CallbackContext> exitHandler;

    private readonly Stack<IModalUI> stack = new Stack<IModalUI>();
    private int topPushedFrame = -1;   // frame en el que se pusheo el actual top

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
    /// El usuario presiono UI/Exit (ESC por defecto). Si la modal del top declara que
    /// consume ESC, le pedimos que se cierre. Si no, no hacemos nada (el PauseManager
    /// escucha la misma tecla con Player/Pause y abrira la pausa por encima).
    ///
    /// Importante: si la modal del top fue pusheada en este MISMO frame, ignoramos el
    /// evento. Esto evita el race condition cuando Player/Pause y UI/Exit comparten
    /// la misma tecla y disparan en el mismo frame al abrir la pausa.
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

    /// <summary>True si la modal del top declara que bloquea la apertura de la pausa.</summary>
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

    /// <summary>True si la modal del top quiere comerse el ESC. False -> el ESC pasa al PauseManager.</summary>
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
    /// Registra la modal como activa. Si es la primera, congela Time.timeScale y libera el cursor.
    /// Idempotente: doble Push de la misma modal no hace nada.
    /// </summary>
    public void Push(IModalUI modal)
    {
        if (modal == null) return;
        if (Contains(modal))
        {
            Debug.LogWarning($"[UIStateManager] Push duplicado ignorado: {modal.ModalId}");
            return;
        }

        if (stack.Count == 0) TakeSnapshot();

        stack.Push(modal);
        topPushedFrame = Time.frameCount;
        ApplyModalEnvironment();
        OnModalPushed?.Invoke(modal);
    }

    /// <summary>
    /// Quita la modal. Solo restaura Time.timeScale y cursor cuando la pila queda vacia.
    /// Si la modal no esta en el top, igualmente se remueve (caso de cierres fuera de orden).
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
            // Cierre fuera de orden: reconstruimos sin el elemento.
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

    /// <summary>Cierra todas las modales (por ejemplo al cambiar de escena o al perder).</summary>
    public void CloseAll()
    {
        while (stack.Count > 0)
        {
            IModalUI top = stack.Pop();
            top?.RequestClose();
        }
        RestoreSnapshot();
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
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void RestoreSnapshot()
    {
        if (!snapshotTaken) return;
        Time.timeScale = Mathf.Approximately(previousTimeScale, 0f) ? 1f : previousTimeScale;
        Cursor.lockState = previousCursorLock == CursorLockMode.None ? CursorLockMode.Locked : previousCursorLock;
        Cursor.visible = previousCursorVisible && previousCursorLock != CursorLockMode.Locked;
        snapshotTaken = false;
    }
}
