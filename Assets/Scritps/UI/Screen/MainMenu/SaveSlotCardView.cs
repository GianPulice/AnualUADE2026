using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Individual view of a card on the Save Slots screen.
/// Reads the new <see cref="SO_SaveSlotData"/> structure with its list of
/// <see cref="ModuleSaveEntry"/> (first 3 -> pips) and a runtime relative timestamp.
/// </summary>
public class SaveSlotCardView : MonoBehaviour
{
    [Header("Labels")]
    [SerializeField] private TextMeshProUGUI _slotNumberLabel;
    [SerializeField] private TextMeshProUGUI _zoneLabel;
    [SerializeField] private TextMeshProUGUI _playTimeLabel;
    [SerializeField] private TextMeshProUGUI _lastSavedLabel;
    [SerializeField] private TextMeshProUGUI _modulesLabel;
    [SerializeField] private TextMeshProUGUI _actionButtonLabel;

    [Header("Pips (the first 3 modules)")]
    [SerializeField] private Image _pip1;
    [SerializeField] private Image _pip2;
    [SerializeField] private Image _pip3;

    [Header("Pip Colors")]
    [SerializeField] private Color _pipInactive = new Color(0.11f, 0.11f, 0.11f, 1f);
    [SerializeField] private Color _pipActive   = new Color(0.53f, 0.13f, 0.13f, 1f);
    [SerializeField] private Color _pipResolved = new Color(0.10f, 0.36f, 0.10f, 1f);

    [Header("Action")]
    [SerializeField] private Button _actionButton;
    [SerializeField] private string _loadText = "[ load game ]";
    [SerializeField] private string _newText  = "[ new game ]";

    public event Action<int> OnSlotClicked;

    private int _slotIndex;

    private void Awake()
    {
        if (_actionButton != null)
            _actionButton.onClick.AddListener(() => OnSlotClicked?.Invoke(_slotIndex));
    }

    private void OnDestroy()
    {
        if (_actionButton != null)
            _actionButton.onClick.RemoveAllListeners();
    }

    public void Bind(SO_SaveSlotData data)
    {
        if (data == null) return;

        _slotIndex = data.SlotIndex;

        if (_slotNumberLabel != null)
            _slotNumberLabel.text = $"// SLOT {data.SlotIndex:00}";

        if (data.IsEmpty)
        {
            SetEmptyState();
            return;
        }

        if (_zoneLabel != null)         _zoneLabel.text         = data.ZoneName;
        if (_playTimeLabel != null)     _playTimeLabel.text     = $"Play time  {FormatPlayTime(data.PlayTimeSeconds)}";
        if (_lastSavedLabel != null)    _lastSavedLabel.text    = $"Saved  {data.GetRelativeSavedDescription()}";
        if (_modulesLabel != null)      _modulesLabel.text      = $"Modules  {data.ResolvedModulesCount} / 3 resolved";
        if (_actionButtonLabel != null) _actionButtonLabel.text = _loadText;

        IReadOnlyList<ModuleSaveEntry> modules = data.Modules;
        SetPip(_pip1, GetModuleStatus(modules, 0));
        SetPip(_pip2, GetModuleStatus(modules, 1));
        SetPip(_pip3, GetModuleStatus(modules, 2));
    }

    private void SetEmptyState()
    {
        if (_zoneLabel != null)         _zoneLabel.text         = "— empty —";
        if (_playTimeLabel != null)     _playTimeLabel.text     = "Play time  —";
        if (_lastSavedLabel != null)    _lastSavedLabel.text    = "Saved  —";
        if (_modulesLabel != null)      _modulesLabel.text      = "Modules  —";
        if (_actionButtonLabel != null) _actionButtonLabel.text = _newText;

        SetPip(_pip1, ModuleSlotStatus.Inactive);
        SetPip(_pip2, ModuleSlotStatus.Inactive);
        SetPip(_pip3, ModuleSlotStatus.Inactive);
    }

    private static ModuleSlotStatus GetModuleStatus(IReadOnlyList<ModuleSaveEntry> list, int idx)
        => list != null && idx < list.Count ? list[idx].status : ModuleSlotStatus.Inactive;

    private void SetPip(Image pip, ModuleSlotStatus status)
    {
        if (pip == null) return;
        pip.color = status switch
        {
            ModuleSlotStatus.Resolved => _pipResolved,
            ModuleSlotStatus.Active   => _pipActive,
            _                         => _pipInactive,
        };
    }

    private static string FormatPlayTime(float seconds)
    {
        int totalMinutes = Mathf.FloorToInt(seconds / 60f);
        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        return $"{hours}h {minutes:00}m";
    }
}
