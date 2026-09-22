using UnityEngine;

/// <summary>
/// Switch of the SP2 (boxes) room. Without power (SP1 not completed) it still takes the interaction
/// highlight, makes no sound and says it needs power, and its lights (<see cref="objects"/>) start
/// switched off. Once SP1 is completed it toggles them: on activates every object in the array, off
/// deactivates them again, with a click and a lever animation each time.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PoweredLightSwitch : BaseRangeInteractable
{
    [Tooltip("The lights. Deactivated at start while SP1 is not completed; the switch toggles them.")]
    [SerializeField] private GameObject[] objects = new GameObject[0];

    [Tooltip("SP1 completed: the switch has power. Set automatically when the puzzle below completes.")]
    [SerializeField] private bool sp1Completed;

    [PuzzleId]
    [SerializeField] private string sp1PuzzleId = "sp1_panel_electrico";

    [Tooltip("ON: if this button's GameObject is inactive (activeSelf false) it does nothing — no " +
             "toggle, no look change. For a second button that shares the same objects.")]
    [SerializeField] private bool ignoreIfInactive = true;

    [Header("Text")]
    [SerializeField] private string turnOnPrompt = "Turn on";
    [SerializeField] private string turnOffPrompt = "Turn off";
    [SerializeField] private string noPowerInfo = "You need to turn on the energy first";

    [Header("Audio")]
    [SoundId] [SerializeField] private string clickSoundId = "sfx_elevator_button_01";

    [Header("Switch animation")]
    [Tooltip("Animator driving the lever mesh. Left empty it is taken from this object's own " +
             "children, so the prefab needs no wiring at all.")]
    [SerializeField] private Animator switchAnimator;

    [Tooltip("The name of the trigger in the animator when pressed.")]
    [SerializeField] private string pressTriggerName = "Pressed";

    private int pressTriggerHash;

    // On = the lights are active. Read from the objects themselves, not stored: two buttons sharing
    // the same array would otherwise each keep their own idea of it and undo each other.
    private bool IsOn
    {
        get
        {
            foreach (GameObject go in objects)
                if (go != null) return go.activeSelf;
            return false;
        }
    }

    private bool Ignored => ignoreIfInactive && !gameObject.activeSelf;

    protected override void Awake()
    {
        base.Awake();
        ResolveSwitchAnimation();
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
    }

    private void Start()
    {
        if (PuzzleStateManager.Exists && PuzzleStateManager.Instance.IsPuzzleCompleted(sp1PuzzleId))
            sp1Completed = true;

        // No power yet: the room starts dark. Left as authored when SP1 is already done.
        if (!sp1Completed) SetLights(false);
    }

    private void OnDestroy()
    {
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
    }

    private void HandlePuzzleCompleted(string puzzleId)
    {
        if (puzzleId != sp1PuzzleId || Ignored) return;
        sp1Completed = true;
        InteractionEvents.RequestPromptRefresh();
    }

    // ── IInteractable ───────────────────────────────────────────────────────

    public override string GetInteractText() => IsOn ? turnOffPrompt : turnOnPrompt;

    // Shown by the prompt in its grey info style while the switch cannot be used.
    public override string GetInfoText() => sp1Completed ? string.Empty : noPowerInfo;

    protected override bool CanInteractInCloseRange() => sp1Completed && !Ignored;

    public override bool IsRepeatable() => true;

    protected override void OnInteract()
    {
        if (!sp1Completed || Ignored) return;

        SetLights(!IsOn);
        PlaySwitchPress();

        if (!string.IsNullOrWhiteSpace(clickSoundId) && AudioManager.Exists)
            AudioManager.Instance.PlaySFX(clickSoundId, transform.position);

        InteractionEvents.RequestPromptRefresh();
    }

    // No OnInteractAttemptBlocked override: without power, pressing it is silent.

    private void SetLights(bool on)
    {
        foreach (GameObject go in objects)
            if (go != null) go.SetActive(on);
    }

    // ── Lever animation ─────────────────────────────────────────────────────

    /// <summary>
    /// Finds the Animator and resolves the trigger hash once. See
    /// <see cref="ElevatorCallPanel.ResolveSwitchAnimation"/> for why this is resolved up front
    /// rather than looked up on every press.
    /// </summary>
    private void ResolveSwitchAnimation()
    {
        if (switchAnimator == null) switchAnimator = GetComponentInChildren<Animator>(true);

        // Silent when there is none. A switch is allowed to be a flat unanimated box.
        if (switchAnimator == null) return;

        pressTriggerHash = Animator.StringToHash(pressTriggerName);
    }

    /// <summary>Throws the lever, top to bottom and back to top. A no-op with no animator.</summary>
    private void PlaySwitchPress()
    {
        if (switchAnimator == null) return;

        switchAnimator.SetTrigger(pressTriggerHash);
    }
}
