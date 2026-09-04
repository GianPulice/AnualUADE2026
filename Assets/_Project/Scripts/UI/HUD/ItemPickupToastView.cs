using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The "+ ITEM" line that slides in when something enters the inventory.
///
/// It exists because picking an item up produced no acknowledgement at all: the object vanished
/// from the world and the only confirmation was opening the inventory to check. The interaction
/// prompt cannot cover this — it describes what you are LOOKING at, and the pickup has just been
/// destroyed, so the prompt goes blank at exactly the moment feedback is due.
///
/// The animation is <see cref="UISlideTransition"/> with <c>feedbackStyle</c> and <c>autoHide</c>,
/// which is what its own doc comment lists this use case for.
///
/// SETUP: on a GameObject under HUDCanvas, with a UISlideTransition and a TMP text. Set the
/// transition's Direction to FromBottom, Feedback Style on, Auto Hide on.
/// </summary>
public class ItemPickupToastView : MonoBehaviour
{
    [SerializeField] private UISlideTransition slide;
    [SerializeField] private TMP_Text label;

    [Tooltip("Direction the toast enters from. Bottom-right is out of the way of the crosshair and " +
             "of the interaction prompt, which also slides up from the bottom.")]
    [SerializeField] private SlideDirection direction =
        SlideDirection.FromBottom;

    [Tooltip("Format of the line. {0} is the item name.")]
    [SerializeField] private string format = "+ {0}";

    [Tooltip("Longest queue kept when items arrive faster than they can be shown. Beyond this the " +
             "OLDEST pending entries are dropped: a burst of pickups should not make the player " +
             "watch a backlog play out long after it happened.")]
    [SerializeField, Min(1)] private int maxQueued = 4;

    private readonly Queue<string> pending = new Queue<string>();
    private bool isShowing;

    // Awake/OnDestroy and not OnEnable/OnDisable: InventoryEvents is a static delegate that
    // outlives this GameObject's enabled state, and an item picked up while the toast happened to
    // be disabled would be missed outright. See docs/UI-System.md §7.1.
    private void Awake()
    {
        InventoryEvents.OnItemAdded += HandleItemAdded;

        if (slide != null) slide.OnHidden += ShowNext;
    }

    private void OnDestroy()
    {
        InventoryEvents.OnItemAdded -= HandleItemAdded;

        if (slide != null) slide.OnHidden -= ShowNext;
    }

    private void HandleItemAdded(SO_InventoryItem item)
    {
        if (item == null || slide == null || label == null) return;

        pending.Enqueue(string.Format(format, item.ItemName));

        while (pending.Count > maxQueued) pending.Dequeue();

        if (!isShowing) ShowNext();
    }

    /// <summary>
    /// Shows the next queued line, or stands down when there are none left.
    ///
    /// Driven off the transition's own OnHidden rather than a timer of ours: the slide already owns
    /// the visible duration and the fade, and a second clock would drift out of step with it the
    /// first time someone retunes the animation.
    /// </summary>
    private void ShowNext()
    {
        if (pending.Count == 0)
        {
            isShowing = false;
            return;
        }

        isShowing = true;
        label.text = pending.Dequeue();
        slide.SlideIn(direction);
    }
}
