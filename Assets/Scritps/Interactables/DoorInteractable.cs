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

    [Header("Nemesis")]
    [Tooltip("Whether the Nemesis can open this door on its way through. Turn it off on doors " +
             "that are meant to be a safe haven.\n\n" +
             "When it is off, the door should also get a NavMeshObstacle with Carve: that way the " +
             "NavMesh genuinely closes and the Nemesis does not even try to path through, instead " +
             "of walking up to the door and pushing against it.")]
    [SerializeField] private bool nemesisCanOpen = true;

    [Tooltip("Whether the Nemesis can force the door even when it is locked or gated behind an " +
             "unsolved puzzle. On is the Mr. X behaviour: locks do not exist for it. Off makes it " +
             "respect the same conditions as the player.")]
    [SerializeField] private bool nemesisCanForceLocked = true;

    private Quaternion hingeClosedLocalRot;
    private bool isOpen;
    private bool isAnimating;
    private bool wasEverOpened;

    public bool IsOpen => isOpen;
    public bool IsAnimating => isAnimating;

    /// <summary>Seconds the leaf takes to swing open. The Nemesis reads it to know how long to
    /// hold back before crossing, instead of duplicating the number on its own component.</summary>
    public float OpenDuration => openDuration;

    public bool NemesisCanOpen => nemesisCanOpen;

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

        string logId = doorData != null ? doorData.DoorId : gameObject.name;
        Debug.Log($"Door opened: {logId}");
    }

    // ── Opening by the Nemesis ──────────────────────────────────────────────

    /// <summary>
    /// Opens the door for the Nemesis. Deliberately does NOT go through <see cref="OpenDoor"/>.
    ///
    /// OpenDoor does the player's progress bookkeeping: it consumes the key from the inventory and
    /// marks the door as opened on the PuzzleStateManager, i.e. permanently. The Nemesis smashing
    /// a door open cannot spend the player's key or hand them a puzzle, so this only swings the
    /// leaf. For the player the door is still locked: if they close it again, it will ask for
    /// exactly what it asked for before.
    /// </summary>
    /// <returns>true if it started opening on this call.</returns>
    public bool TryOpenForNemesis()
    {
        if (isOpen || isAnimating) return false;
        if (!nemesisCanOpen) return false;

        // CanInteractInCloseRange is the player's condition (key + puzzle). It is only consulted
        // when this door canNOT be forced.
        if (!nemesisCanForceLocked && !CanInteractInCloseRange()) return false;

        StartCoroutine(AnimateOpen());

        string logId = doorData != null ? doorData.DoorId : gameObject.name;
        Debug.Log($"[Nemesis] Door forced open: {logId}", this);
        return true;
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
        // Identity and not default(Quaternion): the implicit default is (0,0,0,0), which is not a
        // rotation at all. It only ever mattered when the hinge was missing, and there the door
        // failed silently anyway — but leaving an invalid quaternion around is the kind of thing
        // that produces an unexplainable NaN three systems away.
        hingeClosedLocalRot = Quaternion.identity;

        if (hinge == null)
        {
            // Loud, because the failure it causes is completely mute: every open request succeeds,
            // logs that it opened, flips isOpen — and nothing moves on screen. Chasing that from
            // the symptom costs far more than this line.
            Debug.LogError($"[{nameof(DoorInteractable)}] '{name}' has no hinge assigned. It will " +
                           "report itself as opening and nothing will actually move. Assign the " +
                           "pivot transform the leaf swings around.", this);
            return;
        }

        hingeClosedLocalRot = hinge.localRotation;

        if (hinge.childCount == 0)
        {
            Debug.LogWarning($"[{nameof(DoorInteractable)}] '{name}': the hinge '{hinge.name}' has " +
                             "no children, so rotating it moves nothing visible. The leaf meshes " +
                             "have to be children of the hinge, not siblings of it.", this);
        }
    }    public override bool IsRepeatable()
    {
        return true;
    }
}
