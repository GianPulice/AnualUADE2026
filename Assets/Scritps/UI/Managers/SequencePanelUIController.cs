using UnityEngine;

/// <summary>
/// Singleton que orquesta la apertura/cierre de la UI del panel de secuencia.
/// Vive en la escena LevelUI. Cualquier SequencePanelInteractable del mundo lo invoca
/// con Open(panel) cuando el jugador presiona E.
///
/// Es generico: una sola UI sirve para todos los SequencePanelInteractable del juego.
/// </summary>
public class SequencePanelUIController : Singleton<SequencePanelUIController>
{
    [Header("View")]
    [SerializeField] private SequencePanelView view;

    [Header("Input")]
    [SerializeField] private KeyCode closeKey = KeyCode.Escape;

    private SequencePanelInteractable activePanel;
    private bool isOpen;

    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;

    public bool IsOpen => isOpen;

    private void Awake()
    {
        CreateSingleton(false);

        if (view != null) view.SetVisibleImmediate(false);
    }

    private void Update()
    {
        if (!isOpen) return;
        if (Input.GetKeyDown(closeKey)) Close();
    }

    public void Open(SequencePanelInteractable panel)
    {
        if (panel == null) return;
        if (isOpen) return;
        if (view == null)
        {
            Debug.LogError("[SequencePanelUIController] View no asignada.");
            return;
        }

        activePanel = panel;
        isOpen = true;

        SubscribeToPanel(panel);

        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Time.timeScale = 0f;

        view.Bind(panel);
        view.Show();
    }

    public void Close()
    {
        if (!isOpen) return;

        // Si el jugador cierra sin completar, reseteamos lo ingresado.
        if (activePanel != null && !activePanel.IsCompleted)
            activePanel.ResetSequence();

        UnsubscribeFromPanel(activePanel);
        activePanel = null;
        isOpen = false;

        if (PauseManager.Instance == null || !PauseManager.Instance.IsPaused)
        {
            Time.timeScale = 1f;
            Cursor.lockState = previousCursorLock == CursorLockMode.None
                ? CursorLockMode.Locked
                : previousCursorLock;
            Cursor.visible = previousCursorVisible && previousCursorLock == CursorLockMode.None
                ? previousCursorVisible
                : false;
        }

        if (view != null) view.Hide();
    }

    private void SubscribeToPanel(SequencePanelInteractable panel)
    {
        if (panel == null) return;
        panel.OnSequenceCompleted += HandleSequenceCompleted;
    }

    private void UnsubscribeFromPanel(SequencePanelInteractable panel)
    {
        if (panel == null) return;
        panel.OnSequenceCompleted -= HandleSequenceCompleted;
    }

    private void HandleSequenceCompleted()
    {
        // Cerrar la UI al resolver el puzzle. La animacion de exito la maneja la View
        // antes de cerrar, si quiere.
        Close();
    }
}
