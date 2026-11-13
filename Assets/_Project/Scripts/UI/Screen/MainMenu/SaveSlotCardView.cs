using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Individual view of a card on the Save Slots screen.
/// Reads the new <see cref="SO_SaveSlotData"/> structure with its list of
/// <see cref="ModuleSaveEntry"/> (first 3 -> pips), and the save's date and time.
///
/// The action button starts a new run in an empty slot or loads the one with data (the MainMenu
/// decides). The delete button only shows on a card with data, and only asks: the confirmation and
/// the deletion are the SaveSlotsController's. The "last played" nodes mark the slot the player was
/// on last (<see cref="SaveSlotsModel.LastPlayedSlot"/>).
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
    [SerializeField] private Color _pipExploded = new Color(0.55f, 0.30f, 0.05f, 1f);

    [Header("Action")]
    [SerializeField] private Button _actionButton;
    [SerializeField] private string _loadText = "[ load game ]";
    [SerializeField] private string _newText  = "[ new game ]";
    [Tooltip("The action on a card with a save while choosing which slot New Game overwrites.")]
    [SerializeField] private string _overwriteText = "[ overwrite ]";

    [Header("Resolved (won run)")]
    [Tooltip("Shown only on a resolved slot: the green RESOLVED block in the middle of the card.")]
    [SerializeField] private GameObject[] _resolvedNodes = Array.Empty<GameObject>();
    [Tooltip("Second line of the block: when the run was won and its play time.")]
    [SerializeField] private TextMeshProUGUI _resolvedDetailLabel;

    [Header("Delete")]
    [Tooltip("Shown only when the slot has a save. Clicking it asks for confirmation first.")]
    [SerializeField] private Button _deleteButton;

    [Header("Last played")]
    [Tooltip("Shown only on the card of the slot played last (the tag, the side bar).")]
    [SerializeField] private GameObject[] _lastPlayedNodes = Array.Empty<GameObject>();

    [Header("Empty slot: powered off")]
    [Tooltip("The card's surfaces (frame fill, screen). On an empty slot they take Empty Surface Role.")]
    [SerializeField] private UIThemeApplier[] _emptySurfaces = Array.Empty<UIThemeApplier>();
    [Tooltip("Role of those surfaces on an empty slot: grey instead of the red of a slot with a save.")]
    [SerializeField] private UIThemeRole _emptySurfaceRole = UIThemeRole.SurfacePanel;
    [Tooltip("Role of every text of an empty card (slot number, \"— empty —\", the data lines).")]
    [SerializeField] private UIThemeRole _emptyTextRole = UIThemeRole.TextDisabled;
    [Tooltip("Role of the [ new game ] label: dimmed with the card, still readable (it is the one thing to click).")]
    [SerializeField] private UIThemeRole _emptyButtonTextRole = UIThemeRole.TextMuted;
    [Tooltip("The card's bevel frames. On an empty slot they are multiplied by Empty Frame Tint.")]
    [SerializeField] private UIBevelFrame[] _emptyFrames = Array.Empty<UIBevelFrame>();
    [Tooltip("Below white dims the frames of an empty card.")]
    [SerializeField] private Color _emptyFrameTint = new Color(0.45f, 0.45f, 0.45f, 1f);

    /// <summary>The action button was clicked (slot index): new run or load.</summary>
    public event Action<int> OnSlotClicked;

    /// <summary>The delete button was clicked (slot index). Nothing is deleted yet.</summary>
    public event Action<int> OnDeleteClicked;

    private int _slotIndex;

    // The look of a card with a save, read from the prefab the first time the card is bound, so an
    // empty card can be switched back (a card is normally rebuilt instead, but Bind may be called twice).
    private readonly List<(UIThemeApplier applier, UIThemeRole role)> _onRoles = new List<(UIThemeApplier, UIThemeRole)>();
    private readonly List<(UIBevelFrame frame, Color color)> _onFrames = new List<(UIBevelFrame, Color)>();
    private bool _onLookCaptured;

    private void Awake()
    {
        if (_actionButton != null)
        {
            _actionButton.onClick.AddListener(() =>
            {
                UISounds.PlayRandom(UISounds.SaveConfirm);
                OnSlotClicked?.Invoke(_slotIndex);
            });

            // Cards are instantiated at runtime, so UICanvasSounds never sees them. No click sound:
            // the confirmation above is the click.
            ConfigureSound(_actionButton, UISounds.SaveSlotHover, null);
        }
        else
        {
            Debug.LogWarning($"[{nameof(SaveSlotCardView)}] '{name}': _actionButton not assigned, the card cannot be clicked.", this);
        }

        if (_deleteButton != null)
        {
            _deleteButton.onClick.AddListener(() => OnDeleteClicked?.Invoke(_slotIndex));
            ConfigureSound(_deleteButton, UISounds.Hover, UISounds.Click);
        }
    }

    private void OnDestroy()
    {
        if (_actionButton != null) _actionButton.onClick.RemoveAllListeners();
        if (_deleteButton != null) _deleteButton.onClick.RemoveAllListeners();
    }

    /// <param name="overwriteMode">The screen was opened by New Game with every slot taken: a card
    /// with a save offers [ overwrite ] instead of [ load game ], and no delete.</param>
    public void Bind(SO_SaveSlotData data, bool isLastPlayed = false, bool overwriteMode = false)
    {
        if (data == null) return;

        _slotIndex = data.SlotIndex;

        if (_slotNumberLabel != null)
            _slotNumberLabel.text = $"// SLOT {data.SlotIndex:00}";

        // Empty and resolved both look switched off: neither has a run to go back to.
        SetPoweredOff(!data.IsLoadable);
        SetResolvedVisible(data.IsResolved);

        if (data.IsEmpty)
        {
            SetEmptyState();
            return;
        }

        if (data.IsResolved)
        {
            SetResolvedState(data);
            return;
        }

        if (_zoneLabel != null)         _zoneLabel.text         = data.ZoneName;
        if (_playTimeLabel != null)     _playTimeLabel.text     = $"Play time  {FormatPlayTime(data.PlayTimeSeconds)}";
        if (_lastSavedLabel != null)    _lastSavedLabel.text    = $"Saved  {FormatSaved(data)}";
        if (_modulesLabel != null)      _modulesLabel.text      = $"Modules  {data.ResolvedModulesCount} / 3 resolved";
        if (_actionButtonLabel != null) _actionButtonLabel.text = overwriteMode ? _overwriteText : _loadText;

        IReadOnlyList<ModuleSaveEntry> modules = data.Modules;
        SetPip(_pip1, GetModuleStatus(modules, 0));
        SetPip(_pip2, GetModuleStatus(modules, 1));
        SetPip(_pip3, GetModuleStatus(modules, 2));

        SetDeleteVisible(!overwriteMode);
        SetLastPlayed(isLastPlayed);
    }

    /// <summary>
    /// A won run: the card is switched off like an empty one, with the green RESOLVED block in its
    /// middle (the date of the win and the play time under it). It cannot be loaded; [ new game ]
    /// takes the slot, as after a loss. The data lines are blanked so nothing shows around the block.
    /// </summary>
    private void SetResolvedState(SO_SaveSlotData data)
    {
        if (_zoneLabel != null)         _zoneLabel.text         = string.Empty;
        if (_playTimeLabel != null)     _playTimeLabel.text     = string.Empty;
        if (_lastSavedLabel != null)    _lastSavedLabel.text    = string.Empty;
        if (_modulesLabel != null)      _modulesLabel.text      = string.Empty;
        if (_actionButtonLabel != null) _actionButtonLabel.text = _newText;

        if (_resolvedDetailLabel != null)
            _resolvedDetailLabel.text = $"{data.GetSavedDateTimeText()}   //   {FormatPlayTime(data.PlayTimeSeconds)}";

        foreach (Image pip in new[] { _pip1, _pip2, _pip3 })
            if (pip != null) pip.color = Color.clear;

        SetDeleteVisible(false);
        SetLastPlayed(false);
    }

    private void SetResolvedVisible(bool visible)
    {
        if (_resolvedNodes == null) return;
        foreach (GameObject node in _resolvedNodes)
            if (node != null) node.SetActive(visible);
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

        // Nothing to delete, and an empty slot is never the one to go back to.
        SetDeleteVisible(false);
        SetLastPlayed(false);
    }

    /// <summary>
    /// An empty or resolved slot looks switched off: grey surfaces instead of red, dimmed frames, every text in
    /// the disabled grey, and [ new game ] a step brighter so it still reads as the thing to click.
    /// Through the theme roles, so the greys follow the palette.
    /// </summary>
    private void SetPoweredOff(bool off)
    {
        CaptureOnLook();

        foreach ((UIThemeApplier applier, UIThemeRole role) in _onRoles)
        {
            if (applier == null) continue;

            UIThemeRole target = role;
            if (off)
            {
                bool isButton = _actionButtonLabel != null && applier.gameObject == _actionButtonLabel.gameObject;
                bool isSurface = Array.IndexOf(_emptySurfaces, applier) >= 0;
                target = isSurface ? _emptySurfaceRole : isButton ? _emptyButtonTextRole : _emptyTextRole;
            }
            if (applier.Role != target) applier.SetRole(target);
        }

        foreach ((UIBevelFrame frame, Color color) in _onFrames)
            if (frame != null) frame.color = off ? color * _emptyFrameTint : color;
    }

    private void CaptureOnLook()
    {
        if (_onLookCaptured) return;
        _onLookCaptured = true;

        void Add(Component c)
        {
            UIThemeApplier applier = c != null ? c.GetComponent<UIThemeApplier>() : null;
            if (applier != null) _onRoles.Add((applier, applier.Role));
        }

        Add(_slotNumberLabel);
        Add(_zoneLabel);
        Add(_playTimeLabel);
        Add(_lastSavedLabel);
        Add(_modulesLabel);
        Add(_actionButtonLabel);
        foreach (UIThemeApplier surface in _emptySurfaces)
            if (surface != null) _onRoles.Add((surface, surface.Role));

        foreach (UIBevelFrame frame in _emptyFrames)
            if (frame != null) _onFrames.Add((frame, frame.color));
    }

    private void SetDeleteVisible(bool visible)
    {
        if (_deleteButton != null) _deleteButton.gameObject.SetActive(visible);
    }

    private void SetLastPlayed(bool value)
    {
        if (_lastPlayedNodes == null) return;
        foreach (GameObject node in _lastPlayedNodes)
            if (node != null) node.SetActive(value);
    }

    private static void ConfigureSound(Button button, string hover, string click)
    {
        UISelectableSound sound = button.GetComponent<UISelectableSound>();
        if (sound == null) sound = button.gameObject.AddComponent<UISelectableSound>();
        sound.Configure(hover, click);
    }

    /// <summary>
    /// "13 NOV 2026  14:32  (12 min ago)", in the player's local time. The "ago" part only for
    /// saves under a week old: past that the date says it all, and "(10 weeks ago)" would push the
    /// line past the card's width.
    /// </summary>
    private static string FormatSaved(SO_SaveSlotData data)
    {
        if (!data.TryGetSavedUtc(out DateTime savedUtc)) return "—";

        string date = data.GetSavedDateTimeText();
        return DateTime.UtcNow - savedUtc < TimeSpan.FromDays(7)
            ? $"{date}  ({data.GetRelativeSavedDescription()})"
            : date;
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
            ModuleSlotStatus.Exploded => _pipExploded,
            _                         => _pipInactive,
        };
    }

    /// <summary>"2h 05m". Shared with the delete confirmation.</summary>
    public static string FormatPlayTime(float seconds)
    {
        int totalMinutes = Mathf.FloorToInt(seconds / 60f);
        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        return $"{hours}h {minutes:00}m";
    }
}
