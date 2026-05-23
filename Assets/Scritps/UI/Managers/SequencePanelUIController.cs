using UnityEngine;

/// <summary>
/// Singleton que orquesta la apertura/cierre de la UI del panel de secuencia.
/// Vive en la escena LevelUI. Cualquier SequencePanelInteractable del mundo lo invoca
/// con Open(panel) cuando el jugador presiona E.
///
/// Es generico: una sola UI sirve para todos los SequencePanelInteractable del juego.
/// </summary>
public class SequencePanelUIController : Singleton<SequencePanelUIController>, IModalUI
{
    [Header("View")]
    [SerializeField] private SequencePanelView view;

    private SequencePanelInteractable activePanel;
    private bool isOpen;

    public bool IsOpen => isOpen;

    // -- IModalUI --
    public string ModalId => "SequencePanel";
    public bool ConsumesEscape => false;  // ESC sube al PauseManager y abre la pausa encima.
    public bool BlocksPause   => false;   // La pausa puede aparecer sobre el panel.
    public void RequestClose() => Close();

    private void Awake()
    {
        CreateSingleton(false);

        if (view != null) view.SetVisibleImmediate(false);
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

        // Time.timeScale y cursor los gobierna UIStateManager.
        if (UIStateManager.Exists) UIStateManager.Instance.Push(this);

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

        if (UIStateManager.Exists) UIStateManager.Instance.Pop(this);

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
