using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SaveSlotsView : BaseScreenView
{
    [Header("Grid")]
    [SerializeField] private Transform _gridContainer;
    [SerializeField] private SaveSlotCardView _cardPrefab;

    [Header("Navigation")]
    [SerializeField] private Button _backButton;

    [Header("Overlays")]
    [Tooltip("Yes / no box for deleting a save. Child of this canvas, saved inactive.")]
    [SerializeField] private SaveSlotConfirmDialog _confirmDialog;

    [Tooltip("Banner shown when New Game finds every slot taken. Saved inactive.")]
    [SerializeField] private GameObject _noFreeSlotNotice;

    [Tooltip("What the banner says while New Game asks which slot to overwrite.")]
    [SerializeField] private string _overwriteNoticeText = "ALL SAVE SLOTS ARE IN USE   //   CHOOSE ONE TO OVERWRITE";

    public event Action<int> OnSlotClicked;

    /// <summary>A card's delete button was clicked (slot index). Nothing is deleted yet.</summary>
    public event Action<int> OnDeleteClicked;

    public event Action OnBackClicked;

    private readonly List<SaveSlotCardView> _spawnedCards = new List<SaveSlotCardView>();

    private void Awake()
    {
        if (_backButton != null)
            _backButton.onClick.AddListener(() => OnBackClicked?.Invoke());
    }

    private void OnDestroy()
    {
        if (_backButton != null)
            _backButton.onClick.RemoveAllListeners();

        foreach (SaveSlotCardView card in _spawnedCards)
        {
            if (card == null) continue;
            card.OnSlotClicked -= HandleSlotClicked;
            card.OnDeleteClicked -= HandleDeleteClicked;
        }
        _spawnedCards.Clear();
    }

    /// <param name="lastPlayedSlot">Slot whose card is marked as the one played last, or
    /// <see cref="SaveGame.NoSlot"/> for none.</param>
    /// <param name="overwriteMode">New Game with every slot taken: the cards offer [ overwrite ].</param>
    public void Populate(IReadOnlyList<SO_SaveSlotData> slots, int lastPlayedSlot = SaveGame.NoSlot,
        bool overwriteMode = false)
    {
        ClearCards();

        if (slots == null || _cardPrefab == null || _gridContainer == null) return;

        foreach (SO_SaveSlotData slot in slots)
        {
            if (slot == null) continue;
            SaveSlotCardView card = Instantiate(_cardPrefab, _gridContainer);
            card.Bind(slot, slot.SlotIndex == lastPlayedSlot, overwriteMode);
            card.OnSlotClicked += HandleSlotClicked;
            card.OnDeleteClicked += HandleDeleteClicked;
            _spawnedCards.Add(card);
        }
    }

    /// <summary>Shows or hides the "every slot is taken, choose one to overwrite" banner.</summary>
    public void SetNoFreeSlotNotice(bool visible)
    {
        if (_noFreeSlotNotice == null) return;

        if (visible)
        {
            TMP_Text label = _noFreeSlotNotice.GetComponentInChildren<TMP_Text>(true);
            if (label != null && !string.IsNullOrEmpty(_overwriteNoticeText)) label.text = _overwriteNoticeText;
        }

        if (_noFreeSlotNotice.activeSelf != visible) _noFreeSlotNotice.SetActive(visible);
    }

    /// <summary>The yes / no box is up (ESC answers it first).</summary>
    public bool IsDialogOpen => _confirmDialog != null && _confirmDialog.IsOpen;

    /// <summary>Closes the yes / no box with no answer.</summary>
    public void CancelDialog()
    {
        if (_confirmDialog != null) _confirmDialog.Cancel();
    }

    /// <summary>
    /// Asks whether New Game may overwrite <paramref name="slot"/>. <paramref name="onConfirm"/> runs
    /// only on YES; with no dialog in the prefab nothing is overwritten.
    /// </summary>
    public void AskToOverwrite(SO_SaveSlotData slot, Action onConfirm)
    {
        if (slot == null) return;

        if (_confirmDialog == null)
        {
            Debug.LogError($"[{nameof(SaveSlotsView)}] _confirmDialog not assigned: slot {slot.SlotIndex} was not overwritten.", this);
            return;
        }

        _confirmDialog.Ask($"// OVERWRITE SLOT {slot.SlotIndex:00}?", Describe(slot) +
                           "This save will be lost and a new game will start in this slot.",
                           "[ YES ]", "[ NO ]", onConfirm);
    }

    // Zone, date and play time of a slot, and a blank line: the top of both questions.
    private static string Describe(SO_SaveSlotData slot)
    {
        string zone = string.IsNullOrWhiteSpace(slot.ZoneName) ? "Unknown location" : slot.ZoneName;
        return $"{zone}\n" +
               $"Saved {slot.GetSavedDateTimeText()}   //   Play time {SaveSlotCardView.FormatPlayTime(slot.PlayTimeSeconds)}\n\n";
    }

    /// <summary>
    /// Asks whether to delete <paramref name="slot"/>. <paramref name="onConfirm"/> runs only on yes.
    /// With no dialog in the prefab nothing is deleted: a save never goes without a confirmation.
    /// </summary>
    public void AskToDelete(SO_SaveSlotData slot, Action onConfirm)
    {
        if (slot == null) return;

        if (_confirmDialog == null)
        {
            Debug.LogError($"[{nameof(SaveSlotsView)}] _confirmDialog not assigned: slot {slot.SlotIndex} was not deleted.", this);
            return;
        }

        _confirmDialog.Ask($"// DELETE SLOT {slot.SlotIndex:00}?", Describe(slot) + "This save will be lost. It cannot be undone.",
                           "[ delete ]", "[ cancel ]", onConfirm);
    }

    /// <summary>Closes the dialog and the banner at once (the screen opening or closing).</summary>
    public void CloseOverlays()
    {
        if (_confirmDialog != null) _confirmDialog.HideImmediate();
        SetNoFreeSlotNotice(false);
    }

    private void ClearCards()
    {
        foreach (SaveSlotCardView card in _spawnedCards)
        {
            if (card == null) continue;
            card.OnSlotClicked -= HandleSlotClicked;
            card.OnDeleteClicked -= HandleDeleteClicked;
            // Destroy waits for the end of the frame: off first, or for one frame the grid lays
            // out the old cards and the new ones together (a redraw after a delete jumps).
            card.gameObject.SetActive(false);
            Destroy(card.gameObject);
        }
        _spawnedCards.Clear();
    }

    private void HandleSlotClicked(int slotIndex) => OnSlotClicked?.Invoke(slotIndex);

    private void HandleDeleteClicked(int slotIndex) => OnDeleteClicked?.Invoke(slotIndex);
}
