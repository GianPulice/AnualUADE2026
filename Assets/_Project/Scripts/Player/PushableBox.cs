using UnityEngine;

/// <summary>
/// A push box hooked into the crosshair interaction system.
///
/// Detection now comes from <see cref="InteractionManager"/> raycasting through the crosshair, not
/// from the four child triggers colliding with the player. Those child transforms are still on the
/// prefab — they mark the position the player latches onto for each of the four cardinal faces,
/// authored per-box — but their <see cref="PushBoxTriggerLogic"/> callbacks no longer decide when
/// the interaction can happen. At grab time the closest anchor to the player (XZ) is picked, and
/// that anchor's position/direction is what <see cref="PlayerStateManager.SetPlayerPositionAndDirection"/>
/// consumes exactly like before, so the push behaviour and animation are untouched.
///
/// While grabbed the box pins itself as the forced interactable on the manager, so E keeps
/// releasing even if the player rotates the camera away from the mesh while pushing.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PushableBox : BaseRangeInteractable
{
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Seconds the box takes to slide to the centre of its matching basket after the " +
             "BasketTrigger confirms it is the correct one. Only X/Z are tweened; Y is preserved.")]
    [SerializeField, Min(0f)] private float snapDuration = 0.35f;

    [Header("Grab range")]
    [Tooltip("Shared config with the max grab distance and the out-of-range prompt. When null the " +
             "distance check is skipped and the box behaves exactly like the global interaction " +
             "reach allows — fine for boxes that do not need the closer gate, so leaving this " +
             "empty is a valid, backward-compatible authoring.")]
    [SerializeField] private SO_PushableBoxConfig config;

    [Header("Prompts")]
    [SerializeField] private string grabPrompt = "Press 'E' to push the box";
    [SerializeField] private string releasePrompt = "Press 'E' to stop pushing the box";

    private Rigidbody rb;
    // Authored side anchors — one Transform per face, read from the PushBoxTriggerLogic children
    // on Awake. Used ONLY as latch positions; their trigger callbacks no longer gate interaction.
    private Transform[] sideAnchors;

    private PlayerStateManager player;
    private Transform currentTriggerTransform;
    private bool isGrabbed;
    private bool locked;

    // Tracks IsWithinGrabRange between frames so Update fires a prompt refresh exactly on the
    // frame the answer flips (in-range ↔ out-of-range). Null means "no cached answer" — used
    // whenever the box is not the crosshair's current target, so re-acquiring the target does
    // not compare against a stale value.
    private bool? lastGrabInRange;

    // Legacy fields kept as public setters so any external code that still writes to them
    // (e.g. lingering references from PushBoxTriggerLogic) does not break. The values are not
    // read for interaction gating any more — the crosshair system decides that.
    public string PlayerTag { get => playerTag; set => playerTag = value; }
    public Transform CurrentTriggerTransform { get => currentTriggerTransform; set => currentTriggerTransform = value; }
    public PlayerStateManager Player { get => player; set => player = value; }
    public bool PlayerNearby { get; set; }
    public bool IsLocked => locked;

    protected override void Awake()
    {
        base.Awake();
        rb = GetComponent<Rigidbody>();
        CacheSideAnchors();
    }

    // The four PushBoxTriggerLogic children stay on the prefab: their transforms are the authored
    // latch positions for each cardinal face. Read once and cached so we do not walk the hierarchy
    // per interaction. Includes inactive on purpose: a face gated behind a puzzle may start off.
    private void CacheSideAnchors()
    {
        PushBoxTriggerLogic[] children = GetComponentsInChildren<PushBoxTriggerLogic>(true);
        sideAnchors = new Transform[children.Length];
        for (int i = 0; i < children.Length; i++) sideAnchors[i] = children[i].transform;

        if (sideAnchors.Length == 0)
        {
            Debug.LogWarning($"[{nameof(PushableBox)}] '{name}' has no side anchor children " +
                             $"({nameof(PushBoxTriggerLogic)}). Grab will fail because the player " +
                             "would have nowhere to latch on. Re-add the four side transforms.", this);
        }
    }

    // Watches the boundary between in-range and out-of-range while the crosshair is on this box.
    // Without this, the prompt only refreshes on TargetChanged (looking away and back) or on an
    // explicit RequestPromptRefresh — neither of which fires just because the player walked one
    // step closer. Costs nothing when the box is not the crosshair target: the early-outs skip
    // the distance math entirely.
    private void Update()
    {
        if (isGrabbed || locked) { lastGrabInRange = null; return; }

        if (!InteractionManager.Exists) return;
        if (!ReferenceEquals(InteractionManager.Instance.CurrentInteractable, this))
        {
            lastGrabInRange = null;
            return;
        }

        bool inRangeNow = IsWithinGrabRange();
        if (lastGrabInRange.HasValue && lastGrabInRange.Value == inRangeNow) return;

        lastGrabInRange = inRangeNow;
        InteractionEvents.RequestPromptRefresh();
    }


    // ── IInteractable ───────────────────────────────────────────────────────

    public override string GetInteractText() => isGrabbed ? releasePrompt : grabPrompt;

    public override bool IsRepeatable() => true;

    // The crosshair system already enforces global reach and line of sight; the box gates the
    // action further with its own state. A locked box (already snapped into its basket) refuses
    // everything, and while grabbed it stays available so E always releases. Otherwise it also
    // demands the player be closer than the config's MaxGrabDistance so the auto-slide onto the
    // anchor does not read as a teleport across the room.
    protected override bool CanInteractInCloseRange()
    {
        if (locked) return false;
        if (isGrabbed) return true;
        return IsWithinGrabRange();
    }

    // Info text shown by the prompt UI when CanInteract is false and the box still has something
    // to say ("move closer"). Empty otherwise so the locked / grabbed cases stay silent and the
    // prompt just fades out on its own.
    public override string GetInfoText()
    {
        if (locked || isGrabbed) return string.Empty;
        if (IsWithinGrabRange()) return string.Empty;
        return config != null ? config.OutOfRangePrompt : string.Empty;
    }

    // True when either no config is assigned (no gate) or the player is within MaxGrabDistance of
    // the closest side anchor, measured in the XZ plane so vertical offsets from crouch or from
    // props at different heights do not artificially fail the check.
    private bool IsWithinGrabRange()
    {
        if (config == null) return true;

        Vector3? playerPos = ResolvePlayerPositionForRange();
        if (!playerPos.HasValue) return true;

        Transform anchor = PickClosestAnchor(playerPos.Value);
        if (anchor == null) return true;

        Vector3 delta = anchor.position - playerPos.Value;
        delta.y = 0f;
        return delta.sqrMagnitude <= config.MaxGrabDistance * config.MaxGrabDistance;
    }

    // Registry first (cheap, correct), then tag lookup (safety net for scenes that do not wire
    // the registry). Kept separate from ResolvePlayer() because that one caches the found player
    // in the field, which we do not want during a per-frame CanInteract check.
    private Vector3? ResolvePlayerPositionForRange()
    {
        Transform t = PlayerRegistry.CurrentTransform;
        if (t != null) return t.position;

        GameObject tagged = GameObject.FindGameObjectWithTag(playerTag);
        if (tagged != null) return tagged.transform.position;

        return null;
    }

    protected override void OnInteract()
    {
        if (locked) return;
        if (!isGrabbed) Grab();
        else Release();
    }

    // ── Grab / Release ──────────────────────────────────────────────────────

    private void Grab()
    {
        PlayerStateManager resolved = ResolvePlayer();
        if (resolved == null)
        {
            Debug.LogWarning($"[{nameof(PushableBox)}] '{name}' could not resolve the player " +
                             "on grab. The box will not latch on.", this);
            return;
        }
        player = resolved;

        Transform anchor = PickClosestAnchor(player.transform.position);
        if (anchor == null) return;
        currentTriggerTransform = anchor;

        isGrabbed = true;
        rb.mass = 1;
        player.IsInteracting = true;

        Vector3 tempDir = new Vector3(transform.position.x - anchor.position.x, 0f,
                                      transform.position.z - anchor.position.z).normalized;
        player.SetPlayerPositionAndDirection(anchor.position, tempDir);

        // Pin the box as the active interactable so releasing with E still works if the crosshair
        // moves off the mesh while pushing. Cleared on Release / ForceRelease / LockAtBasket.
        if (InteractionManager.Exists)
            InteractionManager.Instance.SetForcedInteractable(this);

        // The target did not change (still this box) but its state did (isGrabbed flipped), so
        // fire a prompt-only refresh. Without this the UI would keep advertising the previous
        // action ("push") until the crosshair leaves the box and comes back.
        InteractionEvents.RequestPromptRefresh();
    }

    private void Release()
    {
        if (player != null) player.IsInteracting = false;
        isGrabbed = false;
        if (rb != null) rb.mass = 1000;

        if (InteractionManager.Exists)
            InteractionManager.Instance.ClearForcedInteractable(this);

        // Same reason as in Grab: same target, different state, so the prompt text has to be
        // re-read. The next Update's raycast will fire TargetChanged(null) on its own if the
        // crosshair has already left the box, so this refresh only affects the "still looking
        // at it" case and does not fight the fade-out otherwise.
        InteractionEvents.RequestPromptRefresh();
    }

    // Same as Release but survives a null Player. Used when the box is torn out of the player's
    // hands from the outside (LockAtBasket): the player reference may already be gone, and we
    // still need isGrabbed / mass / forced-interactable in a sane state.
    private void ForceRelease()
    {
        if (player != null) player.IsInteracting = false;
        isGrabbed = false;
        if (rb != null) rb.mass = 1000;

        if (InteractionManager.Exists)
            InteractionManager.Instance.ClearForcedInteractable(this);
    }

    // Nearest cached anchor to the player in the XZ plane. Y is ignored because the box is a
    // ground prop and Y differences would bias the pick against short players or crouched ones.
    private Transform PickClosestAnchor(Vector3 playerWorldPos)
    {
        if (sideAnchors == null || sideAnchors.Length == 0) return null;

        Transform best = null;
        float bestSqr = float.PositiveInfinity;
        for (int i = 0; i < sideAnchors.Length; i++)
        {
            Transform t = sideAnchors[i];
            if (t == null) continue;

            Vector3 delta = t.position - playerWorldPos;
            delta.y = 0f;
            float sqr = delta.sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = t;
            }
        }
        return best;
    }

    // The current field first (may have been set by a PushBoxTriggerLogic still active), then the
    // registry, then the tag lookup. The tag path is a last-resort safety net for scenes that do
    // not wire up PlayerRegistry.
    private PlayerStateManager ResolvePlayer()
    {
        if (player != null) return player;
        if (PlayerRegistry.Current != null) return PlayerRegistry.Current;

        GameObject tagged = GameObject.FindGameObjectWithTag(playerTag);
        return tagged != null ? tagged.GetComponent<PlayerStateManager>() : null;
    }

    /// <summary>
    /// Called by <see cref="BasketTrigger"/> when THIS box has just entered the basket it is meant
    /// for. Detaches the player from any push interaction, disables further grabs, freezes physics
    /// input, and slides the box to (target.x, currentY, target.z) over <see cref="snapDuration"/>.
    /// Y is never touched. Safe to call more than once — subsequent calls are ignored.
    /// </summary>
    public void LockAtBasket(Transform target)
    {
        if (locked) return;
        if (target == null)
        {
            Debug.LogWarning($"[{nameof(PushableBox)}] '{name}' LockAtBasket called with a null " +
                             "target. Ignoring.", this);
            return;
        }

        if (isGrabbed) ForceRelease();

        locked = true;

        // Freeze physics so no residual push or collision can drift the box off-centre while the
        // tween runs, and so the player cannot bump into it any more.
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // Kill the (now cosmetic) child triggers so a lingering PushBoxTriggerLogic cannot keep
        // writing to PlayerNearby / CurrentTriggerTransform after the box is locked.
        foreach (PushBoxTriggerLogic t in GetComponentsInChildren<PushBoxTriggerLogic>(true))
            t.enabled = false;

        Vector3 from = transform.position;
        Vector3 to = new Vector3(target.position.x, from.y, target.position.z);

        if (snapDuration <= 0f)
        {
            transform.position = to;
            return;
        }

        LeanTween.move(gameObject, to, snapDuration).setEaseOutCubic();
    }
}
