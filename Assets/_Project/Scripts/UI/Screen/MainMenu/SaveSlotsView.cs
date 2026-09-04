using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SaveSlotsView : BaseScreenView
{
    [Header("Grid")]
    [SerializeField] private Transform _gridContainer;
    [SerializeField] private SaveSlotCardView _cardPrefab;

    [Header("Navigation")]
    [SerializeField] private Button _backButton;

    public event Action<int> OnSlotClicked;
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
            if (card != null) card.OnSlotClicked -= HandleSlotClicked;
        }
        _spawnedCards.Clear();
    }

    public void Populate(IReadOnlyList<SO_SaveSlotData> slots)
    {
        ClearCards();

        if (slots == null || _cardPrefab == null || _gridContainer == null) return;

        foreach (SO_SaveSlotData slot in slots)
        {
            if (slot == null) continue;
            SaveSlotCardView card = Instantiate(_cardPrefab, _gridContainer);
            card.Bind(slot);
            card.OnSlotClicked += HandleSlotClicked;
            _spawnedCards.Add(card);
        }
    }

    private void ClearCards()
    {
        foreach (SaveSlotCardView card in _spawnedCards)
        {
            if (card == null) continue;
            card.OnSlotClicked -= HandleSlotClicked;
            Destroy(card.gameObject);
        }
        _spawnedCards.Clear();
    }

    private void HandleSlotClicked(int slotIndex) => OnSlotClicked?.Invoke(slotIndex);
}
