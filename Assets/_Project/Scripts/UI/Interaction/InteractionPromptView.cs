using System.Collections;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

public class InteractionPromptView : BaseScreenView
{
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color infoColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    [SerializeField] private UISlideTransition slide;

    [Header("Auto-pickup notice")]
    [Tooltip("Format of the transient line shown when an item is granted to the inventory WITHOUT " +
             "a world pickup (puzzle reward, scripted grant). {0} is the item name.")]
    [SerializeField] private string autoPickupFormat = "\"{0}\" added to inventory";

    [Tooltip("Seconds the auto-pickup notice stays on screen before returning to the normal " +
             "interaction prompt state. Counted in SCALED time, so a paused modal freezes the " +
             "timer and it resumes with the remaining seconds when the modal closes.")]
    [SerializeField, Min(0f)] private float autoPickupSeconds = 3f;

    [Tooltip("Colour used for the auto-pickup notice line. Kept separate from normalColor so the " +
             "notice reads as a distinct kind of message from an interaction prompt.")]
    [SerializeField] private Color autoNoticeColor = Color.yellow;

    private IInteractable currentTarget;

    // Auto-pickup notice state — event-driven and independent from the interaction system. While
    // active this view ignores TargetChanged / RequestPromptRefresh so the message is not overwritten
    // by an interactable the crosshair happens to land on mid-notice. Cleared by a timer, at which
    // point RefreshDisplay re-syncs the UI with whatever the interaction state is at that moment.
    private bool showingAutoNotice;
    private Coroutine autoNoticeRoutine;

    // A reward granted while a modal is open (e.g. the sequence panel completes and hands the item)
    // is deferred here until the modal closes. Without this the notice would start counting down
    // while the panel is still on top of it, and be gone the moment the panel closes. Only the
    // latest pending item is kept — a stale reward from a previous modal has no reason to surface
    // after a newer one arrives.
    private SO_InventoryItem pendingAutoItem;

    private void Awake()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        InteractionEvents.OnTargetChanged        += HandleTargetChanged;
        InteractionEvents.OnPromptRefreshRequested += HandlePromptRefreshRequested;
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
        InventoryEvents.OnItemAdded              -= HandleInventoryChanged;
        InventoryEvents.OnItemRemoved            -= HandleInventoryChanged;
        InventoryEvents.OnItemAutoAdded          -= HandleItemAutoAdded;
        UIStateManager.OnModalPushed             -= HandleModalPushed;
        UIStateManager.OnModalPopped             -= HandleModalPopped;
    }

    // Safety net for the deferred-notice path: OnModalPopped is the primary trigger to release a
    // pending notice, but some UIs close without pushing/popping through UIStateManager (or pop
    // one modal while another is still on top). Polling here fires the notice on the first frame
    // after every modal is gone, regardless of which event surfaced that fact. Costs a couple of
    // property checks per frame when idle — pendingAutoItem is null and the branch exits early.
    private void Update()
    {
        if (pendingAutoItem == null) return;
        if (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen) return;

        SO_InventoryItem item = pendingAutoItem;
        pendingAutoItem = null;
        StartAutoNotice(item);
    }

    private void HandlePromptRefreshRequested()
    {
        if (showingAutoNotice) return;
        RefreshDisplay(animate: false);
    }

    private void HandleTargetChanged(IInteractable target)
    {
        // Treat destroyed interactables (e.g. a pickup destroyed the frame it was consumed)
        // as null. IInteractable is an interface, so the raw `!= null` check on the field
        // skips UnityEngine.Object's fake-null overload — see IsAlive.
        if (!IsAlive(target)) target = null;

        currentTarget = target;

        // A live AND actionable target dismisses the auto-pickup notice for good: the player
        // choosing to look at something they can act on is a stronger signal than the tail end of
        // a pickup announcement, and per design we do NOT resume the notice afterwards. A target
        // that is not currently interactable (a box already locked into its basket, an info-only
        // prop) leaves the notice alone — the interaction prompt would have nothing to show
        // anyway, so hiding the notice for it would only lose information.
        if (showingAutoNotice && target != null && target.CanInteract())
        {
            CancelAutoNotice();
            // Fall through into the normal "target != null" branch below so the prompt is shown
            // immediately in this same call instead of waiting for the next TargetChanged.
        }
        else if (showingAutoNotice)
        {
            return;
        }

        if (target != null)
        {
            RefreshDisplay(animate: true);
            slide?.SlideIn(SlideDirection.FromBottom);
        }
        else
        {
            Fade(0f, 0.15f).Forget();
            slide?.SlideOut();
        }
    }

    private void HandleInventoryChanged(SO_InventoryItem _)
    {
        if (showingAutoNotice) return;
        RefreshDisplay(animate: false);
    }

    /// <summary>
    /// Shows a transient "'name' added to inventory" line for <see cref="autoPickupSeconds"/>
    /// seconds. Independent from the interaction system: driven only by the inventory event and
    /// an internal timer, so it fires the same whether the crosshair is on an interactable, on
    /// the sky, or nowhere. While the notice is up, target/inventory refreshes are suppressed so
    /// the message is not overwritten mid-display; when the timer ends, the view resyncs with
    /// whatever the interaction state is at that moment.
    /// </summary>
    private void HandleItemAutoAdded(SO_InventoryItem item)
    {
        if (item == null || promptText == null) return;

        // Defer the notice while any modal (sequence panel, inventory, pause...) is open. Firing
        // now would start the countdown behind the modal and the message would be gone the second
        // the modal closes. HandleModalPopped picks the pending item up and shows it then.
        if (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen)
        {
            pendingAutoItem = item;
            return;
        }

        StartAutoNotice(item);
    }

    private void StartAutoNotice(SO_InventoryItem item)
    {
        showingAutoNotice = true;
        promptText.color = autoNoticeColor;
        promptText.text  = string.Format(autoPickupFormat, item.ItemName);

        // A second grant arriving before the first notice ends restarts the timer with the newer
        // message — dropping the older text is preferable to queuing it, so the player never sees
        // a delayed line for an item they picked up several seconds ago.
        if (autoNoticeRoutine != null) StopCoroutine(autoNoticeRoutine);

        Fade(1f, 0.15f).Forget();
        slide?.SlideIn(SlideDirection.FromBottom);

        autoNoticeRoutine = StartCoroutine(AutoNoticeCountdown());
    }

    // Aborts the notice without triggering its exit animation. Used when a live interactable
    // preempts the notice: the caller is about to draw the interaction prompt over the same
    // CanvasGroup, so a fade-out here would fight it.
    private void CancelAutoNotice()
    {
        if (autoNoticeRoutine != null)
        {
            StopCoroutine(autoNoticeRoutine);
            autoNoticeRoutine = null;
        }
        showingAutoNotice = false;
    }

    // Scaled deltaTime on purpose: modals set Time.timeScale to 0, and the design here is that
    // the notice freezes with the game. A 2.3s-in pause must resume at 0.7s remaining after
    // unpause, not skip forward while the pause menu was open.
    private IEnumerator AutoNoticeCountdown()
    {
        float t = 0f;
        while (t < autoPickupSeconds)
        {
            t += Time.deltaTime;
            yield return null;
        }

        showingAutoNotice = false;
        autoNoticeRoutine = null;

        // Re-sync with the real interaction state: if the crosshair is on something, restore its
        // prompt; otherwise fade out. Doing this in one place (RefreshDisplay + the else branch)
        // keeps the exit symmetric with HandleTargetChanged.
        if (IsAlive(currentTarget))
        {
            RefreshDisplay(animate: true);
        }
        else
        {
            Fade(0f, 0.15f).Forget();
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

        // A reward granted while the modal was open was deferred to pendingAutoItem. Fire its
        // notice now that the modal is gone so the full 3s dwell starts against a visible UI, not
        // behind the panel. Consumes the pending slot so a subsequent modal pop does not replay it.
        if (pendingAutoItem != null)
        {
            SO_InventoryItem item = pendingAutoItem;
            pendingAutoItem = null;
            StartAutoNotice(item);
            return;
        }

        // Notice countdown was frozen (Time.deltaTime == 0 under the modal) but visuals were
        // hidden by HandleModalPushed. Restore them so the remaining seconds actually show.
        if (showingAutoNotice)
        {
            promptText.color = autoNoticeColor;
            Fade(1f, 0.15f).Forget();
            slide?.SlideIn(SlideDirection.FromBottom);
            return;
        }

        IInteractable live = InteractionManager.Exists
            ? InteractionManager.Instance.CurrentInteractable
            : null;

        if (!IsAlive(live)) return;

        currentTarget = live;
        RefreshDisplay(animate: true);
        slide?.SlideIn(SlideDirection.FromBottom);
    }

    private void RefreshDisplay(bool animate)
    {
        if (!IsAlive(currentTarget))
        {
            // The cached target was destroyed since the last update (typical after picking up
            // an item and then any UI event fires a refresh). Drop it so the prompt hides
            // cleanly instead of crashing on the next CanInteract call.
            currentTarget = null;
            Fade(0f, 0.15f).Forget();
            return;
        }

        if (currentTarget.CanInteract())
        {
            promptText.color = normalColor;
            promptText.text  = $"{currentTarget.GetInteractText()}";
            if (animate) Fade(1f, 0.15f).Forget();
        }
        else
        {
            string info = currentTarget.GetInfoText();
            if (!string.IsNullOrEmpty(info))
            {
                promptText.color = infoColor;
                promptText.text  = info;
                if (animate) Fade(1f, 0.15f).Forget();
            }
            else
            {
                Fade(0f, 0.15f).Forget();
            }
        }
    }

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
