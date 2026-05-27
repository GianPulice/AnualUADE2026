using System;
using System.Collections.Generic;

/// <summary>
/// Modelo de la pantalla del SequencePanel. Es un adapter delgado entre el
/// <see cref="SequencePanelInteractable"/> (que vive en el mundo y tiene la lógica
/// del puzzle) y la View. No duplica lógica de validación — solo expone los datos
/// y re-emite los eventos del Interactable para que la View no necesite conocerlo.
/// </summary>
public class SequencePanelModel : BaseScreenModel
{
    public SequencePanelInteractable ActivePanel { get; private set; }

    public int ButtonCount => ActivePanel != null ? ActivePanel.ButtonCount : 0;

    public IReadOnlyList<int> EnteredSequence =>
        ActivePanel != null ? ActivePanel.EnteredSequence : EmptyList;

    public bool IsCompleted => ActivePanel != null && ActivePanel.IsCompleted;

    public event Action<int> OnButtonPressed;
    public event Action      OnSequenceFailed;
    public event Action      OnSequenceCompleted;

    private static readonly IReadOnlyList<int> EmptyList = new List<int>();

    public override void Initialize()
    {
        IsInitialized = true;
    }

    public void BindPanel(SequencePanelInteractable panel)
    {
        UnbindPanel();

        ActivePanel = panel;
        if (panel == null) return;

        panel.OnButtonPressed    += ForwardButtonPressed;
        panel.OnSequenceFailed   += ForwardSequenceFailed;
        panel.OnSequenceCompleted += ForwardSequenceCompleted;

        NotifyDataChanged();
    }

    public void UnbindPanel()
    {
        if (ActivePanel == null) return;

        ActivePanel.OnButtonPressed    -= ForwardButtonPressed;
        ActivePanel.OnSequenceFailed   -= ForwardSequenceFailed;
        ActivePanel.OnSequenceCompleted -= ForwardSequenceCompleted;

        ActivePanel = null;
    }

    public bool TryPressButton(int buttonId) =>
        ActivePanel != null && ActivePanel.TryPressButton(buttonId);

    public void ResetSequenceIfIncomplete()
    {
        if (ActivePanel != null && !ActivePanel.IsCompleted)
            ActivePanel.ResetSequence();
    }

    private void ForwardButtonPressed(int id) => OnButtonPressed?.Invoke(id);
    private void ForwardSequenceFailed()      => OnSequenceFailed?.Invoke();
    private void ForwardSequenceCompleted()   => OnSequenceCompleted?.Invoke();
}
