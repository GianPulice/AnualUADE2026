using UnityEngine;

public class ContainerPuzzleController : MonoBehaviour
{
    [SerializeField] private SO_ContainerPuzzleData containerPuzzleData;
    [Tooltip("Where the reward is left if the player cannot carry it (one-Special-at-a-time rule). Empty = in front of the player.")]
    [SerializeField] private Transform rewardDropPoint;

    public string PuzzleId => containerPuzzleData != null ? containerPuzzleData.PuzzleId : string.Empty;
    public SO_ContainerPuzzleData PuzzleData => containerPuzzleData;

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
            AudioManager.Instance.PlaySFX("sfx_subpuzzle_completo");

        PuzzleRewardDelivery.Deliver(containerPuzzleData.RewardItem, rewardDropPoint, this);

        Debug.Log($"Container puzzle completed: {containerPuzzleData.PuzzleId}");
    }

    /// <summary>
    /// Freezes every box of this puzzle where it stands, now that the arrangement is the right one.
    ///
    /// The snap onto a basket is deliberately reversible (see <c>PushableBox.SnapToBasket</c>) so a
    /// box put in the wrong place can be pulled out again. That has to stop the moment the puzzle
    /// is solved, or the player can push a box back out of a finished puzzle and leave the level in
    /// a state where the reward is granted and the arrangement no longer matches it.
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
}
