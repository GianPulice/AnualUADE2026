using UnityEngine;

public class BasketTrigger : MonoBehaviour
{
    [SerializeField] private string basketId;
    [SerializeField] private string linkedPuzzleId;
    [Tooltip("Transform the box (PushableBox) is snapped to when the CORRECT ball lands here. " +
             "Only X and Z are used; the box keeps its own Y. Leave empty to fall back to this " +
             "trigger's parent — the intended layout, where the trigger is a child of Canasto_X.")]
    [SerializeField] private Transform snapTarget;

    private BallPuzzleItem currentBall;

    private ContainerPuzzleController cachedController;

    /// <summary>
    /// The controller that owns <see cref="linkedPuzzleId"/>, resolved once and cached.
    ///
    /// Both trigger handlers used to run their own <c>FindObjectsByType</c> scan, so a single box
    /// landing in a basket cost two full-scene searches — and a box nudged in and out of a basket
    /// repeats that on every crossing.
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

    private void OnTriggerEnter(Collider other)
    {
        if (other.isTrigger) return;

        BallPuzzleItem ball = other.GetComponentInParent<BallPuzzleItem>();

        if (ball == null) return;
        if (!ball.IsConfigured) return;
        if (ball.LinkedPuzzleId != linkedPuzzleId) return;

        currentBall = ball;

        if (PuzzleStateManager.Exists)
            PuzzleStateManager.Instance.SetContainerSlot(ball.BallId, basketId);
        else
            Debug.LogWarning($"[{nameof(BasketTrigger)}] No PuzzleStateManager — ball " +
                             $"'{ball.BallId}' landing in basket '{basketId}' was not recorded.", this);

        // Any box of this puzzle snaps onto any basket, right or wrong. It used to snap only onto
        // the basket the Requirements table paired it with, which turned "does it click into
        // place?" into the answer: the player pushed a box at each basket in turn until one
        // accepted it and never had to understand the symbols. The arrangement is judged as a
        // whole in CheckContainers instead, and the snap is reversible so a wrong guess can be
        // pushed back out.
        PushableBox grab = ball.GetComponentInParent<PushableBox>();
        if (grab != null) grab.SnapToBasket(ResolveSnapTarget());

        // After the snap, so a puzzle completed by this very box locks a box that is already
        // centred on its basket rather than one mid-slide.
        NotifyPuzzleController();

        Debug.Log($"Basket {basketId} detected ball {ball.BallId}");
    }

    private Transform ResolveSnapTarget()
    {
        if (snapTarget != null) return snapTarget;
        return transform.parent != null ? transform.parent : transform;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.isTrigger) return;

        BallPuzzleItem ball = other.GetComponentInParent<BallPuzzleItem>();

        if (ball == null) return;
        if (ball != currentBall) return;

        if (PuzzleStateManager.Exists)
            PuzzleStateManager.Instance.ClearContainerSlot(ball.BallId);
        else
            Debug.LogWarning($"[{nameof(BasketTrigger)}] No PuzzleStateManager — ball " +
                             $"'{ball.BallId}' leaving basket '{basketId}' was not recorded.", this);

        currentBall = null;

        NotifyPuzzleController();

        Debug.Log($"Ball {ball.BallId} left basket {basketId}");
    }

    private void NotifyPuzzleController()
    {
        ContainerPuzzleController controller = Controller;
        if (controller != null) controller.CheckContainers();
    }
}
