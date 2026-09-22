using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The active module's countdown on the gameplay HUD: a small Win95 window in the top-left corner
/// with the MM:SS time inside a draining block ring, the module's name, one pip per module and a
/// popup for time jumps ("-5s" on a skill check miss, "+3s" on a perfect hit).
///
/// The inventory has its own copy of this information (<see cref="ModuleHUDView"/>); this one exists
/// because the timer has to be readable while playing, not only with the inventory open.
///
/// Lifecycle:
///  • Slides in when a module goes Active, and stays while it runs.
///  • When that module resolves or explodes it stays on the outcome (RESOLVED / EXPLODED) until the
///    next module goes Active (optionally slides out, see <see cref="hideWhenSettled"/>).
///  • Slides out when the Nemesis grabs the player and back in once the player is up with control
///    again (<see cref="PlayerStateManager.IsRecoveringFromCapture"/>) — the same span the module
///    timer is frozen, so it comes back showing the time it left with.
///  • Under ≤ the beeper's warning threshold the time and the ring turn Accent and blink, pulsing in
///    step with each <see cref="ModuleTimerBeeper.Beeped"/>.
///
/// Always active: shown and hidden by the slide's CanvasGroup (and the root's
/// <see cref="ModalVisibilityGate"/>, which keeps it up over the skill check), never SetActive, so
/// the static subscriptions below never miss an event. Every animation runs on unscaled time.
/// </summary>
public class ModuleTimerHUDView : MonoBehaviour
{
    [Header("Theme")]
    [SerializeField] private SO_UIThemeConfig theme;

    [Header("Window")]
    [Tooltip("On the window, not on this root: the slide cancels every tween on its own object.")]
    [SerializeField] private UISlideTransition slide;

    [Header("Timer")]
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text moduleLabel;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private UIRingArc ring;
    [Tooltip("Time and ring color while the module is running (not in warning).")]
    [SerializeField] private Color timerColor = new Color(1f, 0.6f, 0f); // amber/orange
    [Tooltip("Static background track behind the ring (sibling named \"RingTrack\"). Optional — " +
             "found automatically next to ring if left empty.")]
    [SerializeField] private UIRingArc ringTrack;
    [Tooltip("Dim amber-gray shade for the track, instead of the theme's neutral gray.")]
    [SerializeField] private Color ringTrackColor = new Color(0.32f, 0.24f, 0.12f); // amber shadow
    [Tooltip("Scaled on every beep. Its own object, so the pulse does not fight the slide.")]
    [SerializeField] private RectTransform pulseTarget;

    [Header("Modules")]
    [Tooltip("One per module, in config order. Extra pips are hidden.")]
    [SerializeField] private Graphic[] pips = new Graphic[0];

    [Header("Time jump popup")]
    [SerializeField] private TMP_Text deltaText;
    [SerializeField, Min(0f)] private float deltaRise = 18f;
    [SerializeField, Min(0.1f)] private float deltaDuration = 1.1f;

    [Header("Beeper")]
    [Tooltip("Optional. Sets the warning threshold and drives the pulse; without it the view uses " +
             "fallbackWarningSeconds and does not pulse.")]
    [SerializeField] private ModuleTimerBeeper beeper;
    [SerializeField, Min(0f)] private float fallbackWarningSeconds = 30f;

    [Header("Feel")]
    [Tooltip("Slide the window out Settled Hold Seconds after a module resolves or explodes. Off = " +
             "it stays on the outcome until the next module starts.")]
    [SerializeField] private bool hideWhenSettled = false;
    [SerializeField, Min(0f)] private float settledHoldSeconds = 2f;
    [Tooltip("Blink cycles per second of the time while in warning.")]
    [SerializeField, Min(0.1f)] private float blinkSpeed = 2f;
    [SerializeField, Min(1f)] private float pulseScale = UITweenDefaults.FeedbackOvershoot;
    [SerializeField, Min(0.02f)] private float pulseDuration = 0.16f;

    private ModuleRuntime shown;
    private bool isVisible;        // the window is on screen (slid in)
    private bool captureHidden;    // the player is caught / getting up: out of the way until control is back
    private bool cinematicHidden;  // a shot that wants a clean screen (CinematicState.HudHidden)
    private bool inWarning;
    private float blinkTime;
    private Vector2 deltaRestPosition;

    private float WarningSeconds => beeper != null ? beeper.WarningStartSeconds : fallbackWarningSeconds;

    // -- Unity -------------------

    private void Awake()
    {
        if (ringTrack == null && ring != null)
            ringTrack = ring.transform.parent.Find("RingTrack")?.GetComponent<UIRingArc>();
        if (ringTrack != null) ringTrack.color = ringTrackColor;

        ModuleEvents.OnStateChanged += HandleStateChanged;
        ModuleEvents.OnTimerTick += HandleTimerTick;
        ModuleEvents.OnTimeAdjusted += HandleTimeAdjusted;
        if (beeper != null) beeper.Beeped += HandleBeeped;

        if (deltaText != null)
        {
            deltaRestPosition = deltaText.rectTransform.anchoredPosition;
            deltaText.alpha = 0f;
        }
    }

    private void OnDestroy()
    {
        ModuleEvents.OnStateChanged -= HandleStateChanged;
        ModuleEvents.OnTimerTick -= HandleTimerTick;
        ModuleEvents.OnTimeAdjusted -= HandleTimeAdjusted;
        if (beeper != null) beeper.Beeped -= HandleBeeped;

        LeanTween.cancel(gameObject);
        if (pulseTarget != null) LeanTween.cancel(pulseTarget.gameObject);
        if (deltaText != null) LeanTween.cancel(deltaText.gameObject);
    }

    private void Start()
    {
        // The manager lives in another persistent scene and may have come up after this Awake: catch
        // up with a module that was already running when the HUD appeared.
        if (!ModuleManager.Exists) return;
        ModuleRuntime active = ModuleManager.Instance.GetActiveModule();
        if (active != null) Show(active);
    }

    private void Update()
    {
        // Polled, not an event: the flag lives on whichever player is in the level, and a player
        // unloaded mid-capture simply stops reporting it — nothing is left stuck hidden.
        PlayerStateManager player = PlayerRegistry.Current;
        bool caught = player != null && player.IsRecoveringFromCapture;
        if (caught != captureHidden)
        {
            captureHidden = caught;
            ApplyVisibility();
        }

        // Taken off at once, not slid: it goes on the frame of a hard cut, and a window sliding out
        // over the new shot is exactly what the shot wants gone.
        bool cinematic = CinematicState.HudHidden;
        if (cinematic != cinematicHidden)
        {
            cinematicHidden = cinematic;
            ApplyVisibility(instant: cinematic);
        }

        if (!inWarning || shown == null || timerText == null) return;

        blinkTime += Time.unscaledDeltaTime * blinkSpeed;
        float alpha = Mathf.Lerp(0.35f, 1f, (Mathf.Sin(blinkTime * Mathf.PI * 2f) + 1f) * 0.5f);
        timerText.alpha = alpha;
    }

    // -- Events -------------------

    private void HandleStateChanged(ModuleRuntime module)
    {
        if (module == null) return;

        if (module.Status == ModuleStatus.Active)
        {
            Show(module);
            return;
        }

        RefreshPips();

        if (module == shown && (module.Status == ModuleStatus.Resolved || module.Status == ModuleStatus.Exploded))
            Settle(module);
    }

    private void HandleTimerTick(ModuleRuntime module)
    {
        if (module != shown) return;
        RefreshTime(module);
    }

    private void HandleTimeAdjusted(ModuleRuntime module, float delta)
    {
        if (module != shown) return;
        RefreshTime(module);
        PopDelta(delta);
    }

    private void HandleBeeped(bool urgent)
    {
        if (shown == null || pulseTarget == null) return;

        LeanTween.cancel(pulseTarget.gameObject);
        pulseTarget.localScale = Vector3.one * pulseScale;
        LeanTween.scale(pulseTarget.gameObject, Vector3.one, pulseDuration)
                 .setEase(LeanTweenType.easeOutQuad)
                 .setIgnoreTimeScale(true);
    }

    // -- States -------------------

    private void Show(ModuleRuntime module)
    {
        // A pending "slide out after the outcome" from the previous module must not hide this one.
        LeanTween.cancel(gameObject);

        shown = module;
        inWarning = false;

        if (moduleLabel != null) moduleLabel.text = LabelFor(module);
        SetStatus("T-MINUS", theme != null ? theme.TextMuted : Color.gray);
        RefreshTime(module);
        RefreshPips();
        ApplyVisibility();
    }

    /// <summary>
    /// Freezes the window on the outcome and leaves it there: the window sliding away the moment a
    /// module ends read as the HUD breaking. The next module going Active replaces it (<see cref="Show"/>).
    /// </summary>
    private void Settle(ModuleRuntime module)
    {
        inWarning = false;
        bool resolved = module.Status == ModuleStatus.Resolved;

        Color outcome = resolved ? module.BarColor : Accent;
        if (timerText != null)
        {
            timerText.text = resolved ? FormatTime(module.TimeRemaining) : "00:00";
            timerText.color = outcome;
        }
        if (ring != null)
        {
            ring.SetSweep(resolved ? 360f : 0f);
            ring.color = outcome;
        }
        SetStatus(resolved ? "RESOLVED" : "EXPLODED", outcome);

        LeanTween.cancel(gameObject);
        if (hideWhenSettled)
            LeanTween.delayedCall(gameObject, settledHoldSeconds, Hide).setIgnoreTimeScale(true);
    }

    private void Hide()
    {
        shown = null;
        inWarning = false;
        ApplyVisibility();
    }

    /// <summary>
    /// On screen = a module to show AND the player not caught AND no shot asking for a clean screen.
    /// Slides only on a change, so a capture takes the window out and the stand-up brings it back
    /// in, and the timer it shows again is the one that was frozen during the capture.
    ///
    /// <paramref name="instant"/> takes it off in the same frame instead of sliding: the window is
    /// a child of this root, so switching it off leaves the subscriptions here running, and
    /// <see cref="UISlideTransition.SlideIn"/> switches it back on by itself.
    /// </summary>
    private void ApplyVisibility(bool instant = false)
    {
        bool target = shown != null && !captureHidden && !cinematicHidden;
        if (target == isVisible || slide == null) return;

        if (target) slide.SlideIn(SlideDirection.FromLeft);
        else if (instant) slide.gameObject.SetActive(false);
        else slide.SlideOut(SlideDirection.FromLeft);
        isVisible = target;
    }

    // -- Drawing -------------------

    private void RefreshTime(ModuleRuntime module)
    {
        float left = module.TimeRemaining;
        bool warning = left <= WarningSeconds;

        if (warning != inWarning)
        {
            inWarning = warning;
            blinkTime = 0f;
        }

        if (timerText != null)
        {
            timerText.text = FormatTime(left);
            Color c = timerColor;
            // The blink owns the alpha while in warning.
            c.a = warning ? timerText.alpha : 1f;
            timerText.color = c;
        }

        if (ring != null)
        {
            ring.SetSweep(360f * module.TimerProgress);
            ring.color = warning ? Accent : timerColor;
        }
    }

    private void RefreshPips()
    {
        if (pips == null || pips.Length == 0 || !ModuleManager.Exists) return;

        IReadOnlyList<ModuleRuntime> modules = ModuleManager.Instance.GetAllModules();
        for (int i = 0; i < pips.Length; i++)
        {
            Graphic pip = pips[i];
            if (pip == null) continue;

            bool used = i < modules.Count;
            pip.gameObject.SetActive(used);
            if (!used) continue;

            ModuleRuntime m = modules[i];
            pip.color = m.Status == ModuleStatus.Active ? Primary : m.BarColor;
        }
    }

    private void PopDelta(float delta)
    {
        if (deltaText == null || Mathf.Approximately(delta, 0f)) return;

        int seconds = Mathf.RoundToInt(Mathf.Abs(delta));
        if (seconds == 0) return;

        deltaText.text = (delta < 0f ? "-" : "+") + seconds + "s";
        deltaText.color = delta < 0f ? Accent : Primary;

        GameObject host = deltaText.gameObject;
        RectTransform rt = deltaText.rectTransform;
        LeanTween.cancel(host);
        rt.anchoredPosition = deltaRestPosition;
        deltaText.alpha = 1f;

        LeanTween.value(host, 0f, 1f, deltaDuration)
                 .setOnUpdate(t =>
                 {
                     rt.anchoredPosition = deltaRestPosition + Vector2.up * (deltaRise * t);
                     // Holds full for the first half, then fades: a jump has to be readable.
                     deltaText.alpha = t < 0.5f ? 1f : 1f - (t - 0.5f) * 2f;
                 })
                 .setEase(LeanTweenType.easeOutQuad)
                 .setIgnoreTimeScale(true);
    }

    private void SetStatus(string text, Color color)
    {
        if (statusText == null) return;
        statusText.text = text;
        statusText.color = color;
    }

    private Color Primary => theme != null ? theme.TextPrimary : Color.white;
    private Color Accent => theme != null ? theme.Accent : new Color(0.8f, 0.1f, 0.1f);

    /// <summary>"M2 // CHEST". Falls back to the id when the module has no short label.</summary>
    private static string LabelFor(ModuleRuntime module)
    {
        string label = string.IsNullOrEmpty(module.ModuleLogLabel) ? module.ModuleID : module.ModuleLogLabel;
        return module.Data != null
            ? $"{label} // {module.Data.Penalty.ToString().ToUpperInvariant()}"
            : label;
    }

    /// <summary>MM:SS, flooring the seconds like <see cref="ModuleRuntime.FormattedTime"/> — which
    /// cannot be used here because it returns "--:--" once the module stops.</summary>
    private static string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int minutes = Mathf.FloorToInt(seconds / 60f);
        int secs = Mathf.FloorToInt(seconds % 60f);
        return $"{minutes:00}:{secs:00}";
    }
}
