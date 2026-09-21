using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One basket of the container puzzle. Any box of the puzzle snaps onto any basket; which box is
/// in which basket is recorded in <see cref="PuzzleStateManager"/> and judged as a whole by
/// <see cref="ContainerPuzzleController"/>. The snap is reversible: a box can be pulled back out
/// and placed again, here or in another basket, until the puzzle is solved and every box locks.
///
/// Occupancy is polled every physics step (an overlap query on this object's BoxCollider) rather
/// than driven by OnTriggerEnter/Exit. The snap toggles the box's Rigidbody kinematic and back,
/// and trigger callbacks around that toggle, around two boxes touching the same basket, or around
/// a box that never fully left are exactly where an event-driven version desyncs. A query answers
/// "what is on me right now" and cannot drift.
///
///  - A box snaps when its solid collider covers this trigger (the small zone at the centre of
///    the basket) and the basket is empty.
///  - It leaves the basket, and its slot is cleared, as soon as it stops covering the zone.
///  - It can only snap onto THIS basket again once it has left the basket completely — its
///    footprint no longer overlaps the basket's (<see cref="footprint"/>). Otherwise pulling a box
///    out a few centimetres would drag it straight back in.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class BasketTrigger : MonoBehaviour
{
    [SerializeField] private string basketId;
    [SerializeField] private string linkedPuzzleId;
    [Tooltip("Transform the box (PushableBox) is snapped to when it lands here. Only X and Z are " +
             "used; the box keeps its own Y. Leave empty to fall back to this trigger's parent — " +
             "the intended layout, where the trigger is a child of Canasto_X.")]
    [SerializeField] private Transform snapTarget;
    [Tooltip("The whole basket area. A box that left this basket can only snap onto it again " +
             "once it no longer overlaps this area at all. Leave empty to use the collider on " +
             "this trigger's parent (Canasto_X), or its renderers if it has none.")]
    [SerializeField] private Collider footprint;

    private BoxCollider zone;

    // The box seated on this basket, or null. Only one at a time.
    private BallPuzzleItem occupant;
    // Boxes that left this basket and have not yet cleared its footprint: not allowed back in.
    private readonly HashSet<BallPuzzleItem> awaitingFullExit = new HashSet<BallPuzzleItem>();
    private readonly List<BallPuzzleItem> inZone = new List<BallPuzzleItem>();
    private readonly List<BallPuzzleItem> scratch = new List<BallPuzzleItem>();

    private static readonly Collider[] OverlapBuffer = new Collider[32];

    private ContainerPuzzleController cachedController;

    public string BasketId => basketId;
    public string LinkedPuzzleId => linkedPuzzleId;

    /// <summary>
    /// The controller that owns <see cref="linkedPuzzleId"/>, resolved once and cached.
    ///
    /// Resolved lazily rather than in Awake because gameplay scenes load additively: the controller
    /// may not exist yet when this trigger wakes up. The null test is Unity's overloaded
    /// <c>==</c>, so a controller destroyed with its scene reads as null here and is re-resolved
    /// instead of throwing.
    /// </summary>
    private ContainerPuzzleController Controller
    {
        get
        {
            if (cachedController != null) return cachedController;

            ContainerPuzzleController[] controllers =
                FindObjectsByType<ContainerPuzzleController>(FindObjectsInactive.Exclude);

            foreach (ContainerPuzzleController controller in controllers)
            {
                if (controller.PuzzleId != linkedPuzzleId) continue;

                cachedController = controller;
                break;
            }

            return cachedController;
        }
    }

    private void Awake()
    {
        zone = GetComponent<BoxCollider>();
        if (footprint == null && transform.parent != null)
            footprint = transform.parent.GetComponent<Collider>();
    }

    private void FixedUpdate()
    {
        if (zone == null || !zone.enabled) return;

        CollectBallsInZone(inZone);

        // 1. The seated box stopped covering the zone: it has left the basket.
        if (occupant != null && !inZone.Contains(occupant)) Vacate();

        // 2. Boxes that left earlier may come back once they have cleared the whole basket.
        if (awaitingFullExit.Count > 0)
        {
            scratch.Clear();
            foreach (BallPuzzleItem ball in awaitingFullExit)
                if (ball == null || !OverlapsFootprint(ball)) scratch.Add(ball);
            foreach (BallPuzzleItem ball in scratch) awaitingFullExit.Remove(ball);
        }

        // 3. Empty basket: seat the first eligible box standing on it.
        if (occupant != null) return;
        foreach (BallPuzzleItem ball in inZone)
        {
            if (awaitingFullExit.Contains(ball)) continue;
            Seat(ball);
            break;
        }
    }

    private void Seat(BallPuzzleItem ball)
    {
        occupant = ball;

        if (PuzzleStateManager.Exists)
            PuzzleStateManager.Instance.SetContainerSlot(ball.BallId, basketId);
        else
            Debug.LogWarning($"[{nameof(BasketTrigger)}] No PuzzleStateManager — ball " +
                             $"'{ball.BallId}' landing in basket '{basketId}' was not recorded.", this);

        // A locked box (puzzle already solved, e.g. on a scene reload) is only recorded, never moved.
        PushableBox box = ball.GetComponentInParent<PushableBox>();
        if (box != null && !box.IsLocked) box.SnapToBasket(ResolveSnapTarget());

        // After the snap starts, so a puzzle completed by this very box locks a box that is
        // already sliding onto the centre (LockInPlace lets the slide finish).
        NotifyPuzzleController();

        Debug.Log($"Basket {basketId} detected ball {ball.BallId}");
    }

    private void Vacate()
    {
        BallPuzzleItem ball = occupant;
        occupant = null;
        if (ball == null) return;

        awaitingFullExit.Add(ball);

        // Only clear the slot if it still points here, so a stale call can never wipe the record
        // of another basket.
        if (PuzzleStateManager.Exists &&
            PuzzleStateManager.Instance.GetContainerSlot(ball.BallId) == basketId)
            PuzzleStateManager.Instance.ClearContainerSlot(ball.BallId);

        NotifyPuzzleController();

        Debug.Log($"Ball {ball.BallId} left basket {basketId}");
    }

    /// <summary>
    /// Writes this basket's current occupant back into <see cref="PuzzleStateManager"/>. Used by
    /// <see cref="ContainerPuzzleController"/> after a checkpoint rollback, which restores the
    /// recorded slots but leaves the physical boxes where they are.
    /// </summary>
    public void PublishOccupant()
    {
        if (occupant == null || !PuzzleStateManager.Exists) return;
        PuzzleStateManager.Instance.SetContainerSlot(occupant.BallId, basketId);
    }

    private void CollectBallsInZone(List<BallPuzzleItem> result)
    {
        result.Clear();

        Transform t = zone.transform;
        Vector3 centre = t.TransformPoint(zone.center);
        Vector3 scale = t.lossyScale;
        Vector3 halfExtents = new Vector3(Mathf.Abs(zone.size.x * scale.x),
                                          Mathf.Abs(zone.size.y * scale.y),
                                          Mathf.Abs(zone.size.z * scale.z)) * 0.5f;

        int count = Physics.OverlapBoxNonAlloc(centre, halfExtents, OverlapBuffer, t.rotation,
                                               Physics.AllLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            BallPuzzleItem ball = OverlapBuffer[i].GetComponentInParent<BallPuzzleItem>();
            if (ball == null || !ball.IsConfigured) continue;
            if (ball.LinkedPuzzleId != linkedPuzzleId) continue;
            if (!result.Contains(ball)) result.Add(ball);
        }
    }

    /// <summary>True while any solid collider of the box still overlaps the basket, on X/Z.</summary>
    private bool OverlapsFootprint(BallPuzzleItem ball)
    {
        Bounds area = FootprintBounds();

        Rigidbody body = ball.GetComponentInParent<Rigidbody>();
        Component root = body != null ? body : ball;
        foreach (Collider c in root.GetComponentsInChildren<Collider>())
        {
            if (c.isTrigger || !c.enabled) continue;

            Bounds b = c.bounds;
            if (b.min.x < area.max.x && b.max.x > area.min.x &&
                b.min.z < area.max.z && b.max.z > area.min.z)
                return true;
        }
        return false;
    }

    private Bounds FootprintBounds()
    {
        if (footprint != null) return footprint.bounds;

        Transform basket = transform.parent != null ? transform.parent : transform;
        Renderer[] renderers = basket.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        return zone.bounds;
    }

    private Transform ResolveSnapTarget()
    {
        if (snapTarget != null) return snapTarget;
        return transform.parent != null ? transform.parent : transform;
    }

    private void NotifyPuzzleController()
    {
        ContainerPuzzleController controller = Controller;
        if (controller != null) controller.CheckContainers();
    }
}
