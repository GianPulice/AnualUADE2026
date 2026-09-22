using UnityEngine;

public class ContainerPuzzleController : MonoBehaviour
{
    [SerializeField] private SO_ContainerPuzzleData containerPuzzleData;
    [Tooltip("Where the reward is left if the player cannot carry it (one-Special-at-a-time rule). Empty = in front of the player.")]
    [SerializeField] private Transform rewardDropPoint;

    public string PuzzleId => containerPuzzleData != null ? containerPuzzleData.PuzzleId : string.Empty;
    public SO_ContainerPuzzleData PuzzleData => containerPuzzleData;

    private void OnEnable() => CheckpointManager.OnRespawned += HandleRespawned;
    private void OnDisable() => CheckpointManager.OnRespawned -= HandleRespawned;

    private void Start()
    {
        // Already solved when the scene came up (e.g. reloaded mid-run): the boxes must not be
        // movable out of a finished arrangement.
        if (containerPuzzleData != null && PuzzleStateManager.Exists &&
            PuzzleStateManager.Instance.IsPuzzleCompleted(containerPuzzleData.PuzzleId))
            LockSolvedBoxes();
    }

    public void CheckContainers()
    {
        if (containerPuzzleData == null) return;

        // Every read and the completion write below go through the manager; bailing once here
        // beats four separate guards. Singleton.Instance already logs on its own when it is null,
        // so there is nothing to add — this only has to stop the NullReferenceException.
        if (!PuzzleStateManager.Exists) return;

        if (PuzzleStateManager.Instance.IsPuzzleCompleted(containerPuzzleData.PuzzleId))
            return;

        foreach (SO_ContainerPuzzleData.ContainerRequirement requirement in containerPuzzleData.Requirements)
        {
            string currentSlot = PuzzleStateManager.Instance.GetContainerSlot(requirement.containerId);

            if (currentSlot != requirement.requiredSlotId)
                return;
        }

        PuzzleStateManager.Instance.SetPuzzleCompleted(containerPuzzleData.PuzzleId);

        LockSolvedBoxes();

        if (AudioManager.Exists)
            AudioManager.Instance.PlaySFX("sfx_subpuzzle_2_completo");

        PuzzleRewardDelivery.Deliver(containerPuzzleData.RewardItem, rewardDropPoint, this);

        Debug.Log($"Container puzzle completed: {containerPuzzleData.PuzzleId}");
    }

    /// <summary>
    /// Freezes every box of this puzzle where it stands, now that the arrangement is the right one.
    /// Until then the snap onto a basket is reversible (see <see cref="PushableBox.SnapToBasket"/>),
    /// so a box in the wrong place can be pulled out again; after this nothing can be moved.
    /// </summary>
    private void LockSolvedBoxes()
    {
        BallPuzzleItem[] balls = FindObjectsByType<BallPuzzleItem>(FindObjectsInactive.Exclude);

        foreach (BallPuzzleItem ball in balls)
        {
            if (!ball.IsConfigured) continue;
            if (ball.LinkedPuzzleId != containerPuzzleData.PuzzleId) continue;

            PushableBox box = ball.GetComponentInParent<PushableBox>();
            if (box != null) box.LockInPlace();
        }
    }

    /// <summary>
    /// A capture rolls the recorded slots back to the checkpoint's snapshot, but the boxes stay
    /// physically where the player left them. Rebuild the record from what the baskets actually
    /// hold, so the puzzle keeps judging the real arrangement.
    /// </summary>
    private void HandleRespawned(Checkpoint checkpoint)
    {
        if (containerPuzzleData == null || !PuzzleStateManager.Exists) return;

        PuzzleStateManager state = PuzzleStateManager.Instance;

        if (state.IsPuzzleCompleted(containerPuzzleData.PuzzleId))
        {
            LockSolvedBoxes();
            return;
        }

        foreach (SO_ContainerPuzzleData.ContainerRequirement requirement in containerPuzzleData.Requirements)
            state.ClearContainerSlot(requirement.containerId);

        foreach (BasketTrigger basket in FindObjectsByType<BasketTrigger>(FindObjectsInactive.Exclude))
            if (basket.LinkedPuzzleId == containerPuzzleData.PuzzleId) basket.PublishOccupant();

        CheckContainers();
    }
}
