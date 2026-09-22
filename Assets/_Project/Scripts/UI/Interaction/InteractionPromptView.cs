using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The interaction prompt: a small Win95 window with a phosphor command line inside it.
///
/// It shows two KINDS of message in the same slot, each looking slightly different so the player
/// can tell them apart at a glance without reading:
///   Common — doors, valves, panels, notes. Dark title bar, key cap, no well.
///   Item   — picking something up or putting it in a socket. Adds the item's icon in a sunken well.
///
/// Which kind a target uses comes from the optional <see cref="IPromptPresentation"/>; anything that
/// does not implement it is Common.
///
/// It only describes what the player is looking at. What the game says about an interaction on its
/// own (<see cref="InteractionEvents.OnGlobalMessage"/>, items entering the inventory) is
/// <see cref="InteractionNotificationFeed"/>'s: sharing this one slot, the next prompt overwrote it.
///
/// The window is sized to its line, not the line to the window — see <see cref="FitWindow"/>.
///
/// The title bar is painted here from <see cref="SO_UIThemeConfig"/> rather than by a
/// UIThemeApplier: the applier repaints on enable and would overwrite the per-kind colour. Every
/// other node of the window is themed by UIStyle_InteractionCanvas.
/// </summary>
public class InteractionPromptView : BaseScreenView
{
    /// <summary>One look. Two of these are authored in the Inspector, one per kind.</summary>
    [Serializable]
    public class PromptVariant
    {
        public string title = @"C:\WIRED\INTERACT.EXE";
        public UIThemeRole titleBarRole = UIThemeRole.SurfaceFooter;
        public UIThemeRole titleTextRole = UIThemeRole.TextSecondary;
        [Tooltip("Show the [E] key cap. Off for messages the player cannot answer.")]
        public bool showKey = true;
        [Tooltip("Start the line with the command prefix. Off for messages the game says on its own: " +
                 "the prefix marks a line waiting for input, and those only report.")]
        public bool showPrefix = true;
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

    [Header("Layout")]
    [Tooltip("Resized to fit the line. Its pivot is on the top edge, so a line that wraps grows the " +
             "window downwards and the title bar stays put.")]
    [SerializeField] private RectTransform promptRoot;
    [Tooltip("Left margin of the message when no slot is shown. Canvas units.")]
    [SerializeField] private float textInsetBase = 16f;
    [Tooltip("Extra left margin taken by the key cap.")]
    [SerializeField] private float keySlotWidth = 44f;
    [Tooltip("Extra left margin taken by the icon well.")]
    [SerializeField] private float iconSlotWidth = 48f;
    [SerializeField] private float textInsetRight = 16f;
    [Tooltip("Space above and below a line that wraps past what the minimum height holds.")]
    [SerializeField] private float textInsetVertical = 16f;
    [Tooltip("Widest the window gets. A longer line wraps and the window grows taller instead.")]
    [SerializeField] private float maxWindowWidth = 760f;
    [Tooltip("Height of a one-row window, and the least it ever gets, so the key cap and the icon " +
             "well always fit.")]
    [SerializeField] private float minWindowHeight = 96f;

    private IInteractable currentTarget;

    // Stand-in for "no limit" when asking TMP how much room a line wants. TMP's own large value: an
    // infinity would leak into its arithmetic.
    private const float Unbounded = 32767f;

    // The line without its cursor. Kept so the blink can rewrite the label without re-running the
    // typewriter — see the replay rule below.
    private string currentBody = string.Empty;
    private bool prefixEnabled = true;
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
        InventoryEvents.OnItemAdded              += HandleInventoryChanged;
        InventoryEvents.OnItemRemoved            += HandleInventoryChanged;
        UIStateManager.OnModalPushed             += HandleModalPushed;
        UIStateManager.OnModalPopped             += HandleModalPopped;
    }

    private void OnDestroy()
    {
        InteractionEvents.OnTargetChanged        -= HandleTargetChanged;
        InteractionEvents.OnPromptRefreshRequested -= HandlePromptRefreshRequested;
        InventoryEvents.OnItemAdded              -= HandleInventoryChanged;
        InventoryEvents.OnItemRemoved            -= HandleInventoryChanged;
        UIStateManager.OnModalPushed             -= HandleModalPushed;
        UIStateManager.OnModalPopped             -= HandleModalPopped;
    }

    private void Update() => TickCursor();

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
        // A refresh may bring the window back, not only redraw it in place. A target whose state
        // has nothing to say hides it, and when that state changes under the crosshair the refresh is
        // the only thing that fires — the elevator call panel going from "cabin here" to "call it",
        // a door the crosshair found mid-swing. Only while the window is off screen, so refreshing
        // one already up does not restart its fade, and never under a modal, which covers the prompt
        // (see HandleModalPushed).
        bool mayAppear = !visible && !(UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen);
        RefreshDisplay(animate: mayAppear);
    }

    private void HandleTargetChanged(IInteractable target)
    {
        // Treat destroyed interactables (e.g. a pickup destroyed the frame it was consumed)
        // as null. IInteractable is an interface, so the raw `!= null` check on the field
        // skips UnityEngine.Object's fake-null overload — see IsAlive.
        if (!IsAlive(target)) target = null;

        currentTarget = target;

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

    private void HandleInventoryChanged(SO_InventoryItem _) => RefreshDisplay(animate: false);

    /// <summary>
    /// Any modal (inventory, pause, settings, sequence panel, document reader...) covers the
    /// prompt instantly. InteractionCanvas has sortingOrder 100, above every modal canvas (pause 70,
    /// settings 80), so without this the prompt would be drawn ON TOP of any modal.
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
            ApplyVariant(variant, icon);
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
                ApplyVariant(variant, icon, forceHideKey: true);
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
    private void ApplyVariant(PromptVariant variant, Sprite icon, bool forceHideKey = false)
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

        bool showWell = icon != null;
        if (iconWell != null) iconWell.SetActive(showWell);
        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        prefixEnabled = variant.showPrefix;
        cursorEnabled = variant.blinkCursor;
        cursorOn = true;
        cursorTimer = 0f;

        LayOutMessage(showKey, showWell);
    }

    /// <summary>
    /// Slides the message right by exactly the slots that are visible. Plain insets rather than a
    /// LayoutGroup, which would only resolve at the end of the frame: <see cref="FitWindow"/> sizes
    /// the window from these insets straight away, and the slide measures the window for its
    /// off-screen start in the same call.
    ///
    /// The icon well moves up into the key cap's place when there is no key cap. Left where it is
    /// authored — behind the key cap — it would sit on top of the start of the message.
    /// </summary>
    private void LayOutMessage(bool showKey, bool showWell)
    {
        if (iconWell != null && keyCapRoot != null)
        {
            RectTransform well = (RectTransform)iconWell.transform;
            float firstSlot = ((RectTransform)keyCapRoot.transform).anchoredPosition.x;
            well.anchoredPosition = new Vector2(firstSlot + (showKey ? keySlotWidth : 0f), well.anchoredPosition.y);
        }

        if (promptText == null) return;

        float left = textInsetBase + (showKey ? keySlotWidth : 0f) + (showWell ? iconSlotWidth : 0f);
        RectTransform rt = promptText.rectTransform;
        rt.offsetMin = new Vector2(left, rt.offsetMin.y);
        rt.offsetMax = new Vector2(-textInsetRight, rt.offsetMax.y);
    }

    /// <summary>
    /// Sizes the window to its line: as wide as the line is on one row, and once that passes
    /// <see cref="maxWindowWidth"/> the line wraps and the window grows taller instead. Never
    /// narrower than its title needs, never shorter than <see cref="minWindowHeight"/>.
    ///
    /// Measured on the whole line, cursor included — TMP's preferred values ignore
    /// maxVisibleCharacters — so the window takes its final size at once and the typewriter fills
    /// it, instead of the frame stretching letter by letter.
    /// </summary>
    private void FitWindow()
    {
        if (promptRoot == null || promptText == null) return;

        RectTransform message = promptText.rectTransform;
        float insets = message.offsetMin.x - message.offsetMax.x;

        // One unit to spare: a label exactly as wide as its line can still wrap the last word on rounding.
        float oneRow = Mathf.Ceil(promptText.GetPreferredValues(Unbounded, Unbounded).x) + 1f;
        float minWidth = TitleWidth();
        float width = Mathf.Clamp(oneRow + insets, minWidth, Mathf.Max(minWidth, maxWindowWidth));

        // Asked at the width the line really gets, so a clamped line reports every row it wraps to.
        float rows = promptText.GetPreferredValues(width - insets, Unbounded).y;
        float titleHeight = titleBar != null ? titleBar.rectTransform.rect.height : 0f;
        float height = Mathf.Max(minWindowHeight, Mathf.Ceil(titleHeight + rows + 2f * textInsetVertical));

        promptRoot.sizeDelta = new Vector2(width, height);
    }

    /// <summary>The narrowest window whose title still clears the caps on its right.</summary>
    private float TitleWidth()
    {
        if (titleText == null) return 0f;

        RectTransform title = titleText.rectTransform;
        return Mathf.Ceil(titleText.GetPreferredValues(Unbounded, Unbounded).x) + title.offsetMin.x - title.offsetMax.x;
    }

    /// <summary>
    /// Writes the command line. The typewriter only replays when the text actually changed: an
    /// inventory refresh or a prompt refresh on the same target must not re-type the same line.
    /// </summary>
    private void SetLine(string message, Color color, bool forceReplay)
    {
        if (promptText == null) return;

        string body = (prefixEnabled ? commandPrefix : string.Empty) + (uppercase ? message.ToUpperInvariant() : message);
        bool replay = forceReplay || body != currentBody;

        currentBody = body;
        promptText.color = color;
        cursorOn = true;
        cursorTimer = 0f;
        RenderLine();
        FitWindow();

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
