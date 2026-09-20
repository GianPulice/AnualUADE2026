using System.Collections;
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

    // Dedicated source for the "empujando_caja" loop — owned here rather than borrowed from the
    // AudioManager's shared pool so it can be paused/resumed the exact moment the box's own
    // velocity crosses the movement threshold, without another SFX in the pool ever hearing it.
    private AudioSource pushLoopSource;
    // World-space position of the box on the previous FixedUpdate — used to tell "actually moving"
    // from "grabbed but stuck against a wall / AFK", which is Rigidbody.linearVelocity's job on
    // paper but not in practice: kinematic snaps and micro-jitter both feed noise back into it.
    private Vector3 lastPushPos;
    // Cached so SnapToBasket can silence the push loop the FRAME the box lands on the basket, and
    // guarantee the "colocar_caja" sound plays with no overlap of the pushing sound underneath.
    private bool pushSoundPlaying;
    // Minimum XZ movement per second below which the box counts as still. Squared to avoid a
    // sqrt on every FixedUpdate; 0.01 m/s is small enough that a genuine push (0.5–1.5 m/s in
    // this project) reads as moving without the noise of physics jitter tripping it.
    private const float PushMoveThresholdSqr = 0.01f * 0.01f;

    // Push-loop shaping. The box's speed dips below the threshold for a physics step or two on
    // every bump, corner or change of direction; stopping on the first dip and restarting the
    // clip from zero on the next push is what made the loop stutter. So the loop only goes quiet
    // after the box has been still for PushStopGraceSeconds, fades instead of cutting (a hard
    // Stop clicks), and pauses rather than stops so the next push resumes mid-clip.
    private const float PushStopGraceSeconds = 0.15f;
    private const float PushFadeSeconds = 0.1f;
    // Pitch follows the box's actual speed as a fraction of SO_Movement.BoxPushSpeed, so a box
    // grinding against a wall sounds heavier than one sliding freely at full speed.
    private const float PushPitchMin = 0.9f;
    private const float PushPitchMax = 1.05f;
    private const float PushPitchChangePerSecond = 1f;
    // SO_SoundData.Volume as PlayLoop left it on the source; the fade scales from it.
    private float pushLoopBaseVolume = 1f;
    private float pushLoopFade;
    private float pushLoopTargetPitch = 1f;
    private float lastPushMovingTime = float.NegativeInfinity;
    private bool pushLoopPaused;
    // Authored side anchors — one Transform per face, read from the PushBoxTriggerLogic children
    // on Awake. Used ONLY as latch positions; their trigger callbacks no longer gate interaction.
    private Transform[] sideAnchors;

    private PlayerStateManager player;
    private Transform currentTriggerTransform;
    private bool isGrabbed;
    private bool locked;
    // True for the length of a reversible slide onto a basket. Separate from `locked` because that
    // one means "finished, never grabbable again" — a wrong guess has to slide in and still be
    // pullable afterwards.
    private bool snapping;

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

    /// <summary>
    /// A basket is currently driving this rigidbody and nothing else may write to it — either
    /// mid-slide onto one, or frozen there for good. The grab-snap pin reads this to know when to
    /// stop hard-writing the box's pose back, which would otherwise fight the slide.
    /// </summary>
    private bool BasketOwnsBody => locked || snapping;

    protected override void Awake()
    {
        base.Awake();
        rb = GetComponent<Rigidbody>();
        CacheSideAnchors();
        EnsurePushLoopSource();
    }

    private void OnEnable()
    {
        PlayerEvents.OnPlayerCaptured += HandlePlayerCaptured;
        ModuleEvents.OnExploded += HandleModuleExploded;
    }

    private void OnDisable()
    {
        PlayerEvents.OnPlayerCaptured -= HandlePlayerCaptured;
        ModuleEvents.OnExploded -= HandleModuleExploded;
    }

    /// <summary>
    /// Let go when a module explodes, too. The explosion cinematic locks the player and swings the
    /// camera onto the body, and resuming a push out of it left the latch half-broken (the snap
    /// towards the anchor re-ran from wherever the shot left the player). After the explosion the
    /// player is simply standing next to the box, free to grab it again.
    /// </summary>
    private void HandleModuleExploded(ModuleRuntime runtime)
    {
        if (!isGrabbed) return;
        ForceRelease();
        InteractionEvents.RequestPromptRefresh();
    }

    /// <summary>
    /// Let go the instant the Nemesis grabs the player.
    ///
    /// Nothing else does. IsInteracting is only ever cleared by <see cref="Release"/>, which needs
    /// an E press on a box the player is no longer standing next to, so a capture mid-push used to
    /// leave it stuck on. CheckpointManager then teleports the player to the checkpoint and hands
    /// control back, the FSM leaves Disabled for Idle — and PlayerIdleState reads that stale
    /// IsInteracting and drops straight back into PlayerBoxInteractingState. That state re-runs its
    /// 0.2s snap towards NextPosition, the anchor beside this box on the other side of the level,
    /// dragging the player back out of the checkpoint locked in the push animation with no way out:
    /// the release needs the box, and the box is out of grab range.
    ///
    /// The box was left just as wrong — still grabbed, still mass 1, still pinned as the forced
    /// interactable — which is why this releases rather than just clearing the player's flag.
    ///
    /// Hung off the capture and not CheckpointManager.OnRespawned because the defeat fallback never
    /// respawns, and a box still latched there would carry its state into the next run. The
    /// Architect's lines, which also set IsDisabled, deliberately do NOT release: they never move
    /// the player, so resuming the push afterwards is correct. The module explosion does release —
    /// see <see cref="HandleModuleExploded"/>.
    /// </summary>
    private void HandlePlayerCaptured(PlayerStateManager captured)
    {
        if (isGrabbed) ForceRelease();
    }

    // The loop source rides on the box so it inherits the box's world position (the sound is 3D).
    // Created here rather than authored on the prefab so no existing box prefab needs re-saving.
    private void EnsurePushLoopSource()
    {
        pushLoopSource = gameObject.AddComponent<AudioSource>();
        pushLoopSource.playOnAwake = false;
        pushLoopSource.loop = true;
        pushLoopSource.spatialBlend = 1f;
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
        TickPushLoop();

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


    // Reads the box's XZ displacement since the last physics step. When it is above the movement
    // threshold and the box is grabbed, the push loop plays; otherwise it stops. In FixedUpdate
    // rather than Update because the position we compare against is the physics-driven position,
    // which is what Rigidbody-based pushing writes. Locked boxes get no sound — SnapToBasket
    // owns the crossover from "empujando" to "colocar".
    private void FixedUpdate()
    {
        if (!isGrabbed || locked)
        {
            if (pushSoundPlaying) StopPushSound();
            return;
        }

        Vector3 delta = transform.position - lastPushPos;
        delta.y = 0f;
        float perSecondSqr = delta.sqrMagnitude / Mathf.Max(Time.fixedDeltaTime * Time.fixedDeltaTime, 1e-8f);
        // Gate on the push input too: pressing E snaps the player onto the box's anchor with a
        // 0.2s Slerp (PlayerBoxInteractingState.animTimer), and that snap nudges the box a few
        // centimetres even though the player never pressed a direction. Without this gate the
        // push loop would fire under the "grab" chirp on every latch. Reading the direction the
        // push state itself resolved keeps the two in step: the loop sounds exactly while the
        // player is actively driving the box, whichever of the four ways.
        bool pressingPush = player != null && player.PushDirection != Vector3.zero;
        bool moving = pressingPush && perSecondSqr > PushMoveThresholdSqr;
        lastPushPos = transform.position;

        if (moving)
        {
            lastPushMovingTime = Time.time;
            float pushCap = Mathf.Max(player.Movement != null ? player.Movement.BoxPushSpeed : 0f, 0.01f);
            float speed01 = Mathf.Clamp01(Mathf.Sqrt(perSecondSqr) / pushCap);
            pushLoopTargetPitch = Mathf.Lerp(PushPitchMin, PushPitchMax, speed01);
            if (!pushSoundPlaying) StartPushSound();
        }
        else if (pushSoundPlaying && Time.time - lastPushMovingTime > PushStopGraceSeconds)
        {
            StopPushSound();
        }
    }

    // Length of the Slerp in PlayerBoxInteractingState.animTimer. Kept as a local constant
    // instead of read from the player state because that field is private, and the value has
    // been fixed since the state was written — if it ever moves, both places want the same tweak.
    private const float SnapCollisionSuppressSeconds = 0.2f;

    // Turns off every Collider-pair between the box and the player, waits out the snap, and turns
    // them back on. Uses Physics.IgnoreCollision instead of layers so the box's own collision
    // with walls, floors and other props stays untouched. The pairs are re-enabled from a
    // captured list because relying on GetComponentsInChildren at the end would miss any collider
    // that has since been disabled or destroyed.
    private IEnumerator SuppressPlayerCollisionForSnap(PlayerStateManager snapPlayer)
    {
        if (snapPlayer == null) yield break;

        Collider[] playerCols = snapPlayer.GetComponentsInChildren<Collider>(includeInactive: false);
        Collider[] boxCols = GetComponentsInChildren<Collider>(includeInactive: false);

        foreach (Collider p in playerCols)
        {
            if (p == null || p.isTrigger) continue;
            foreach (Collider b in boxCols)
            {
                if (b == null || b.isTrigger) continue;
                Physics.IgnoreCollision(p, b, true);
            }
        }

        // Pin the box in place for the length of the snap.
        //
        // Kinematic + IgnoreCollision was not enough on its own — the box still drifted a few
        // centimetres when the player's transform-driven snap slid it into a collider the
        // physics engine had not yet caught up with. Freezing every constraint on the rigidbody
        // AND hard-writing the transform back on every physics step during the snap guarantees
        // zero movement regardless of the cause: contact from another prop, residual velocity,
        // depenetration solver, anything. Both are restored at the end.
        RigidbodyConstraints originalConstraints = RigidbodyConstraints.None;
        bool wasKinematic = false;
        Vector3 pinnedPos = transform.position;
        Quaternion pinnedRot = transform.rotation;
        if (rb != null)
        {
            wasKinematic = rb.isKinematic;
            originalConstraints = rb.constraints;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.constraints = RigidbodyConstraints.FreezeAll;
        }

        // Hold pose over the length of the snap. WaitForFixedUpdate rather than WaitForSeconds
        // so the pin runs on the same timeline as the physics step that could otherwise nudge
        // the box, and so the transform write below actually cancels penetration resolution
        // from that step. The seconds-based deadline is compared against real time.
        float pinDeadline = Time.time + SnapCollisionSuppressSeconds;
        while (Time.time < pinDeadline && !BasketOwnsBody)
        {
            yield return new WaitForFixedUpdate();
            if (rb != null)
            {
                rb.position = pinnedPos;
                rb.rotation = pinnedRot;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        // Restore only if a basket has not already taken over the rigidbody. It sets its own
        // kinematic + zeroed-velocity state on purpose (see BasketOwnsBody) and any writeback here
        // would undo that mid-snap onto the basket.
        if (rb != null && !BasketOwnsBody)
        {
            rb.constraints = originalConstraints;
            rb.isKinematic = wasKinematic;
        }

        foreach (Collider p in playerCols)
        {
            if (p == null || p.isTrigger) continue;
            foreach (Collider b in boxCols)
            {
                if (b == null || b.isTrigger) continue;
                Physics.IgnoreCollision(p, b, false);
            }
        }
    }

    // pushSoundPlaying is the loop's target ("should be audible"); TickPushLoop moves the actual
    // volume toward it. A push that resumes while the loop is still fading out just reverses the
    // fade, one that resumes after it went quiet unpauses, and only the very first push (or the
    // first after a hard stop) loads the clip.
    private void StartPushSound()
    {
        if (pushLoopSource == null) return;

        if (pushLoopPaused)
        {
            pushLoopSource.UnPause();
            pushLoopPaused = false;
        }
        else if (!pushLoopSource.isPlaying)
        {
            if (!AudioManager.Exists) return;
            AudioManager.Instance.PlayLoop("sfx_empujando_caja", pushLoopSource);
            if (!pushLoopSource.isPlaying) return; // Unknown id or missing clip; AudioManager logged it.

            pushLoopBaseVolume = pushLoopSource.volume;
            pushLoopFade = 0f;
            pushLoopSource.volume = 0f;
            pushLoopSource.pitch = pushLoopTargetPitch;
            // Random entry point so every fresh push does not open on the same attack.
            if (pushLoopSource.clip != null)
                pushLoopSource.time = Random.Range(0f, pushLoopSource.clip.length);
        }

        pushSoundPlaying = true;
    }

    /// <param name="immediate">Cut with no fade. Only for SnapToBasket, where the loop must be
    /// gone before the "colocar_caja" one-shot starts.</param>
    private void StopPushSound(bool immediate = false)
    {
        pushSoundPlaying = false;
        if (!immediate || pushLoopSource == null) return;

        pushLoopSource.Stop();
        pushLoopSource.volume = 0f;
        pushLoopFade = 0f;
        pushLoopPaused = false;
    }

    // Runs every frame, grabbed or not, so a fade-out started by Release still completes.
    private void TickPushLoop()
    {
        if (pushLoopSource == null || !pushLoopSource.isPlaying) return;

        float target = pushSoundPlaying ? 1f : 0f;
        pushLoopFade = Mathf.MoveTowards(pushLoopFade, target, Time.deltaTime / PushFadeSeconds);
        pushLoopSource.volume = pushLoopBaseVolume * pushLoopFade;
        pushLoopSource.pitch = Mathf.MoveTowards(pushLoopSource.pitch, pushLoopTargetPitch,
                                                 PushPitchChangePerSecond * Time.deltaTime);

        if (!pushSoundPlaying && pushLoopFade <= 0f)
        {
            pushLoopSource.Pause();
            pushLoopPaused = true;
        }
    }

    // ── IInteractable ───────────────────────────────────────────────────────

    public override string GetInteractText() => isGrabbed ? releasePrompt : grabPrompt;

    public override bool IsRepeatable() => true;

    // The crosshair system already enforces global reach and line of sight; the box gates the
    // action further with its own state. A locked box (already snapped into its basket) refuses
    // everything, and while grabbed it stays available so E always releases. Otherwise it also
    // demands the player be closer than the config's MaxGrabDistance so the auto-slide onto the
    // anchor does not read as a teleport across the room.
    /// <summary>Locked into its basket: it can never be grabbed again.</summary>
    public override bool IsFinished() => locked;

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
        player.SetPushedBox(rb);
        player.IsInteracting = true;

        // Suspend player↔box collision for the length of the 0.2s snap animation in
        // PlayerBoxInteractingState. During that snap the player's collider slides onto the
        // anchor, and any residual contact used to visibly nudge the box (mass 1 → box moves) or,
        // when we kept the box heavy, shove the player back instead. Neither read as
        // "interacting" — the whole point is that pressing E must not move anything on screen.
        // Restored after the snap so the normal push physics takes over on the first W.
        StartCoroutine(SuppressPlayerCollisionForSnap(player));

        Vector3 tempDir = new Vector3(transform.position.x - anchor.position.x, 0f,
                                      transform.position.z - anchor.position.z).normalized;
        player.SetPlayerPositionAndDirection(anchor.position, tempDir);

        // Pin the box as the active interactable so releasing with E still works if the crosshair
        // moves off the mesh while pushing. Cleared on Release / ForceRelease / SnapToBasket.
        if (InteractionManager.Exists)
            InteractionManager.Instance.SetForcedInteractable(this);

        // One-shot "grabbed the box" chirp. Distinct from the push loop below, which only kicks
        // in once the box is actually moving.
        if (AudioManager.Exists) AudioManager.Instance.PlaySFX("sfx_interaction_box", transform.position);

        // Reset the movement sampler so the first FixedUpdate after grabbing does not see a huge
        // delta between "wherever the box was last frame" and its current position.
        lastPushPos = transform.position;

        // The target did not change (still this box) but its state did (isGrabbed flipped), so
        // fire a prompt-only refresh. Without this the UI would keep advertising the previous
        // action ("push") until the crosshair leaves the box and comes back.
        InteractionEvents.RequestPromptRefresh();
    }

    private void Release()
    {
        if (player != null)
        {
            player.IsInteracting = false;
            player.ClearPushedBox(rb);
        }
        isGrabbed = false;
        if (rb != null) rb.mass = 1000;

        if (InteractionManager.Exists)
            InteractionManager.Instance.ClearForcedInteractable(this);

        StopPushSound();

        // Same reason as in Grab: same target, different state, so the prompt text has to be
        // re-read. The next Update's raycast will fire TargetChanged(null) on its own if the
        // crosshair has already left the box, so this refresh only affects the "still looking
        // at it" case and does not fight the fade-out otherwise.
        InteractionEvents.RequestPromptRefresh();
    }

    // Same as Release but survives a null Player. Used when the box is torn out of the player's
    // hands from the outside (SnapToBasket): the player reference may already be gone, and we
    // still need isGrabbed / mass / forced-interactable in a sane state.
    private void ForceRelease()
    {
        if (player != null)
        {
            player.IsInteracting = false;
            player.ClearPushedBox(rb);
        }
        isGrabbed = false;
        if (rb != null) rb.mass = 1000;

        if (InteractionManager.Exists)
            InteractionManager.Instance.ClearForcedInteractable(this);

        StopPushSound();
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
    /// Centres the box on a basket it has just entered: detaches the player from the push, plays
    /// the "placed" sound and slides the box to (target.x, currentY, target.z) over
    /// <see cref="snapDuration"/>. Y is never touched.
    ///
    /// REVERSIBLE on purpose, and that is the whole point of the method existing separately from
    /// <see cref="LockInPlace"/>. The box stays grabbable, so one dropped in the wrong basket can
    /// be pulled straight back out. Making the wrong placement physically impossible is what let
    /// the player solve the puzzle by trying every basket until one accepted the box, without ever
    /// reading the symbols; the puzzle already refuses to complete until every box is in its own
    /// basket (<c>ContainerPuzzleController.CheckContainers</c>), so the validation does not need
    /// to double as the feedback.
    /// </summary>
    public void SnapToBasket(Transform target)
    {
        if (locked) return;
        if (target == null)
        {
            Debug.LogWarning($"[{nameof(PushableBox)}] '{name}' SnapToBasket called with a null " +
                             "target. Ignoring.", this);
            return;
        }

        if (isGrabbed) ForceRelease();

        // Silence the push loop the same frame the box lands on the basket, so it does not bleed
        // under the "colocar_caja" one-shot. ForceRelease above does not touch it, and even if it
        // did the ordering matters: stop first, then play, otherwise the loop's own Stop could
        // race the one-shot on the shared bus.
        StopPushSound(immediate: true);
        if (AudioManager.Exists)
            AudioManager.Instance.PlaySFX("sfx_colocar_caja", transform.position);

        Vector3 to = new Vector3(target.position.x, transform.position.y, target.position.z);

        if (snapDuration <= 0f)
        {
            transform.position = to;
            InteractionEvents.RequestPromptRefresh();
            return;
        }

        // Kinematic for the length of the slide only. LeanTween writes the transform directly and
        // the solver would fight it the whole way; EndSnap hands the box back to physics so it can
        // be pushed out again.
        snapping = true;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // A box nudged out and straight back in would otherwise leave two tweens racing for the
        // same transform, and whichever finished last would win.
        LeanTween.cancel(gameObject);
        LeanTween.move(gameObject, to, snapDuration).setEaseOutCubic().setOnComplete(EndSnap);

        // The crosshair may still be on this box after the snap starts; without a refresh the UI
        // keeps advertising the prompt from before the slide ("stop pushing the box").
        InteractionEvents.RequestPromptRefresh();
    }

    private void EndSnap()
    {
        snapping = false;

        // The puzzle may have been completed during the slide, which freezes the box for good.
        // Handing it back to physics here would undo exactly that.
        if (locked) return;

        if (rb != null) rb.isKinematic = false;
    }

    /// <summary>
    /// Freezes the box where it stands, permanently: no more grabbing, no more physics.
    ///
    /// Called by <c>ContainerPuzzleController</c> once the whole puzzle is solved, so a finished
    /// arrangement cannot be taken apart again. Safe to call more than once. Does not move the box
    /// — by the time the puzzle is solved every box is already sitting in its basket.
    /// </summary>
    public void LockInPlace()
    {
        if (locked) return;

        if (isGrabbed) ForceRelease();
        StopPushSound(immediate: true);

        locked = true;

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

        InteractionEvents.RequestPromptRefresh();
    }
}
