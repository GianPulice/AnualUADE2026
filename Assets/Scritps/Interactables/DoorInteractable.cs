using System.Collections;
using UnityEngine;

public class DoorInteractable : BaseRangeInteractable
{
    [SerializeField] private SO_DoorData doorData;

    [Header("Opening animation (double door)")]
    [Tooltip("Left panel. Slides towards -X (its local left).")]
    [SerializeField] private Transform leftPanel;
    [Tooltip("Right panel. Slides towards +X (its local right).")]
    [SerializeField] private Transform rightPanel;
    [Tooltip("Distance each panel slides when opening, in local units.")]
    [SerializeField, Min(0f)] private float slideDistance = 1f;
    [Tooltip("Total duration of the slide, in seconds.")]
    [SerializeField, Min(0.01f)] private float slideDuration = 0.6f;

    private Vector3 leftClosedLocalPos;
    private Vector3 rightClosedLocalPos;
    private bool isOpen;
    private bool isAnimating;

    protected override void Awake()
    {
        base.Awake();

        if (doorData == null)
        {
            Debug.LogError($"DoorInteractable without SO_DoorData on {gameObject.name}");
            return;
        }

        CacheClosedPositions();

        // Exists rather than 'Instance != null': the property logs a warning of its own every time
        // it is read while null, so testing it as a null check spams the console on the very setup
        // it is meant to tolerate.
        isOpen = PuzzleStateManager.Exists &&
                 PuzzleStateManager.Instance.IsDoorOpened(doorData.DoorId);

        if (isOpen) ApplyOpenStateImmediate();
    }

    public override string GetInteractText()
    {
        if (doorData == null) return "Unconfigured door";

        if (isOpen) return string.Empty;

        if (doorData.RequiredKey != null)
            return $"Open with {doorData.RequiredKey.ItemName}";

        return doorData.OpenPrompt;
    }

    public override string GetInfoText()
    {
        if (doorData == null || isOpen) return string.Empty;

        if (doorData.RequiredKey != null && InventoryManager.Exists &&
            !InventoryManager.Instance.HasItem(doorData.RequiredKey))
            return $"You need {doorData.RequiredKey.ItemName}";

        if (!string.IsNullOrWhiteSpace(doorData.RequiredCompletedPuzzleId) &&
            PuzzleStateManager.Exists &&
            !PuzzleStateManager.Instance.IsPuzzleCompleted(doorData.RequiredCompletedPuzzleId))
            return doorData.LockedPrompt;

        return string.Empty;
    }

    protected override bool CanInteractInCloseRange()
    {
        if (doorData == null) return false;
        if (isOpen || isAnimating) return false;

        // Without a manager the requirement cannot be confirmed either way. Staying locked is the
        // safe reading: a door that opens because progress could not be checked would let the
        // player walk past a puzzle entirely.
        if (doorData.RequiredKey != null &&
            (!InventoryManager.Exists || !InventoryManager.Instance.HasItem(doorData.RequiredKey)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(doorData.RequiredCompletedPuzzleId) &&
            (!PuzzleStateManager.Exists ||
             !PuzzleStateManager.Instance.IsPuzzleCompleted(doorData.RequiredCompletedPuzzleId)))
        {
            return false;
        }

        return true;
    }

    protected override void OnInteract()
    {
        OpenDoor();
    }

    public void OpenDoor()
    {
        if (doorData == null) return;
        if (isOpen || isAnimating) return;

        if (doorData.ConsumeKey && doorData.RequiredKey != null && InventoryManager.Exists)
            InventoryManager.Instance.ConsumeItem(doorData.RequiredKey);

        // The door still opens without a manager — the animation below runs either way — but it
        // will be shut again next time the scene loads, so say so.
        if (PuzzleStateManager.Exists)
            PuzzleStateManager.Instance.SetDoorOpened(doorData.DoorId);
        else
            Debug.LogWarning($"[{nameof(DoorInteractable)}] No PuzzleStateManager — door " +
                             $"'{doorData.DoorId}' opened but will not stay open.", this);

        StartCoroutine(AnimateOpen());

        Debug.Log($"Door opened: {doorData.DoorId}");
    }

    private IEnumerator AnimateOpen()
    {
        isAnimating = true;

        Vector3 leftTarget = leftClosedLocalPos + Vector3.left * slideDistance;
        Vector3 rightTarget = rightClosedLocalPos + Vector3.right * slideDistance;

        float t = 0f;
        while (t < slideDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / slideDuration));

            if (leftPanel != null)
                leftPanel.localPosition = Vector3.Lerp(leftClosedLocalPos, leftTarget, k);
            if (rightPanel != null)
                rightPanel.localPosition = Vector3.Lerp(rightClosedLocalPos, rightTarget, k);

            yield return null;
        }

        if (leftPanel != null) leftPanel.localPosition = leftTarget;
        if (rightPanel != null) rightPanel.localPosition = rightTarget;

        //DisableBlockingCollider();
        isOpen = true;
        isAnimating = false;
    }

    private void ApplyOpenStateImmediate()
    {
        if (leftPanel != null)
            leftPanel.localPosition = leftClosedLocalPos + Vector3.left * slideDistance;
        if (rightPanel != null)
            rightPanel.localPosition = rightClosedLocalPos + Vector3.right * slideDistance;

        //DisableBlockingCollider();
    }

    private void CacheClosedPositions()
    {
        if (leftPanel != null) leftClosedLocalPos = leftPanel.localPosition;
        if (rightPanel != null) rightClosedLocalPos = rightPanel.localPosition;
    }

    /*private void DisableBlockingCollider()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }*/

    public override bool IsRepeatable()
    {
        return false;
    }
}
