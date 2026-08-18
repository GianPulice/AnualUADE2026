using System.Collections;
using UnityEngine;

public class DoorInteractable : BaseRangeInteractable
{
    [SerializeField] private SO_DoorData doorData;

    [Header("Opening animation (hinged door)")]
    [Tooltip("Pivot transform the door rotates around. Place it at the hinge edge so the mesh " +
             "swings correctly. In variants, only change the mesh under the hinge — keep the " +
             "hinge itself where it is.")]
    [SerializeField] private Transform hinge;
    [Tooltip("Degrees to rotate on the hinge local Y axis when opening. Use a negative value to " +
             "swing the other way.")]
    [SerializeField] private float openAngle = 90f;
    [Tooltip("Total duration of the swing, in seconds.")]
    [SerializeField, Min(0.01f)] private float openDuration = 0.6f;

    private Quaternion hingeClosedLocalRot;
    private bool isOpen;
    private bool isAnimating;
    private bool wasEverOpened;

protected override void Awake()
    {
        base.Awake();

        CacheClosedRotation();

        wasEverOpened = doorData != null && PuzzleStateManager.Exists &&
                        PuzzleStateManager.Instance.IsDoorOpened(doorData.DoorId);

        isOpen = wasEverOpened;
        if (isOpen) ApplyOpenStateImmediate();
    }

public override string GetInteractText()
    {
        if (isOpen) return "Close door";

        if (doorData == null) return "Open door";

        if (!wasEverOpened && doorData.RequiredKey != null)
            return $"Open with {doorData.RequiredKey.ItemName}";

        return string.IsNullOrWhiteSpace(doorData.OpenPrompt) ? "Open door" : doorData.OpenPrompt;
    }

public override string GetInfoText()
    {
        if (isOpen) return string.Empty;
        if (doorData == null) return string.Empty;
        if (wasEverOpened) return string.Empty;

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
        if (isAnimating) return false;

        // Free door: no data means no requirements ever.
        if (doorData == null) return true;

        // Closing is always allowed once the door is open.
        if (isOpen) return true;

        // Already unlocked at some point: re-opening does not re-check requirements.
        if (wasEverOpened) return true;

        // First open: enforce key + puzzle requirements. Without a manager the requirement cannot
        // be confirmed either way, so stay locked — a door that opens because progress could not
        // be checked would let the player walk past a puzzle entirely.
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
        if (isAnimating) return;
        if (isOpen) CloseDoor();
        else OpenDoor();
    }

public void OpenDoor()
    {
        if (isOpen || isAnimating) return;

        // Flip the unlocked flag BEFORE mutating anything the UI listens to (inventory, etc.).
        // Otherwise ConsumeItem below fires a prompt refresh while wasEverOpened is still false,
        // and GetInfoText briefly advertises "You need X" — even though the door is opening.
        bool firstUnlock = !wasEverOpened;
        wasEverOpened = true;

        // Consume the key only on the first ever unlock.
        if (firstUnlock && doorData != null &&
            doorData.ConsumeKey && doorData.RequiredKey != null && InventoryManager.Exists)
            InventoryManager.Instance.ConsumeItem(doorData.RequiredKey);

        if (doorData != null)
        {
            if (PuzzleStateManager.Exists)
                PuzzleStateManager.Instance.SetDoorOpened(doorData.DoorId);
            else
                Debug.LogWarning($"[{nameof(DoorInteractable)}] No PuzzleStateManager — door " +
                                 $"'{doorData.DoorId}' opened but will not stay unlocked.", this);
        }

        StartCoroutine(AnimateOpen());
        StartCoroutine(AnimateOpen());

        string logId = doorData != null ? doorData.DoorId : gameObject.name;
        Debug.Log($"Door opened: {logId}");
    }

public void CloseDoor()
    {
        if (!isOpen || isAnimating) return;

        StartCoroutine(AnimateClose());

        string logId = doorData != null ? doorData.DoorId : gameObject.name;
        Debug.Log($"Door closed: {logId}");
    }


private IEnumerator AnimateOpen()
    {
        yield return AnimateHinge(hingeClosedLocalRot,
                                  hingeClosedLocalRot * Quaternion.Euler(0f, openAngle, 0f));
        isOpen = true;
        InteractionEvents.RequestPromptRefresh();
    }

private IEnumerator AnimateClose()
    {
        yield return AnimateHinge(hingeClosedLocalRot * Quaternion.Euler(0f, openAngle, 0f),
                                  hingeClosedLocalRot);
        isOpen = false;
        InteractionEvents.RequestPromptRefresh();
    }

    private IEnumerator AnimateHinge(Quaternion from, Quaternion to)
    {
        isAnimating = true;

        float t = 0f;
        while (t < openDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / openDuration));

            if (hinge != null)
                hinge.localRotation = Quaternion.Slerp(from, to, k);

            yield return null;
        }

        if (hinge != null) hinge.localRotation = to;

        isAnimating = false;
    }


private void ApplyOpenStateImmediate()
    {
        if (hinge != null)
            hinge.localRotation = hingeClosedLocalRot * Quaternion.Euler(0f, openAngle, 0f);
    }

private void CacheClosedRotation()
    {
        if (hinge != null) hingeClosedLocalRot = hinge.localRotation;
    }    public override bool IsRepeatable()
    {
        return true;
    }
}
