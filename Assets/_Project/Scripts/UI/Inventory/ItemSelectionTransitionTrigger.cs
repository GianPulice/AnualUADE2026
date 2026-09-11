using UnityEngine;

/// <summary>
/// Plays the detail panel's transitions when the selection moves to a different item, so a new
/// selection reads as the panel switching over rather than as text being swapped in place.
///
/// It listens to InventoryEvents itself instead of being called by ItemDetailView: the view only
/// populates, and whether and how a change is animated is not its business. Its order against the
/// view's own handler does not matter — the transitions tolerate the text landing either side of Play().
///
/// Reselecting the item already shown does nothing. Clearing the selection, or closing the inventory,
/// forgets it, so picking the same item again afterwards still plays.
/// </summary>
[AddComponentMenu("WIRED/UI/Item Selection Transition Trigger")]
public class ItemSelectionTransitionTrigger : MonoBehaviour
{
    [SerializeField] private UISignalTransition signal;
    [SerializeField] private TMPTypewriterReveal[] typewriters;

    private SO_InventoryItem shown;

    private void OnEnable() => InventoryEvents.OnItemSelected += HandleItemSelected;

    private void OnDisable()
    {
        InventoryEvents.OnItemSelected -= HandleItemSelected;
        shown = null;
    }

    private void HandleItemSelected(SO_InventoryItem item)
    {
        if (item == shown) return;
        shown = item;
        if (item == null) return;

        if (signal != null) signal.Play();

        if (typewriters == null) return;
        foreach (TMPTypewriterReveal typewriter in typewriters)
            if (typewriter != null) typewriter.Play();
    }
}
