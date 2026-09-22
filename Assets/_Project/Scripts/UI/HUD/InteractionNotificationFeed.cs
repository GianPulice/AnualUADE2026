using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The interaction notifications: short lines stacked in a corner of the HUD. What just entered
/// the inventory, a key used up, a reward left on the floor. At most <see cref="maxVisible"/> are
/// on screen: a new one pushes the oldest out, and each one leaves on its own after its seconds.
///
/// Two sources, both static buses:
///   - <see cref="InteractionEvents.OnGlobalMessage"/>, anything the game says about an interaction
///     (a used key, a reward dropped because the hands were full);
///   - <see cref="InventoryEvents.OnItemAdded"/>, every item that enters the inventory, picked up
///     in the world or granted by a puzzle.
/// These used to share the interaction prompt's single slot, where the prompt of whatever the
/// player looked at next overwrote them straight away. Here they stack and never compete with it.
///
/// Not here on purpose: the Architect's alerts (<see cref="HUDAlertView"/>, one at a time at the
/// top) and anything the player is looking at right now (<see cref="InteractionPromptView"/>).
///
/// Timing: the lifetime counts only while no modal is open, so a line raised under the sequence
/// panel or the inventory waits there with its full seconds and plays its entrance when the modal
/// closes. The animations run on unscaled time, like every other HUD fade.
///
/// SETUP: on a RectTransform in HUDCanvas with a CanvasGroup and a ModalVisibilityGate. One child
/// is the row template (background + TMP label), inactive, anchored and pivoted to the corner the
/// feed grows from. Rows are cloned from it and stacked downwards from the feed's top.
/// </summary>
public class InteractionNotificationFeed : MonoBehaviour
{
    [Tooltip("An inactive child: one finished row (background and TMP label). Cloned per message.")]
    [SerializeField] private RectTransform rowTemplate;

    [Tooltip("Most rows on screen at once. The oldest leaves when a new one would pass this.")]
    [SerializeField, Min(1)] private int maxVisible = 3;

    [Tooltip("Gap between two rows, in canvas units.")]
    [SerializeField, Min(0f)] private float spacing = 6f;

    [Header("Text")]
    [Tooltip("Line shown when an item enters the inventory. {0} is the item name.")]
    [SerializeField] private string itemAddedFormat = "+ {0}";

    [Tooltip("Seconds an item line stays up. Global messages bring their own.")]
    [SerializeField, Min(0.1f)] private float itemAddedSeconds = 3f;

    [SerializeField] private bool uppercase = true;

    [Header("Motion")]
    [Tooltip("How far to the side a row starts when it enters, and ends when it leaves.")]
    [SerializeField] private float slideDistance = 80f;
    [SerializeField, Min(0.01f)] private float enterSeconds = 0.2f;
    [SerializeField, Min(0.01f)] private float exitSeconds = 0.15f;
    [Tooltip("How fast the rows close the gap left by one that leaves. Higher = snappier.")]
    [SerializeField, Min(0.1f)] private float reflowSharpness = 14f;

    private class Row
    {
        public RectTransform rect;
        public CanvasGroup group;
        public TMPTypewriterReveal typewriter;
        public bool typed;
        public float lifeLeft;
        // 0 = off to the side and transparent, 1 = in place. Rises on entry, falls on exit.
        public float shown;
        public float homeX;
    }

    // Oldest first: index 0 is the top row.
    private readonly List<Row> live = new List<Row>();
    private readonly List<Row> leaving = new List<Row>();
    private float rowStep;

    // Awake/OnDestroy, not OnEnable/OnDisable: static buses (docs/UI-System.md §7.1). A message
    // raised while this object happened to be disabled would be lost otherwise.
    private void Awake()
    {
        if (rowTemplate != null)
        {
            rowTemplate.gameObject.SetActive(false);
            rowStep = rowTemplate.rect.height + spacing;
        }

        InteractionEvents.OnGlobalMessage += HandleMessage;
        InventoryEvents.OnItemAdded += HandleItemAdded;
    }

    private void OnDestroy()
    {
        InteractionEvents.OnGlobalMessage -= HandleMessage;
        InventoryEvents.OnItemAdded -= HandleItemAdded;
    }

    private void HandleItemAdded(SO_InventoryItem item)
    {
        if (item == null) return;
        HandleMessage(string.Format(itemAddedFormat, item.ItemName), itemAddedSeconds);
    }

    private void HandleMessage(string text, float seconds)
    {
        if (rowTemplate == null || string.IsNullOrWhiteSpace(text)) return;

        while (live.Count >= maxVisible) Leave(live[0]);

        RectTransform rect = Instantiate(rowTemplate, rowTemplate.parent);
        rect.gameObject.SetActive(true);

        TMP_Text label = rect.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = uppercase ? text.ToUpperInvariant() : text;

        CanvasGroup group = rect.GetComponent<CanvasGroup>();
        if (group == null) group = rect.gameObject.AddComponent<CanvasGroup>();

        var row = new Row
        {
            rect = rect,
            group = group,
            typewriter = rect.GetComponentInChildren<TMPTypewriterReveal>(true),
            lifeLeft = Mathf.Max(0.1f, seconds),
            homeX = rowTemplate.anchoredPosition.x,
        };

        // Enters straight into its slot, below the rows already up: only the horizontal slide moves.
        rect.anchoredPosition = new Vector2(row.homeX + slideDistance, SlotY(live.Count));
        live.Add(row);
        Apply(row);
    }

    private void Update()
    {
        // Frozen as a whole under a modal: the gate has hidden the feed, and a line that played its
        // entrance or spent its seconds behind a panel would be gone the moment the panel closes.
        if (UIStateManager.Exists && UIStateManager.Instance.IsAnyModalOpen) return;

        float dt = Time.unscaledDeltaTime;
        float reflow = 1f - Mathf.Exp(-reflowSharpness * dt);

        for (int i = live.Count - 1; i >= 0; i--)
        {
            Row row = live[i];

            // Typed out on the first visible frame, not at spawn: under a modal it would finish
            // behind the panel.
            if (!row.typed)
            {
                row.typed = true;
                if (row.typewriter != null) row.typewriter.Play();
            }

            row.shown = Mathf.MoveTowards(row.shown, 1f, dt / enterSeconds);

            Vector2 pos = row.rect.anchoredPosition;
            pos.y = Mathf.Lerp(pos.y, SlotY(i), reflow);
            row.rect.anchoredPosition = pos;
            Apply(row);

            // Scaled on purpose: a line does not run out while the game is frozen.
            row.lifeLeft -= Time.deltaTime;
            if (row.lifeLeft <= 0f) Leave(row);
        }

        for (int i = leaving.Count - 1; i >= 0; i--)
        {
            Row row = leaving[i];
            row.shown = Mathf.MoveTowards(row.shown, 0f, dt / exitSeconds);
            Apply(row);

            if (row.shown > 0f) continue;

            leaving.RemoveAt(i);
            if (row.rect != null) Destroy(row.rect.gameObject);
        }
    }

    /// <summary>Takes a row out of the stack: the ones below it close the gap while it slides away.</summary>
    private void Leave(Row row)
    {
        if (!live.Remove(row)) return;
        leaving.Add(row);
    }

    private void Apply(Row row)
    {
        if (row.rect == null) return;

        float eased = Mathf.SmoothStep(0f, 1f, row.shown);
        row.group.alpha = eased;

        Vector2 pos = row.rect.anchoredPosition;
        pos.x = row.homeX + slideDistance * (1f - eased);
        row.rect.anchoredPosition = pos;
    }

    private float SlotY(int index) => rowTemplate.anchoredPosition.y - index * rowStep;
}
