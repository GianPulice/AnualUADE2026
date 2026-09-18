using System.Collections;
using UnityEngine;

/// <summary>
/// Call button for a freight elevator. One per landing.
///
/// This is the piece the shaft was missing. <see cref="MovingPlatform.RequestRide"/> existed from
/// the start, but the only caller was <see cref="NemesisElevatorUser"/> — so the platform could be
/// summoned by the monster and never by the player. Ride up, step off, and the cabin's own
/// auto-return took it back down; from then on that floor was unreachable for the rest of the run.
/// That is the softlock, and it was a missing button rather than a broken state machine.
///
/// Which landing this panel serves is MEASURED, not authored: it asks the link which of the two
/// landings it is standing closer to. Same reasoning as NemesisElevatorLink's ride-distance
/// calibration — the answer is already in the scene, and a hand-set "is top / is bottom" flag is
/// one more thing to get silently backwards.
///
/// SCENE SETUP:
///   1. Empty GameObject next to each landing, with a Collider on the Interactable layer (the
///      InteractionManager's SphereCast needs something to hit — see docs/CLAUDE.md).
///   2. This component, with the shaft's NemesisElevatorLink dragged in.
///   3. Panel Renderer set to the mesh that carries the emissive panel material. No Light needed —
///      the panel's own material provides the glow, this only changes its emission colour.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ElevatorCallPanel : BaseRangeInteractable
{
    /// <summary>
    /// What the panel is doing, which is what both the prompt and the emission colour read from.
    /// </summary>
    private enum PanelState
    {
        /// <summary>Cabin parked on the other floor and free: pressing it calls it over.</summary>
        Callable,

        /// <summary>Cabin parked at THIS landing. Nothing to call.</summary>
        CabinPresent,

        /// <summary>Travelling, or reserved by the Nemesis. The press is refused.</summary>
        Busy,
    }

    [Header("Shaft")]
    [Tooltip("The elevator this panel calls. Left empty it is looked up in the parents, so a " +
             "panel parented under the ElevatorRoot needs no wiring at all.")]
    [SerializeField] private NemesisElevatorLink elevator;

    [Header("Feedback")]
    [Tooltip("Renderer carrying the panel's emissive material. Its _EmissionColor follows the " +
             "state — never red: per the visual language spec, #CC1A1A is reserved for danger, " +
             "and a busy lift is not danger.\n\n" +
             "Driven through a MaterialPropertyBlock rather than material.SetColor, so the shared " +
             "material asset is not instanced per panel — the trap ItemProximityHighlight " +
             "documents.")]
    [SerializeField] private Renderer panelRenderer;

    [Tooltip("Material index on Panel Renderer to tint. 0 for a single-material mesh.")]
    [SerializeField, Min(0)] private int panelMaterialIndex = 0;

    [Tooltip("Cabin is somewhere else and can be called. Amber, the project's device colour.")]
    [SerializeField] private Color callableColor = new Color(1f, 0.784f, 0.314f);

    [Tooltip("Cabin is at this landing.")]
    [SerializeField] private Color cabinPresentColor = new Color(0.541f, 0.706f, 0.831f);

    [Tooltip("Travelling or taken. Dim amber that pulses, rather than a red that would read as " +
             "'danger' instead of 'wait'.")]
    [SerializeField] private Color busyColor = new Color(0.55f, 0.42f, 0.16f);

    [Tooltip("Pulses per second of the busy state. 0 holds it steady.")]
    [SerializeField, Min(0f)] private float busyPulseSpeed = 2f;

    [Header("Switch animation")]
    [Tooltip("Animator driving the lever mesh. Left empty it is taken from this object's own " +
             "children, so the ElevatorCallSwitch prefab needs no wiring at all.")]
    [SerializeField] private Animator switchAnimator;

    [Tooltip("The name of the trigger in the animator when activated.")]
    [SerializeField] private string pressTriggerName = "Pressed";

    [Header("Audio (SO_SoundData ids)")]
    [Tooltip("Played when the call is accepted. Leave empty for none.")]
    [SoundId]
    [SerializeField] private string callAcceptedSoundId;

    [Tooltip("Played when the panel refuses because the lift is busy.")]
    [SoundId]
    [SerializeField] private string callRefusedSoundId;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    /// <summary>Animator layer the lever states live on. Base layer; the controller has no other.</summary>
    private const int SwitchLayer = 0;

    private int pressTriggerHash;

    private MaterialPropertyBlock propertyBlock;

    /// <summary>
    /// Whether this panel sits at the bottom landing. Resolved once in Awake: the landings are
    /// static (they must not be children of the cabin — see NemesisElevatorLink's setup notes), so
    /// the answer cannot change during play.
    /// </summary>
    private bool isBottomPanel;

    private bool isConfigured;

    /// <summary>
    /// The state the prompt was last drawn for, or null while the crosshair is on something else.
    /// See <see cref="RefreshPromptOnStateChange"/>.
    /// </summary>
    private PanelState? promptState;

    private MovingPlatform Platform => elevator != null ? elevator.Platform : null;

    /// <summary>
    /// Whether the cabin is parked at THIS landing.
    ///
    /// Read off <see cref="MovingPlatform.GoingUp"/> and not off a position comparison: this is a
    /// two-position shuttle, so "which way would the next trip leave" and "which end is it parked
    /// at" are the same fact, and the flag is exact where a distance test needs a tolerance.
    /// </summary>
    private bool IsCabinHere
    {
        get
        {
            MovingPlatform platform = Platform;
            return platform != null && platform.GoingUp == isBottomPanel;
        }
    }

    private PanelState State
    {
        get
        {
            MovingPlatform platform = Platform;
            if (platform == null) return PanelState.Busy;

            // Claimed counts as busy even while the cabin is sitting still: the Nemesis holds the
            // platform from the moment it commits to the shaft, and letting a press through in
            // that window would take the ride out from under it.
            if (!platform.IsAvailable) return PanelState.Busy;

            return IsCabinHere ? PanelState.CabinPresent : PanelState.Callable;
        }
    }

    protected override void Awake()
    {
        base.Awake();

        // Ahead of the isConfigured early-out below: a panel whose shaft is miswired still has a
        // lever, and a bad state name deserves its own line in the console instead of being
        // swallowed by an unrelated failure.
        ResolveSwitchAnimation();

        if (elevator == null) elevator = GetComponentInParent<NemesisElevatorLink>();

        isConfigured = ValidateSetup();
        if (!isConfigured) return;

        isBottomPanel = elevator.IsAtBottomSide(transform.position);

        propertyBlock = new MaterialPropertyBlock();
        ApplyFeedback(1f);
    }

    private bool ValidateSetup()
    {
        if (elevator == null)
        {
            Debug.LogError($"[{nameof(ElevatorCallPanel)}] '{name}' has no " +
                           $"{nameof(NemesisElevatorLink)} assigned and none in its parents. The " +
                           "panel will do nothing.", this);
            return false;
        }

        // Asks the platform directly rather than the link's own IsUsable verdict: that flag is set
        // in NemesisElevatorLink.Awake and script execution order does not guarantee it has run
        // yet, so reading it here could report a perfectly good shaft as broken.
        if (elevator.Platform == null)
        {
            Debug.LogError($"[{nameof(ElevatorCallPanel)}] '{name}': the elevator " +
                           $"'{elevator.name}' has no {nameof(MovingPlatform)}. The panel will do " +
                           "nothing.", this);
            return false;
        }

        // Not fatal — CanInteract/OnInteract do not depend on it — but Panel Renderer is now the
        // ONLY feedback path (no Light fallback), so a panel with none is silently invisible about
        // its own state: it still calls the lift, it just never shows busy/callable/here.
        if (panelRenderer == null)
        {
            Debug.LogWarning($"[{nameof(ElevatorCallPanel)}] '{name}' has no {nameof(panelRenderer)} " +
                             "assigned. It will still call the elevator, but will show no visual " +
                             "feedback for callable/busy/here.", this);
        }

        return true;
    }

    // ── IInteractable ───────────────────────────────────────────────────────

    /// <summary>
    /// Refused rather than queued when the lift is busy. Queuing would be friendlier in a lift sim
    /// and wrong here: the point of the panel is that calling it is a decision with a wait
    /// attached, and a press that silently books a ride for later removes the moment where you
    /// stand at the doors listening for what is coming.
    /// </summary>
    protected override bool CanInteractInCloseRange() => isConfigured && State == PanelState.Callable;

    public override string GetInteractText()
    {
        if (!isConfigured) return string.Empty;

        switch (State)
        {
            case PanelState.CabinPresent: return "Forklift is here";
            case PanelState.Busy:         return "Forklift in use";
            default:                      return "Call forklift";
        }
    }

    /// <summary>
    /// The greyed-out line the prompt shows while the panel cannot be pressed. CabinPresent used to
    /// return nothing here — its "Forklift is here" lives in GetInteractText, which the prompt only
    /// draws when the press is allowed — so a panel with the cabin already at its landing said
    /// nothing at all. Which landing it is comes from isBottomPanel, so each panel names its floor.
    /// </summary>
    public override string GetInfoText()
    {
        if (!isConfigured) return string.Empty;

        switch (State)
        {
            case PanelState.CabinPresent:
                return isBottomPanel ? "Forklift is already down here." : "Forklift is already up here.";
            case PanelState.Busy:
                return "Forklift in use. Wait for it to come free.";
            default:
                return string.Empty;
        }
    }

    /// <summary>Repeatable: a call button that works once is a call button that softlocks the
    /// floor the second time.</summary>
    public override bool IsRepeatable() => true;

    protected override void OnInteract()
    {
        MovingPlatform platform = Platform;
        if (platform == null) return;

        // RequestRide can still come back false — CanInteract was evaluated a frame earlier by the
        // InteractionManager and the Nemesis can claim the platform in between. Treated as a
        // refusal rather than ignored, so the player gets told instead of being left pressing a
        // button that does nothing.
        // Thrown for the PRESS, not for the outcome. Reaching here already means CanInteract said
        // yes, so the player did physically flick the lever; whether the cabin answers is what the
        // two sounds below are for. Move this into the accepted branch if the lever should stay
        // still on a call the shaft refuses.
        PlaySwitchPress();

        if (platform.RequestRide()) PlaySound(callAcceptedSoundId);
        else                        PlaySound(callRefusedSoundId);
    }

    // ── Lever animation ─────────────────────────────────────────────────────

    /// <summary>
    /// Finds the Animator and resolves both state names once.
    ///
    /// Resolved up front because <see cref="Animator.Play(int, int, float)"/> with a state that is
    /// not on the layer does NOTHING AT ALL — no exception, no warning, the lever simply never
    /// moves. A typo there is invisible from inside the game and costs an afternoon in the Animator
    /// window; HasState turns it into one line in the console at startup.
    /// </summary>
    private void ResolveSwitchAnimation()
    {
        if (switchAnimator == null) switchAnimator = GetComponentInChildren<Animator>(true);

        // Silent when there is none. A call panel is allowed to be a flat unanimated box, and in
        // this project most of them are.
        if (switchAnimator == null) return;

        pressTriggerHash = Animator.StringToHash(pressTriggerName);
    }

    /// <summary>Throws the lever. A no-op on a panel with no animator or no press state.</summary>
    private void PlaySwitchPress()
    {
        if (switchAnimator == null) return;

        switchAnimator.SetTrigger(pressTriggerHash);
    }

    private void PlaySound(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !AudioManager.Exists) return;
        AudioManager.Instance.PlaySFX(id, transform.position);
    }

    // ── Feedback ────────────────────────────────────────────────────────────

    // LateUpdate, not Update: the ItemProximityHighlight on the prefab root also writes
    // _EmissionColor on the panel's slot, and its lerp is a coroutine, which runs after every Update.
    // From Update the lamp would show the highlight's colour instead of the state for the whole
    // transition each time the crosshair came or went. LateUpdate is the last write before the frame
    // renders, so the lamp always shows the state and the highlight is left to the lever. The prompt
    // watch gains from it too: it sees this frame's target, and a press InteractionManager.Update
    // has just handled, instead of catching them a frame late.
    private void LateUpdate()
    {
        if (!isConfigured) return;

        RefreshPromptOnStateChange();

        // Unscaled: the panel stays readable behind a paused menu, the same choice the UI views
        // make. It drives nothing but colour, so there is no gameplay to freeze.
        float pulse = State == PanelState.Busy && busyPulseSpeed > 0f
            ? Mathf.Lerp(0.35f, 1f,
                         Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI * busyPulseSpeed)))
            : 1f;

        ApplyFeedback(pulse);
    }

    /// <summary>
    /// Re-draws the prompt when the state changes under the crosshair — the same watch as
    /// PushableBox's range check. The prompt only re-reads a target when the target itself changes,
    /// and this panel changes while the player stands there looking at it: the press turns Callable
    /// into Busy on the spot, and the cabin arriving turns Busy into CabinPresent. Without it the
    /// window kept offering "Call forklift" with its [E] for the whole trip, on a panel that
    /// refuses the press.
    ///
    /// Only while this panel is the target: the refresh is global and redraws whatever the crosshair
    /// is on.
    /// </summary>
    private void RefreshPromptOnStateChange()
    {
        if (!InteractionManager.Exists ||
            !ReferenceEquals(InteractionManager.Instance.CurrentInteractable, this))
        {
            promptState = null;
            return;
        }

        PanelState state = State;
        if (promptState == state) return;

        promptState = state;
        InteractionEvents.RequestPromptRefresh();
    }

    private void ApplyFeedback(float intensity)
    {
        if (panelRenderer == null) return;

        Color color = ColorFor(State) * intensity;

        panelRenderer.GetPropertyBlock(propertyBlock, panelMaterialIndex);
        propertyBlock.SetColor(EmissionColorId, color);
        panelRenderer.SetPropertyBlock(propertyBlock, panelMaterialIndex);
    }

    private Color ColorFor(PanelState state)
    {
        switch (state)
        {
            case PanelState.CabinPresent: return cabinPresentColor;
            case PanelState.Busy:         return busyColor;
            default:                      return callableColor;
        }
    }

    /// <summary>
    /// Draws the landing this panel resolved to, so a panel accidentally placed nearer the wrong
    /// one shows up in the Scene view instead of only at runtime.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        NemesisElevatorLink link = elevator != null
            ? elevator
            : GetComponentInParent<NemesisElevatorLink>();

        if (link == null || link.BottomLanding == null || link.TopLanding == null) return;

        Transform served = link.IsAtBottomSide(transform.position)
            ? link.BottomLanding
            : link.TopLanding;

        Color color = new Color(1f, 0.784f, 0.314f);
        Gizmos.color = color;
        Gizmos.DrawLine(transform.position, served.position);
        Gizmos.DrawWireSphere(served.position, 0.3f);

#if UNITY_EDITOR
        // This panel's own name plus which physical landing it resolved to — the name is what
        // tells two panels on the same shaft apart, the landing is the confirmation that the one
        // it is pointing at is actually the one you meant it to serve.
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.4f,
                                  $"{name} → {served.name}");
#endif
    }
}
