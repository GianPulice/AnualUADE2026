using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// The breath meter: a small window shown while the player is inside a hiding spot, with a gauge of
/// the air left in their lungs (<see cref="PlayerStateManager.BreathAir"/>), the key that holds it
/// and one word for what the lungs are doing.
///
/// It is read out of the corner of the eye, by someone watching the Nemesis through the slats of a
/// locker, so every state has to be told apart without reading:
///   Ready       full gauge, the window dimmed. Only the prompt asks for anything.
///   Holding     the gauge drains with the air, at full brightness.
///   Running out under <see cref="lowAir"/> the gauge turns to the accent and blinks, quicker the
///               less air is left, and the window pulses with it.
///   Broken      the lungs gave out (<see cref="SO_HidingData.MaxHoldSeconds"/>): the window flashes
///               in the accent and the status blinks.
///   Exhaled     the player let go: the same beat, shorter and not in the accent.
///   Recovering  the gauge fills back up, muted, and the window flashes once when it is full.
///
/// The accent is the theme's red, and here it only ever means danger: it shows when the air is
/// about to run out and when it did, never while the meter is at rest. The text never takes it —
/// the gauge carries the colour, the lines stay phosphor white so they read over the flash.
///
/// Read-only over the player: the hidden state owns the breath and the noise it makes, this only
/// draws it. The air has no per-frame event, so it is polled through <see cref="PlayerRegistry"/>;
/// the exhale does have one (<see cref="PlayerStateManager.OnBreathExhaled"/>) and that is what
/// starts the beats, so the meter and the gasp the player hears come from the same signal.
///
/// The root carries a <see cref="ModalVisibilityGate"/> on its own CanvasGroup; this fades the
/// window's, so the two never fight over one alpha. Lives in HUDCanvas.prefab, which is the source
/// of truth for its layout: nothing is created here, the view only drives what the prefab holds.
/// Every reference below is optional — an unassigned one just leaves that part of the meter out.
/// </summary>
[DisallowMultipleComponent]
public class BreathHoldMeterView : MonoBehaviour
{
    [SerializeField] private SO_UIThemeConfig theme;

    [Tooltip("The window's own CanvasGroup, faded by this view. Not the root's: that one belongs " +
             "to the ModalVisibilityGate.")]
    [SerializeField] private CanvasGroup windowGroup;

    [Header("Gauge")]
    [Tooltip("The part of the gauge that shrinks with the air. Its right anchor (anchorMax.x) is " +
             "moved between 0 and 1, the way a Slider moves its Fill: stretch it inside the fill " +
             "area with its left anchor at 0 and no offsets.\n\n" +
             "With a RectMask2D on it and a fixed-width UIBlockFill child, the blocks are cut at " +
             "the edge of the air and the gauge drains smoothly. A UIBlockFill on this very rect " +
             "drops whole blocks instead.")]
    [SerializeField] private RectTransform fillRect;

    [Tooltip("What is painted with the state's colour: the fill's Image or UIBlockFill. No " +
             "UIThemeApplier on it — it would repaint over this view on enable.")]
    [SerializeField] private Graphic fillGraphic;

    [Tooltip("Optional, instead of the fill or next to it: one Graphic per slice of air, left to " +
             "right. The pip at the edge of the air fades with it rather than switching off.")]
    [SerializeField] private Graphic[] pips = new Graphic[0];

    [Tooltip("Optional. Stretched over the window, behind the text: washes it on the exhale beats " +
             "and pulses with the gauge while the air runs out. This view owns its colour and " +
             "alpha, so no UIThemeApplier on it either.")]
    [SerializeField] private Graphic flash;

    [Header("Text")]
    [Tooltip("The line that names the key. On screen for as long as the meter is.")]
    [SerializeField] private TextMeshProUGUI promptText;

    [Tooltip("The line that says what the lungs are doing.")]
    [SerializeField] private TextMeshProUGUI statusText;

    [Tooltip("{0} = the hold-breath key or button, read from the action's binding for the device " +
             "in use.")]
    [FormerlySerializedAs("readyFormat")]
    [SerializeField] private string promptFormat = "[{0}] HOLD BREATH";

    [Tooltip("Goes between the brackets only if the action has no binding to read.")]
    [SerializeField] private string fallbackKey = "F";

    [Tooltip("Status with full lungs and no hold. Empty = nothing, the prompt says it all. With " +
             "no Prompt Text assigned the prompt line is shown here instead.")]
    [SerializeField] private string readyText = "";

    [Tooltip("Status while the player holds their breath.")]
    [SerializeField] private string holdingText = "HOLDING";

    [Tooltip("Status right after the player lets go of a hold.")]
    [SerializeField] private string exhaledText = "EXHALE";

    [Tooltip("Status right after the lungs gave out on their own.")]
    [SerializeField] private string brokenText = "OUT OF AIR";

    [Tooltip("Status while the air comes back.")]
    [SerializeField] private string recoveringText = "RECOVERING";

    [Header("Window")]
    [Tooltip("Seconds the window takes to fade, in and out and between resting and in use.")]
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.25f;

    [Tooltip("Opacity of the window at rest: full lungs, no hold. Below 1 it steps back while " +
             "there is nothing to read and comes forward the moment the player holds. 1 = always " +
             "fully opaque.")]
    [SerializeField, Range(0.2f, 1f)] private float idleAlpha = 0.75f;

    [Header("Running out of air")]
    [Tooltip("Below this much air a hold turns urgent: the gauge switches to the accent colour and " +
             "starts to blink.")]
    [SerializeField, Range(0f, 1f)] private float lowAir = 0.3f;

    [Tooltip("Blinks per second when the air drops under Low Air.")]
    [SerializeField, Min(0f)] private float lowAirBlinkHz = 2f;

    [Tooltip("Blinks per second with no air left. The rate climbs in a straight line from Low Air " +
             "Blink Hz to this as the air runs out. Also the rate of the Broken status. Kept inside " +
             "the 0.5 to 3 blinks per second the rest of the HUD's readouts use.")]
    [SerializeField, Min(0f)] private float emptyBlinkHz = 3f;

    [Tooltip("Opacity of the gauge on the off half of a blink. Above 0 so how much air is left " +
             "still reads between two blinks.")]
    [SerializeField, Range(0f, 1f)] private float blinkDimAlpha = 0.3f;

    [Tooltip("Opacity of the flash, in the accent colour, on the on half of a blink. 0 = only the " +
             "gauge blinks.")]
    [SerializeField, Range(0f, 1f)] private float urgencyWashAlpha = 0.16f;

    [Header("Beats")]
    [Tooltip("Seconds the Broken status stays up, blinking, after the lungs give out.")]
    [SerializeField, Min(0f)] private float brokenBeatSeconds = 1.4f;

    [Tooltip("Opacity the flash starts at when the lungs give out. Accent colour.")]
    [SerializeField, Range(0f, 1f)] private float brokenFlashAlpha = 0.6f;

    [Tooltip("Seconds the Exhaled status stays up after the player lets go of a hold.")]
    [SerializeField, Min(0f)] private float exhaledBeatSeconds = 0.7f;

    [Tooltip("Opacity the flash starts at when the player lets go of a hold. Text colour, not the " +
             "accent: letting go is the player's choice, running out is not.")]
    [SerializeField, Range(0f, 1f)] private float exhaledFlashAlpha = 0.2f;

    [Tooltip("Opacity the flash starts at when the lungs are full again. 0 = no flash.")]
    [SerializeField, Range(0f, 1f)] private float readyFlashAlpha = 0.15f;

    [Tooltip("Seconds any of the flashes takes to fade out.")]
    [SerializeField, Min(0.01f)] private float flashSeconds = 0.5f;

    private enum Phase { Hidden, Ready, Holding, Exhaled, Broken, Recovering }

    // Control scheme names of InputSystem_Actions: the same two InputHint reads its labels from.
    private const string KeyboardGroup = "Keyboard&Mouse";
    private const string GamepadGroup = "Gamepad";

    // BreathAir climbs back to 1 in float steps: this close counts as full lungs.
    private const float FullAir = 0.999f;

    private Phase phase = Phase.Hidden;
    private float alpha;

    // The player whose exhale this is listening to, so the handler comes off the same one it went on.
    private PlayerStateManager hooked;

    // Seconds the Exhaled / Broken status still has to run, and which of the two it is.
    private float beatLeft;
    private bool beatForced;

    // 0..1 through one blink, lit for the first half. Accumulated, because its rate changes with
    // the air: derived from the clock it would jump every time the rate moved.
    private float blinkPhase;

    private float flashLeft;
    private float flashPeak;
    private Color flashColor;

    // The prompt, formatted. Rebuilt when the meter appears and when the player changes device,
    // never per frame: building it allocates.
    private string promptLine = string.Empty;
    private bool promptForGamepad;

    // The fill last written to the rect, so an unchanged gauge does not dirty the layout every frame.
    private float appliedFill = -1f;

    // Awake/OnDestroy, not OnEnable/OnDisable: PlayerRegistry is a static bus (docs/UI-System.md §7.1).
    private void Awake()
    {
        if (windowGroup != null)
        {
            windowGroup.alpha = 0f;
            windowGroup.interactable = false;
            windowGroup.blocksRaycasts = false;
        }

        DrawFlash(0f, 0f);

        PlayerRegistry.SubscribeAndCatchUp(HandlePlayerRegistered);
        PlayerRegistry.OnPlayerUnregistered += HandlePlayerUnregistered;
    }

    private void OnDestroy()
    {
        PlayerRegistry.Unsubscribe(HandlePlayerRegistered);
        PlayerRegistry.OnPlayerUnregistered -= HandlePlayerUnregistered;
        Unhook();
    }

    // ── Player ──────────────────────────────────────────────────────────────

    private void HandlePlayerRegistered(PlayerStateManager player)
    {
        Unhook();
        hooked = player;
        hooked.OnBreathExhaled += HandleExhaled;
    }

    private void HandlePlayerUnregistered(PlayerStateManager player)
    {
        if (ReferenceEquals(player, hooked)) Unhook();
    }

    private void Unhook()
    {
        if (hooked != null) hooked.OnBreathExhaled -= HandleExhaled;
        hooked = null;
    }

    private void HandleExhaled(bool forced)
    {
        // Only a breath the meter was showing. Hidden with no spot at all (the F10 console's Hide
        // toggle) still exhales, and there is no meter up to answer it.
        if (phase == Phase.Hidden) return;

        beatForced = forced;
        beatLeft = forced ? brokenBeatSeconds : exhaledBeatSeconds;

        if (forced) StartFlash(Accent, brokenFlashAlpha);
        else StartFlash(Primary, exhaledFlashAlpha);
    }

    // ── Update ──────────────────────────────────────────────────────────────

    private void Update()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        bool visible = player != null && player.CurrentHidingSpot != null && !player.IsDisabled;

        // Unscaled, like every other HUD fade.
        float dt = Time.unscaledDeltaTime;

        if (!visible)
        {
            phase = Phase.Hidden;
            Fade(0f, dt);
            return;
        }

        // A new stay in a spot starts clean: nothing left over from the last one.
        bool appearing = phase == Phase.Hidden;
        if (appearing)
        {
            beatLeft = 0f;
            flashLeft = 0f;
            blinkPhase = 0f;
        }

        // The bracket follows the device in use, like the input hints do.
        bool gamepad = InputHintEvents.UsingGamepad;
        if (appearing || gamepad != promptForGamepad) BuildPrompt(gamepad);

        // The breath itself stands still while a menu is up (PlayerHiddenState.TickBreathing), so
        // the beats and the blink wait with it instead of playing out behind the menu.
        float tick = PauseManager.IsGameplayInputBlocked ? 0f : dt;

        float air = Mathf.Clamp01(player.BreathAir);
        bool holding = player.IsHoldingBreath;

        Phase next;
        if (holding)
        {
            next = Phase.Holding;
            beatLeft = 0f;
        }
        else if (beatLeft > 0f)
        {
            next = beatForced ? Phase.Broken : Phase.Exhaled;
            beatLeft -= tick;
        }
        else if (air < FullAir)
        {
            next = Phase.Recovering;
        }
        else
        {
            next = Phase.Ready;
        }

        // The hold is whole again: worth a beat of its own, or the only tell would be a grey bar
        // turning a slightly lighter grey.
        if (next == Phase.Ready && phase == Phase.Recovering) StartFlash(Primary, readyFlashAlpha);

        bool urgent = next == Phase.Holding && air < lowAir;
        bool alarm = urgent || next == Phase.Broken;

        if (alarm)
        {
            float hz = urgent ? Mathf.Lerp(emptyBlinkHz, lowAirBlinkHz, air / lowAir) : emptyBlinkHz;
            blinkPhase = Mathf.Repeat(blinkPhase + hz * tick, 1f);
        }
        else
        {
            blinkPhase = 0f;
        }

        // A hard blink, not a soft pulse: on or dimmed, like every other readout in this HUD.
        bool lit = blinkPhase < 0.5f;

        Color gauge;
        switch (next)
        {
            case Phase.Holding: gauge = urgent ? Accent : Primary; break;
            case Phase.Broken:  gauge = Accent; break;
            case Phase.Ready:   gauge = Secondary; break;
            default:            gauge = Muted; break;
        }
        if (alarm && !lit) gauge.a *= blinkDimAlpha;

        DrawGauge(air, gauge);

        switch (next)
        {
            case Phase.Holding:    SetStatus(holdingText, Primary); break;
            case Phase.Broken:     SetStatus(brokenText, lit ? Primary : Muted); break;
            case Phase.Exhaled:    SetStatus(exhaledText, Primary); break;
            case Phase.Recovering: SetStatus(recoveringText, Muted); break;
            default:               SetStatus(promptText != null ? readyText : promptLine, Secondary); break;
        }

        // Never dimmed below Secondary: a hold can start again at any point of the recovery, so the
        // key has to stay readable through all of it.
        if (promptText != null) promptText.color = next == Phase.Holding ? Primary : Secondary;

        flashLeft = Mathf.Max(0f, flashLeft - tick);
        DrawFlash(flashPeak * flashLeft / flashSeconds, urgent && lit ? urgencyWashAlpha : 0f);

        phase = next;
        Fade(next == Phase.Ready ? idleAlpha : 1f, dt);
    }

    private void Fade(float target, float dt)
    {
        alpha = Mathf.MoveTowards(alpha, target, dt / fadeSeconds);
        if (windowGroup != null) windowGroup.alpha = alpha;
    }

    // ── Gauge ───────────────────────────────────────────────────────────────

    private void DrawGauge(float air, Color color)
    {
        if (fillRect != null && !Mathf.Approximately(appliedFill, air))
        {
            appliedFill = air;

            Vector2 max = fillRect.anchorMax;
            max.x = air;
            fillRect.anchorMax = max;
        }

        if (fillGraphic != null) fillGraphic.color = color;

        if (pips == null) return;

        int count = pips.Length;
        Color empty = Disabled;
        for (int i = 0; i < count; i++)
        {
            if (pips[i] == null) continue;

            // 1 for a pip the air covers, 0 for one it has not reached, in between at its edge.
            float covered = Mathf.Clamp01(air * count - i);
            pips[i].color = Color.Lerp(empty, color, covered);
        }
    }

    // ── Flash ───────────────────────────────────────────────────────────────

    private void StartFlash(Color color, float peak)
    {
        flashColor = color;
        flashPeak = peak;
        flashLeft = flashSeconds;
    }

    /// <summary>
    /// One Graphic for both: the beat left over from an exhale, and the wash of a blink while the
    /// air runs out. Whichever is stronger shows; the wash is always the accent.
    /// </summary>
    private void DrawFlash(float beat, float wash)
    {
        if (flash == null) return;

        Color color = wash > beat ? Accent : flashColor;
        color.a = Mathf.Max(beat, wash);
        flash.color = color;
    }

    // ── Text ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The binding, not a hard-coded F: the key can be rebound and a pad has a button of its own.
    /// Read the way the input hints read theirs, so both name a key the same way.
    /// </summary>
    private void BuildPrompt(bool gamepad)
    {
        promptForGamepad = gamepad;

        InputAction action = GameInput.HoldBreath;
        string key = InputHintEvents.BindingLabel(action, gamepad ? GamepadGroup : KeyboardGroup);

        // A pad with no binding for this: better the keyboard key than an empty bracket.
        if (string.IsNullOrEmpty(key) && gamepad) key = InputHintEvents.BindingLabel(action, KeyboardGroup);
        if (string.IsNullOrEmpty(key) && action != null) key = action.GetBindingDisplayString(0);
        if (string.IsNullOrEmpty(key)) key = fallbackKey;

        promptLine = string.Format(promptFormat, key.ToUpperInvariant());

        if (promptText != null && promptText.text != promptLine) promptText.text = promptLine;
    }

    private void SetStatus(string text, Color color)
    {
        if (statusText == null) return;

        if (statusText.text != text) statusText.text = text;
        statusText.color = color;
    }

    private Color Primary   => theme != null ? theme.TextPrimary : Color.white;
    private Color Secondary => theme != null ? theme.TextSecondary : new Color(0.8f, 0.8f, 0.8f);
    private Color Muted     => theme != null ? theme.TextMuted : Color.gray;
    private Color Disabled  => theme != null ? theme.TextDisabled : new Color(0.3f, 0.3f, 0.3f);
    private Color Accent    => theme != null ? theme.Accent : Color.white;
}
