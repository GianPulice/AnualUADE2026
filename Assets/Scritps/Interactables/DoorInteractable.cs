using System.Collections;
using UnityEngine;
using UnityEngine.AI;

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
             "The NavMeshObstacle that makes a closed door genuinely closed for pathfinding is " +
             "now added automatically — see Auto Carve Nav Mesh below.")]
    [SerializeField] private bool nemesisCanOpen = true;

    [Tooltip("Whether the Nemesis can force the door even when it is locked or gated behind an " +
             "unsolved puzzle. On is the Mr. X behaviour: locks do not exist for it. Off makes it " +
             "respect the same conditions as the player.")]
    [SerializeField] private bool nemesisCanForceLocked = true;

    [Tooltip("Give the swinging leaf a NavMeshObstacle with Carve on Awake, when it has none.\n\n" +
             "ON by default, and it is what makes the Nemesis treat a closed door as closed. A " +
             "NavMeshAgent ignores physics colliders entirely, and this scene's NavMeshSurface " +
             "excludes layer Default from its bake, so without the obstacle the NavMesh runs " +
             "straight through the doorway and the monster walks through the panel.\n\n" +
             "Turn it off only for a door that already carries a hand-tuned obstacle whose shape " +
             "does not match the leaf collider.")]
    [SerializeField] private bool autoCarveNavMesh = true;

    private Quaternion hingeClosedLocalRot;
    private bool isOpen;
    private bool isAnimating;
    private bool wasEverOpened;
    // When the "locked door" bump is still audibly playing. Player mashing E while the clip is
    // ringing must not stack another instance on top; we skip until Time.unscaledTime passes this.
    // unscaledTime because the sound uses ignoreListenerPause and would otherwise stop being
    // rate-limited while the game is paused.
    private float blockedSoundBusyUntil;
    // Sign applied to openAngle for the CURRENT open cycle. Recomputed at every OpenDoor call from
    // the position of whoever is opening (player or Nemesis), so the leaf always swings AWAY from
    // them. Cached until CloseDoor so the reverse animation lands exactly back on the closed rot.
    private float openedSign = 1f;

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

        // After CacheClosedRotation, which is what validates the hinge reference this
        // depends on, and before ApplyOpenStateImmediate below: a door restored as already
        // open has to carve where the leaf actually ENDS UP, not where it started.
        EnsureNavMeshObstacle();

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

        Vector3? openerPos = ResolvePlayerPosition();
        openedSign = ResolveOpenSign(openerPos);
        StartCoroutine(AnimateOpen());

        // Only sing the "unlocked" chime the first time a door that actually HAD a lock is
        // defeated. A door with no required key was never locked, so there is nothing to unlock,
        // and a re-open reuses the swing animation without touching the lock state either.
        if (firstUnlock && doorData != null && doorData.RequiredKey != null && AudioManager.Exists)
            AudioManager.Instance.PlaySFX("sfx_interaction_puerta_desbloqueada", transform.position);

        string logId = doorData != null ? doorData.DoorId : gameObject.name;
        Debug.Log($"Door opened: {logId} (openedSign={openedSign}, opener={(openerPos.HasValue ? openerPos.Value.ToString("F2") : "null")})", this);
    }

    public override void OnInteractAttemptBlocked()
    {
        // Fires when the player presses E while looking at a door whose CanInteract returned
        // false. Restrict the sound to the "actually locked" case: no data means no lock, a door
        // that is currently animating is only busy (not locked), and an already-unlocked door is
        // never gated behind requirements the player might not have met.
        if (doorData == null) return;
        if (isOpen || isAnimating) return;
        if (wasEverOpened) return;
        if (!AudioManager.Exists) return;

        // Rate-limit: skip while the previous bump is still ringing. Length is looked up on the
        // AudioManager rather than cached on this door so a re-imported clip picks up its new
        // duration without every door prefab having to be re-saved.
        if (Time.unscaledTime < blockedSoundBusyUntil) return;

        AudioManager.Instance.PlaySFX("sfx_interaction_puerta_bloqueada", transform.position);
        float length = AudioManager.Instance.GetSoundLength("sfx_interaction_puerta_bloqueada");
        blockedSoundBusyUntil = Time.unscaledTime + length;
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

        // Nemesis-side swing: same "away from whoever is opening" rule, but the reference is the
        // Nemesis itself, not the player. Falls back to the player if we cannot locate one.
        Vector3? nemesisOpenerPos = null;
        NemesisController nemesis = Object.FindAnyObjectByType<NemesisController>();
        if (nemesis != null) nemesisOpenerPos = nemesis.transform.position;
        else nemesisOpenerPos = ResolvePlayerPosition();
        openedSign = ResolveOpenSign(nemesisOpenerPos);
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
                                  hingeClosedLocalRot * Quaternion.Euler(0f, openAngle * openedSign, 0f));
        isOpen = true;
        InteractionEvents.RequestPromptRefresh();
    }

private IEnumerator AnimateClose()
    {
        yield return AnimateHinge(hingeClosedLocalRot * Quaternion.Euler(0f, openAngle * openedSign, 0f),
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
            hinge.localRotation = hingeClosedLocalRot * Quaternion.Euler(0f, openAngle * openedSign, 0f);
    }

    // Tries every source of the player's world position, in order of reliability, and returns
    // the first hit. Kept as a helper because scenes without the full player-registration flow
    // wired up (e.g. isolated test scenes) leave PlayerRegistry.CurrentTransform null, which
    // would silently fall back to the previous "always open the same way" behaviour.
    private Vector3? ResolvePlayerPosition()
    {
        Transform t = PlayerRegistry.CurrentTransform;
        if (t != null) return t.position;

        GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
        if (taggedPlayer != null) return taggedPlayer.transform.position;

        if (Camera.main != null) return Camera.main.transform.position;

        Debug.LogWarning($"[{nameof(DoorInteractable)}] '{name}' could not resolve a player " +
                         "position when opening — the leaf will swing in its default direction.", this);
        return null;
    }

    // Returns +1 or -1 so that rotating the leaf by openAngle * sign moves it AWAY from
    // openerWorldPos. Simulates both signs on a sample point taken from the RENDERED leaf mesh
    // (combined renderer bounds of the hinge's children), then picks whichever ends up further
    // away in the XZ plane. The renderer sample matters: several door prefabs put a bare child
    // Transform at the hinge origin and the mesh only exists deeper down — a naive first-child
    // read leaves the offset at zero and both signs tie, silently locking the door to +1.
    // Falls back to +1 (previous behaviour) when there is no renderer to sample or no opener.
    private float ResolveOpenSign(Vector3? openerWorldPos)
    {
        if (hinge == null || !openerWorldPos.HasValue) return 1f;

        Vector3 pivot = hinge.position;
        if (!TryGetLeafSample(out Vector3 leafSample)) return 1f;

        Vector3 offset = leafSample - pivot;
        // Rotate only in the horizontal plane; the leaf's vertical extent has no bearing on
        // which side of the door the player is on.
        offset.y = 0f;
        if (offset.sqrMagnitude < 1e-6f) return 1f;

        Vector3 axis = Vector3.up;

        Vector3 posEnd = pivot + Quaternion.AngleAxis(+openAngle, axis) * offset;
        Vector3 negEnd = pivot + Quaternion.AngleAxis(-openAngle, axis) * offset;

        Vector3 target = openerWorldPos.Value;
        target.y = 0f;
        posEnd.y = 0f;
        negEnd.y = 0f;

        float dPos = (posEnd - target).sqrMagnitude;
        float dNeg = (negEnd - target).sqrMagnitude;
        return dPos >= dNeg ? 1f : -1f;
    }

    // Finds a world-space point that actually sits inside the leaf's rendered volume: the
    // combined centre of every Renderer under the hinge. Bare-Transform children at the pivot
    // are ignored because they contribute no bounds.
    private bool TryGetLeafSample(out Vector3 sample)
    {
        sample = default;

        Renderer[] renderers = hinge.GetComponentsInChildren<Renderer>(includeInactive: false);
        if (renderers == null || renderers.Length == 0) return false;

        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) combined.Encapsulate(renderers[i].bounds);

        sample = combined.center;
        return true;
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

    // ── NavMesh carving ─────────────────────────────────────────────────────

    /// <summary>
    /// Gives the swinging leaf a <see cref="NavMeshObstacle"/> with Carve, unless it already has
    /// one or this is switched off.
    ///
    /// <b>Why this is needed at all:</b> a NavMeshAgent ignores physics colliders completely — it
    /// obeys only the NavMesh and NavMeshObstacles. The leaf's solid collider lives on layer 0
    /// (Default), which the scene's NavMeshSurface deliberately excludes from its bake (that is
    /// what keeps ceilings from baking as walkable roofs), so door geometry contributes nothing to
    /// the NavMesh. The result is a NavMesh that runs straight through every doorway as if no door
    /// existed, and a Nemesis that walks through the closed panel instead of opening it.
    ///
    /// <b>Why an obstacle and not baked geometry:</b> the bake is static. Moving the leaf onto a
    /// baked layer would make the CLOSED door block correctly and leave the OPEN door blocking
    /// just as hard. A carving obstacle moves with the panel: closed it covers the doorway, open
    /// it carves to one side and frees the passage.
    ///
    /// <b>Why in code and not in the prefab:</b> same reason the Nemesis auto-adds its sibling
    /// components — no existing prefab or scene instance has to be re-saved, and the ~110 door
    /// instances already placed pick it up on load. A door that was given one by hand keeps it:
    /// this only ever ADDS a missing component, never reconfigures an existing one.
    /// </summary>
    private void EnsureNavMeshObstacle()
    {
        if (!autoCarveNavMesh || hinge == null) return;

        // Already carved by hand ANYWHERE on this door — leave it exactly as authored.
        // Searched from the door ROOT and not from the hinge on purpose: an obstacle placed
        // by hand may sit anywhere on the door, and a hinge-only check would miss one on
        // the root and give that door a second, competing carve.
        if (GetComponentInChildren<NavMeshObstacle>(includeInactive: true) != null) return;

        // The obstacle goes on whatever carries the leaf's SOLID collider, so it inherits that
        // object's rotation for free: it is a child of the hinge, so it swings with the door.
        // Triggers are skipped on purpose — the door's interaction volume wraps the whole doorway
        // and carving THAT would seal the opening permanently.
        BoxCollider leaf = FindLeafCollider();
        if (leaf == null)
        {
            Debug.LogWarning($"[{nameof(DoorInteractable)}] '{name}': no solid BoxCollider found " +
                             "under the hinge, so no NavMeshObstacle could be sized automatically. " +
                             "The Nemesis will path straight through this door. Add one by hand, " +
                             "or turn off Auto Carve Nav Mesh to silence this.", this);
            return;
        }

        NavMeshObstacle obstacle = leaf.gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;

        // Taken from the collider on the SAME GameObject, so centre and size are already in the
        // right local space and need no conversion.
        obstacle.center = leaf.center;
        obstacle.size = leaf.size;

        obstacle.carving = true;

        // Only re-carve once the panel has come to rest. Carving every frame of a 0.6s swing is
        // the expensive way to get the same answer, and it forces a NavMesh update while the agent
        // is mid-path through the doorway.
        obstacle.carveOnlyStationary = true;
    }

    /// <summary>
    /// The first non-trigger BoxCollider under the hinge — the panel the player physically bumps
    /// into. Restricted to BoxCollider because that is the only shape whose centre and size map
    /// onto a NavMeshObstacle without approximating it.
    /// </summary>
    private BoxCollider FindLeafCollider()
    {
        BoxCollider[] colliders = hinge.GetComponentsInChildren<BoxCollider>(includeInactive: true);

        foreach (BoxCollider candidate in colliders)
        {
            if (!candidate.isTrigger) return candidate;
        }

        return null;
    }
}
