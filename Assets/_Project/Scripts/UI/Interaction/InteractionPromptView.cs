using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The interaction prompt: a small Win95 window with a phosphor command line inside it.
///
/// It shows three KINDS of message in the same slot, each looking slightly different so the player
/// can tell them apart at a glance without reading:
///   Common — doors, valves, panels, notes. Dark title bar, key cap, no well.
///   Item   — picking something up or putting it in a socket. Adds the item's icon in a sunken well.
///   Global — the game talking rather than the thing being looked at (see
///            <see cref="InteractionEvents.OnGlobalMessage"/>). Inverted title bar, "!" glyph, no
///            key cap, no cursor, and it slides in from the side and leaves on its own.
///
/// Which kind a target uses comes from the optional <see cref="IPromptPresentation"/>; anything that
/// does not implement it is Common.
///
/// The title bar is painted here from <see cref="SO_UIThemeConfig"/> rather than by a
/// UIThemeApplier: the applier repaints on enable and would overwrite the per-kind colour. Every
/// other node of the window is themed by UIStyle_InteractionCanvas.
/// </summary>
public class InteractionPromptView : BaseScreenView
{
    /// <summary>One look. Three of these are authored in the Inspector, one per kind.</summary>
    [Serializable]
    public class PromptVariant
    {
        public string title = @"C:\WIRED\INTERACT.EXE";
        public UIThemeRole titleBarRole = UIThemeRole.SurfaceFooter;
        public UIThemeRole titleTextRole = UIThemeRole.TextSecondary;
        [Tooltip("Show the [E] key cap. Off for messages the player cannot answer.")]
        public bool showKey = true;
        [Tooltip("Blinking terminal cursor at the end of the line.")]
        public bool blinkCursor = true;
        public SlideDirection enterDirection = SlideDirection.FromBottom;
    }

    [Header("Window")]
    [SerializeField] private SO_UIThemeConfig theme;
    [SerializeField] private Image titleBar;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private GameObject keyCapRoot;
    [SerializeField] private GameObject iconWell;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI glyphLabel;

    [Header("Message")]
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private TMPTypewriterReveal typewriter;
    [SerializeField] private Color normalColor = new Color(0.878f, 0.878f, 0.878f, 1f);
    [SerializeField] private Color infoColor = new Color(0.533f, 0.533f, 0.533f, 1f);
    [SerializeField] private UISlideTransition slide;

    [Header("Command line")]
    [SerializeField] private string commandPrefix = "> ";
    [SerializeField] private bool uppercase = true;
    [SerializeField] private string cursorCharacter = "_";
    [Tooltip("The one chromatic accent of the window. Everything else is phosphor white.")]
    [SerializeField] private Color cursorColor = new Color(0.8f, 0.102f, 0.102f, 1f);
    [SerializeField, Min(0.05f)] private float cursorBlinkInterval = 0.5f;

    [Header("Variants")]
    [SerializeField] private PromptVariant commonVariant = new PromptVariant();
    [SerializeField] private PromptVariant itemVariant = new PromptVariant
    {
        title = @"C:\WIRED\ITEM.DAT",
    };
    [SerializeField] private PromptVariant globalVariant = new PromptVariant
    {
        title = @"C:\WIRED\SYSTEM.MSG",
        titleBarRole = UIThemeRole.BevelLight,
        titleTextRole = UIThemeRole.SurfaceScreen,
        showKey = false,
        blinkCursor = false,
        enterDirection = SlideDirection.FromLeft,
    };

    [Header("Layout")]
    [Tooltip("Left margin of the message when no slot is shown. Canvas units.")]
    [SerializeField] private float textInsetBase = 16f;
    [Tooltip("Extra left margin taken by the key cap.")]
    [SerializeField] private float keySlotWidth = 44f;
    [Tooltip("Extra left margin taken by the icon well.")]
    [SerializeField] private float iconSlotWidth = 48f;
    [SerializeField] private float textInsetRight = 16f;

    [Header("Auto-pickup notice")]
    [Tooltip("Format of the global message shown when an item is granted to the inventory WITHOUT " +
             "a world pickup (puzzle reward, scripted grant). {0} is the item name.")]
    [SerializeField] private string autoPickupFormat = "{0} added to inventory";

    [Tooltip("Seconds the auto-pickup notice stays on screen before returning to the normal " +
             "interaction prompt state. Counted in SCALED time, so a paused modal freezes the " +
             "timer and it resumes with the remaining seconds when the modal closes.")]
    [SerializeField, Min(0f)] private float autoPickupSeconds = 3f;

    private IInteractable currentTarget;

    // Global-message state — event-driven and independent from the interaction system. While one is
    // up this view ignores TargetChanged / RequestPromptRefresh so the message is not overwritten
    // by an interactable the crosshair happens to land on mid-message. Cleared by a timer, at which
    // point RefreshDisplay re-syncs the UI with whatever the interaction state is at that moment.
    private bool showingGlobal;
    private Coroutine globalRoutine;

    // A message raised while a modal is open (e.g. the sequence panel completes and hands an item)
    // is deferred here until the modal closes. Without this the notice would start counting down
    // while the panel is still on top of it, and be gone the moment the panel closes. Only the
    // latest pending message is kept — a stale one from a previous modal has no reason to surface
    // after a newer one arrives.
    private string pendingMessage;
    private float pendingSeconds;

    // The line without its cursor. Kept so the blink can rewrite the label without re-running the
    // typewriter — see the replay rule below.
    private string currentBody = string.Empty;
    private bool cursorEnabled;
    private bool cursorOn = true;
    private float cursorTimer;

    // Whether the window is on screen, and what it was last drawn for. Together they decide when the
    // typewriter replays: the line is typed out every time the window APPEARS (looking at the same
    // door twice types it twice, and so does coming back from a modal), but not when something
    // merely refreshes a prompt that is already up — an inventory change, a state refresh — which
    // would restart the animation under the player's eyes for no reason.
    private bool visible;
    private IInteractable lastRenderedTarget;

    private void Awake()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        InteractionEvents.OnTargetChanged        += HandleTargetChanged;
        InteractionEvents.OnPromptRefreshRequested += HandlePromptRefreshRequested;
        InteractionEvents.OnGlobalMessage        += HandleGlobalMessage;
        InventoryEvents.OnItemAdded              += HandleInventoryChanged;
        InventoryEvents.OnItemRemoved            += HandleInventoryChanged;
        InventoryEvents.OnItemAutoAdded          += HandleItemAutoAdded;
        UIStateManager.OnModalPushed             += HandleModalPushed;
        UIStateManager.OnModalPopped             += HandleModalPopped;
    }

    private void OnDestroy()
    {
        InteractionEvents.OnTargetChanged        -= HandleTargetChanged;
        InteractionEvents.OnPromptRefreshRequested -= HandlePromptRefreshRequested;
        InteractionEvents.OnGlobalMessage        -= HandleGlobalMessage;
        InventoryEvents.OnItemAdded              -= HandleInventoryChanged;
        InventoryEvents.OnItemRemoved            -= HandleInventoryChanged;
        InventoryEvents.OnItemAutoAdded          -= HandleItemAutoAdded;
        UIStateManager.OnModalPushed             -= HandleModalPushed;
        UIStateManager.OnModalPopped             -= HandleModalPopped;
    }

    // Safety net for the deferred-message path: OnModalPopped is the primary trigger to release a
    // pending one, but some UIs close without pushing/popping through UIStateManager (or pop
    // one modal while another is still on top). Polling here fires the message on the first frame
    // after every modal is gone, regardless of which event surfaced that fact. Costs a couple of
    // property checks per frame when idle — pendingMessage is null and the branch exits early.
    private void Update()
    {
        TickCursor();

        if (pendingMessage == null) return;
        if (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen) return;

        string message = pendingMessage;
        float seconds = pendingSeconds;
        pendingMessage = null;
        StartGlobal(message, seconds);
    }

    // Unscaled: the cursor keeps blinking while a modal has frozen the game, the same way the fades
    // of BaseScreenView do.
    private void TickCursor()
    {
        if (!cursorEnabled) return;

        cursorTimer += Time.unscaledDeltaTime;
        if (cursorTimer < cursorBlinkInterval) return;

        cursorTimer = 0f;
        cursorOn = !cursorOn;
        RenderLine();
    }

    private void HandlePromptRefreshRequested()
    {
        if (showingGlobal) return;
        RefreshDisplay(animate: false);
    }

    private void HandleTargetChanged(IInteractable target)
    {
        // Treat destroyed interactables (e.g. a pickup destroyed the frame it was consumed)
        // as null. IInteractable is an interface, so the raw `!= null` check on the field
        // skips UnityEngine.Object's fake-null overload — see IsAlive.
        if (!IsAlive(target)) target = null;

        currentTarget = target;

        // A live AND actionable target dismisses a global message for good: the player
        // choosing to look at something they can act on is a stronger signal than the tail end of
        // an announcement, and per design we do NOT resume the message afterwards. A target
        // that is not currently interactable (a box already locked into its basket, an info-only
        // prop) leaves it alone — the interaction prompt would have nothing to show
        // anyway, so hiding the message for it would only lose information.
        if (showingGlobal && target != null && target.CanInteract())
        {
            CancelGlobal();
            // Fall through into the normal "target != null" branch below so the prompt is shown
            // immediately in this same call instead of waiting for the next TargetChanged.
        }
        else if (showingGlobal)
        {
            return;
        }

        if (target != null)
        {
            RefreshDisplay(animate: true);
            slide?.SlideIn(VariantFor(target).enterDirection);
        }
        else
        {
            HideWindow();
            slide?.SlideOut();
        }
    }

    private void HandleInventoryChanged(SO_InventoryItem _)
    {
        if (showingGlobal) return;
        RefreshDisplay(animate: false);
    }

    /// <summary>
    /// An item that reached the inventory with no world pickup becomes a global message. This is
    /// the first caller of that path; it is routed through the same private entry point as
    /// <see cref="InteractionEvents.OnGlobalMessage"/> rather than re-raising the event, so the
    /// view does not listen to itself.
    /// </summary>
    private void HandleItemAutoAdded(SO_InventoryItem item)
    {
        if (item == null || promptText == null) return;
        ShowGlobalMessage(string.Format(autoPickupFormat, item.ItemName), autoPickupSeconds);
    }

    private void HandleGlobalMessage(string text, float seconds) => ShowGlobalMessage(text, seconds);

    private void ShowGlobalMessage(string text, float seconds)
    {
        if (string.IsNullOrWhiteSpace(text) || promptText == null) return;

        // Defer while any modal (sequence panel, inventory, pause...) is open. Firing now would
        // start the countdown behind the modal and the message would be gone the second it closes.
        // HandleModalPopped — or the Update poll — picks it up then.
        if (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen)
        {
            pendingMessage = text;
            pendingSeconds = seconds;
            return;
        }

        StartGlobal(text, seconds);
    }

    private void StartGlobal(string text, float seconds)
    {
        showingGlobal = true;
        lastRenderedTarget = null;
        ApplyVariant(globalVariant, icon: null, showGlyph: true);

        // Always typed out: a global message is an arrival, never a refresh.
        SetLine(text, normalColor, forceReplay: true);

        // A second message arriving before the first ends restarts the timer with the newer
        // one — dropping the older text is preferable to queuing it, so the player never sees
        // a delayed line for something that happened several seconds ago.
        if (globalRoutine != null) StopCoroutine(globalRoutine);

        ShowWindow();
        slide?.SlideIn(globalVariant.enterDirection);

        globalRoutine = StartCoroutine(GlobalCountdown(seconds));
    }

    // Aborts the message without triggering its exit animation. Used when a live interactable
    // preempts it: the caller is about to draw the interaction prompt over the same
    // CanvasGroup, so a fade-out here would fight it.
    private void CancelGlobal()
    {
        if (globalRoutine != null)
        {
            StopCoroutine(globalRoutine);
            globalRoutine = null;
        }
        showingGlobal = false;
    }

    // Scaled deltaTime on purpose: modals set Time.timeScale to 0, and the design here is that
    // the message freezes with the game. A 2.3s-in pause must resume at 0.7s remaining after
    // unpause, not skip forward while the pause menu was open.
    private IEnumerator GlobalCountdown(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            yield return null;
        }

        showingGlobal = false;
        globalRoutine = null;

        // Re-sync with the real interaction state: if the crosshair is on something, restore its
        // prompt; otherwise fade out. Doing this in one place (RefreshDisplay + the else branch)
        // keeps the exit symmetric with HandleTargetChanged.
        if (IsAlive(currentTarget))
        {
            RefreshDisplay(animate: true);
        }
        else
        {
            HideWindow();
            slide?.SlideOut();
        }
    }

    /// <summary>
    /// Any modal (inventory, pause, settings, sequence panel, document reader...) covers the
    /// prompt instantly. InteractionCanvas has sortingOrder 100 (the highest in the project),
    /// so without this the prompt would be drawn ON TOP of any modal.
    /// Snapping without animation on purpose: the modal may set Time.timeScale to 0.
    /// </summary>
    private void HandleModalPushed(IModalUI _)
    {
        // Counts as gone: when it comes back the line is typed out again.
        visible = false;
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    /// <summary>
    /// Restore only if the InteractionManager still reports a live target — i.e. the player
    /// is right now aiming at something interactable. Restoring from a cached "last target"
    /// leaves a stale prompt pegged when the player looked away (or the target was destroyed)
    /// during the modal, and the raycast never fires a new TargetChanged because both the
    /// previous and current detected values are null. When there is no live target here, the
    /// next InteractionManager.Update fires a proper TargetChanged as soon as the raycast
    /// finds one — a one-frame gap invisible to the player.
    /// </summary>
    private void HandleModalPopped(IModalUI _)
    {
        if (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen) return;

        // A message raised while the modal was open was deferred. Fire it now that the modal is
        // gone so its full dwell starts against a visible UI, not behind the panel. Consumes the
        // pending slot so a subsequent modal pop does not replay it.
        if (pendingMessage != null)
        {
            string message = pendingMessage;
            float seconds = pendingSeconds;
            pendingMessage = null;
            StartGlobal(message, seconds);
            return;
        }

        // The countdown was frozen (Time.deltaTime == 0 under the modal) but visuals were
        // hidden by HandleModalPushed. Restore them so the remaining seconds actually show.
        if (showingGlobal)
        {
            ShowWindow();
            slide?.SlideIn(globalVariant.enterDirection);
            return;
        }

        IInteractable live = InteractionManager.Exists
            ? InteractionManager.Instance.CurrentInteractable
            : null;

        if (!IsAlive(live)) return;

        currentTarget = live;
        RefreshDisplay(animate: true);
        slide?.SlideIn(VariantFor(live).enterDirection);
    }

    private void RefreshDisplay(bool animate)
    {
        if (!IsAlive(currentTarget))
        {
            // The cached target was destroyed since the last update (typical after picking up
            // an item and then any UI event fires a refresh). Drop it so the prompt hides
            // cleanly instead of crashing on the next CanInteract call.
            currentTarget = null;
            HideWindow();
            return;
        }

        PromptVariant variant = VariantFor(currentTarget);
        Sprite icon = (currentTarget as IPromptPresentation)?.PromptIcon;

        // The window is appearing — rather than being refreshed in place — when it was off screen,
        // or when it was last drawn for a different object. Two identical doors side by side each
        // get their own typing pass that way, even though the line reads the same.
        bool appearing = !visible || !ReferenceEquals(currentTarget, lastRenderedTarget);

        if (currentTarget.CanInteract())
        {
            ApplyVariant(variant, icon, showGlyph: false);
            SetLine(currentTarget.GetInteractText(), normalColor, appearing);
            lastRenderedTarget = currentTarget;
            if (animate) ShowWindow();
        }
        else
        {
            string info = currentTarget.GetInfoText();
            if (!string.IsNullOrEmpty(info))
            {
                // Same kind, quieter: no key cap, because there is nothing to press.
                ApplyVariant(variant, icon, showGlyph: false, forceHideKey: true);
                SetLine(info, infoColor, appearing);
                lastRenderedTarget = currentTarget;
                if (animate) ShowWindow();
            }
            else
            {
                HideWindow();
            }
        }
    }

    /// <summary>Fades the window in and records that it is up, for the typewriter's benefit.</summary>
    private void ShowWindow()
    {
        visible = true;
        Fade(1f, 0.15f).Forget();
    }

    private void HideWindow()
    {
        visible = false;
        lastRenderedTarget = null;
        Fade(0f, 0.15f).Forget();
    }

    private PromptVariant VariantFor(IInteractable target) =>
        target is IPromptPresentation presentation && presentation.Kind == InteractionPromptKind.Item
            ? itemVariant
            : commonVariant;

    /// <summary>Dresses the window for one kind: title bar, key cap and icon well.</summary>
    private void ApplyVariant(PromptVariant variant, Sprite icon, bool showGlyph, bool forceHideKey = false)
    {
        if (variant == null) return;

        if (titleText != null)
        {
            titleText.text = variant.title;
            titleText.color = Role(variant.titleTextRole);
        }
        if (titleBar != null) titleBar.color = Role(variant.titleBarRole);

        bool showKey = variant.showKey && !forceHideKey;
        if (keyCapRoot != null) keyCapRoot.SetActive(showKey);

        bool showWell = showGlyph || icon != null;
        if (iconWell != null) iconWell.SetActive(showWell);
        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }
        if (glyphLabel != null) glyphLabel.gameObject.SetActive(showGlyph);

        cursorEnabled = variant.blinkCursor;
        cursorOn = true;
        cursorTimer = 0f;

        LayOutMessage(showKey, showWell);
    }

    /// <summary>
    /// Slides the message right by exactly the slots that are visible. Plain insets rather than a
    /// LayoutGroup: the window is a fixed size, so the three positions are constants, and a
    /// content-driven layout would start resizing a single-line label.
    /// </summary>
    private void LayOutMessage(bool showKey, bool showWell)
    {
        if (promptText == null) return;

        float left = textInsetBase + (showKey ? keySlotWidth : 0f) + (showWell ? iconSlotWidth : 0f);
        RectTransform rt = promptText.rectTransform;
        rt.offsetMin = new Vector2(left, rt.offsetMin.y);
        rt.offsetMax = new Vector2(-textInsetRight, rt.offsetMax.y);
    }

    /// <summary>
    /// Writes the command line. The typewriter only replays when the text actually changed: an
    /// inventory refresh or a prompt refresh on the same target must not re-type the same line.
    /// </summary>
    private void SetLine(string message, Color color, bool forceReplay)
    {
        if (promptText == null) return;

        string body = commandPrefix + (uppercase ? message.ToUpperInvariant() : message);
        bool replay = forceReplay || body != currentBody;

        currentBody = body;
        promptText.color = color;
        cursorOn = true;
        cursorTimer = 0f;
        RenderLine();

        if (replay && typewriter != null) typewriter.Play();
    }

    private void RenderLine()
    {
        if (promptText == null) return;

        if (!cursorEnabled || string.IsNullOrEmpty(cursorCharacter))
        {
            promptText.text = currentBody;
            return;
        }

        // Hidden with an alpha tag rather than by dropping the character, so the line does not
        // shift half a glyph left and right as it blinks.
        promptText.text = cursorOn
            ? currentBody + "<color=#" + ColorUtility.ToHtmlStringRGB(cursorColor) + ">" + cursorCharacter + "</color>"
            : currentBody + "<alpha=#00>" + cursorCharacter;
    }

    private Color Role(UIThemeRole role) => theme != null ? theme.Get(role) : Color.white;

    /// <summary>
    /// True if the interactable is non-null AND — when it is a Unity object — not destroyed.
    /// A plain `== null` check on an <see cref="IInteractable"/> variable does NOT hit
    /// UnityEngine.Object's operator overload (it dispatches by static type), so a destroyed
    /// MonoBehaviour would slip through and throw MissingReferenceException on the next call.
    /// </summary>
    private static bool IsAlive(IInteractable target)
    {
        if (target == null) return false;
        if (target is UnityEngine.Object unityObj) return unityObj != null;
        return true;
    }
}
