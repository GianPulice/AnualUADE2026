using UnityEngine;

public class BasketTrigger : MonoBehaviour
{
    [SerializeField] private string basketId;
    [SerializeField] private string linkedPuzzleId;

    private BallPuzzleItem currentBall;

    private void OnTriggerEnter(Collider other)
    {
        BallPuzzleItem ball = other.GetComponentInParent<BallPuzzleItem>();

        if (ball == null) return;
        if (!ball.IsConfigured) return;
        if (ball.LinkedPuzzleId != linkedPuzzleId) return;

        currentBall = ball;

        PuzzleStateManager.Instance.SetContainerSlot(ball.BallId, basketId);

        NotifyPuzzleController();

        Debug.Log($"Canasto {basketId} detecto pelota {ball.BallId}");
    }

    private void OnTriggerExit(Collider other)
    {
        BallPuzzleItem ball = other.GetComponentInParent<BallPuzzleItem>();

        if (ball == null) return;
        if (ball != currentBall) return;

        PuzzleStateManager.Instance.ClearContainerSlot(ball.BallId);

        currentBall = null;

        NotifyPuzzleController();

        Debug.Log($"Pelota {ball.BallId} salio del canasto {basketId}");
    }

    private void NotifyPuzzleController()
    {
        ContainerPuzzleController[] controllers =
            FindObjectsByType<ContainerPuzzleController>(FindObjectsInactive.Exclude);

        foreach (ContainerPuzzleController controller in controllers)
        {
            if (controller.PuzzleId == linkedPuzzleId)
            {
                controller.CheckContainers();
                return;
            }
        }
    }
}
