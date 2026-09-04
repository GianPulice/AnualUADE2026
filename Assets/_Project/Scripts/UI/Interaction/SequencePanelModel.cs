using System;
using System.Collections.Generic;

/// <summary>
/// Model of the SequencePanel screen. It is a thin adapter between the
/// <see cref="SequencePanelInteractable"/> (which lives in the world and holds the puzzle
/// logic) and the View. It does not duplicate validation logic — it only exposes the data
/// and re-raises the Interactable's events so the View does not need to know about it.
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
