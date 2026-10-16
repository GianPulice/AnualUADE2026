using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A locked box that keeps a puzzle's reward until the player comes for it.
///
/// Pick the puzzle in the inspector (<see cref="puzzleId"/>, a dropdown of the puzzle ids that exist)
/// and that is the whole setup. From then on:
///   1. Locked: red glow, red light readable through the fog. E only says "Locked".
///   2. The puzzle is solved (<see cref="PuzzleStateManager.OnPuzzleCompleted"/>): the puzzle's
///      reward comes here instead of to the inventory (<see cref="PuzzleRewardDelivery"/> asks
///      <see cref="TryReceiveReward"/> first), and the box opens in a short skippable shot
///      (<see cref="RewardBoxCinematic"/>): the right lock springs and falls, then the left one,
///      the screen and the light go cyan, the lid opens on the item inside.
///   3. Open: E takes the item, like a pickup on the floor. The light goes out.
///
/// The power, the Architect, the checkpoint — everything on OnPuzzleCompleted — still happens the
/// moment the puzzle is solved, under the shot. What would happen to a player who cannot move
/// (the Nemesis waking, the Director's pressure and staged entrance) waits for the shot to end:
/// the box holds the puzzle in <see cref="PuzzleCompletionAftermath"/> until it is done.
///
/// Subscribe + catch-up like every other listener of a puzzle id: a box that loads with its puzzle
/// already solved opens at once, with no shot.
/// </summary>
[DisallowMultipleComponent]
public class PuzzleRewardBox : BaseRangeInteractable, IPromptPresentation
{
    [Header("Puzzle")]
    [Tooltip("The puzzle that opens this box. The box listens for it, keeps its reward and hands it " +
             "over when the player opens it.")]
    [PuzzleId, SerializeField] private string puzzleId;

    [Tooltip("The puzzle asset with that id, filled in by the inspector when the id is picked. Read " +
             "for its Reward Item. Not set by hand.")]
    [SerializeField] private ScriptableObject puzzleSource;

    [Tooltip("Gives this item instead of the puzzle's Reward Item. Empty = the puzzle's.")]
    [SerializeField] private SO_InventoryItem rewardOverride;

    [Header("Setup")]
    [SerializeField] private SO_RewardBoxConfig config;
    [SerializeField] private RewardBoxRig rig;

    [Tooltip("The cinematic's camera. Framed per box in the Scene view: move this, not the box.")]
    [SerializeField] private EscapeShotCamera shot;

    [Tooltip("What the shot keeps centred.")]
    [SerializeField] private Transform shotTarget;

    private enum BoxState { Locked, Opening, Stocked, Empty }

    private static readonly Dictionary<string, PuzzleRewardBox> Boxes =
        new Dictionary<string, PuzzleRewardBox>(StringComparer.Ordinal);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Boxes.Clear();

    private readonly RewardBoxCinematic cinematic = new RewardBoxCinematic();
    private BoxState state = BoxState.Locked;
    private SO_InventoryItem receivedReward;
    private SO_InventoryItem stockedItem;
    private bool registered;
    private bool presenting;
    private bool started;
    private bool skipRequested;
    private Coroutine opening;

    public string PuzzleId => puzzleId;
    public ScriptableObject PuzzleSource => puzzleSource;

    /// <summary>
    /// Hands <paramref name="item"/> to the box enabled for <paramref name="puzzleId"/>, if there is
    /// one. Called by <see cref="PuzzleRewardDelivery"/> before it gives anything to the inventory.
    /// </summary>
    public static bool TryReceiveReward(string puzzleId, SO_InventoryItem item)
    {
        if (string.IsNullOrWhiteSpace(puzzleId)) return false;
        if (!Boxes.TryGetValue(puzzleId, out PuzzleRewardBox box) || box == null) return false;

        if (item != null) box.receivedReward = item;
        return true;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        if (rig == null) rig = GetComponent<RewardBoxRig>();

        // Static event: subscribed for the object's lifetime, not while enabled (docs/CLAUDE.md).
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
    }

    private void OnDestroy()
    {
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
    }

    private void OnEnable()
    {
        Register();

        // Solved while this was switched off: open now, quietly.
        if (started && state == BoxState.Locked && IsSolved) OpenAtOnce();
    }

    private void OnDisable()
    {
        if (state == BoxState.Opening) FinishOpening(); // never leaves the player frozen
        StopPresenting();
        Unregister();
    }

    private void Start()
    {
        started = true;

        if (config == null || rig == null)
        {
            Debug.LogError($"[{nameof(PuzzleRewardBox)}] '{name}' is missing its config or rig. Run " +
                           "Tools > Puzzles > Set Up Reward Boxes.", this);
            return;
        }

        if (string.IsNullOrWhiteSpace(puzzleId))
            Debug.LogWarning($"[{nameof(PuzzleRewardBox)}] '{name}' has no puzzle: it stays locked forever.", this);

        if (IsSolved) OpenAtOnce();
        else rig.ShowLocked(config);
    }

    private void Update()
    {
        if (cinematic.IsPlaying && !skipRequested && SkipPressed()) skipRequested = true;
    }

    private void Register()
    {
        if (string.IsNullOrWhiteSpace(puzzleId)) return;

        if (Boxes.TryGetValue(puzzleId, out PuzzleRewardBox other) && other != null && other != this)
        {
            Debug.LogWarning($"[{nameof(PuzzleRewardBox)}] '{name}' and '{other.name}' are both boxes " +
                             $"for '{puzzleId}'. Only '{other.name}' gets the reward; this one stays locked.", this);
            return;
        }

        Boxes[puzzleId] = this;
        registered = true;

        // Only a box that can actually play its opening holds the puzzle: one that never finishes
        // would keep the Nemesis asleep for good.
        if (state == BoxState.Locked && !IsSolved && config != null && rig != null)
        {
            PuzzleCompletionAftermath.RegisterPresenter(puzzleId);
            presenting = true;
        }
    }

    private void Unregister()
    {
        if (!registered) return;
        registered = false;
        if (Boxes.TryGetValue(puzzleId, out PuzzleRewardBox current) && current == this) Boxes.Remove(puzzleId);
    }

    /// <summary>Lets the Nemesis and the Director react: the last presenter leaving settles the puzzle.</summary>
    private void StopPresenting()
    {
        if (!presenting) return;
        presenting = false;
        PuzzleCompletionAftermath.UnregisterPresenter(puzzleId);
    }

    private bool IsSolved => PuzzleStateManager.Exists && PuzzleStateManager.Instance.IsPuzzleCompleted(puzzleId);

    // ── Opening ─────────────────────────────────────────────────────────────

    private void HandlePuzzleCompleted(string completedId)
    {
        if (!string.Equals(completedId, puzzleId, StringComparison.Ordinal)) return;
        if (state != BoxState.Locked || !registered || config == null || rig == null) return;
        if (!isActiveAndEnabled) return; // OnEnable catches up

        opening = StartCoroutine(OpenSequence());
    }

    private IEnumerator OpenSequence()
    {
        state = BoxState.Opening;

        // The puzzle hands its reward over right after raising the completion, in the same call.
        yield return null;

        stockedItem = ResolveReward();
        if (stockedItem != null) rig.ShowItem();

        skipRequested = false;
        if (config.PlayCinematic && shot != null)
        {
            // A panel still closing, another cinematic, a capture: wait for the screen, but not forever.
            float waited = 0f;
            while (!RewardBoxCinematic.ScreenIsFree() && waited < config.MaxWaitSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            if (RewardBoxCinematic.ScreenIsFree())
                cinematic.Begin(shot, shotTarget != null ? shotTarget : transform, config);
        }

        yield return Wait(config.HoldBeforeSeconds);
        yield return OpenLock(RewardBoxRig.Side.Right);
        yield return Wait(config.GapBetweenLocksSeconds);
        yield return OpenLock(RewardBoxRig.Side.Left);

        if (!skipRequested)
        {
            rig.ShowUnlocked(config, true);
            PlaySound(config.UnlockedSoundId);
        }
        yield return Wait(config.HoldBeforeLidSeconds);

        if (!skipRequested)
        {
            float lidSeconds = rig.OpenLid();
            PlaySound(config.LidSoundId);
            yield return Wait(lidSeconds);
        }
        yield return Wait(config.HoldAfterSeconds);

        opening = null;
        FinishOpening();
    }

    private IEnumerator OpenLock(RewardBoxRig.Side side)
    {
        if (skipRequested) yield break;

        float latchSeconds = rig.OpenLatch(side, config);
        PlaySound(config.LatchSoundId);
        yield return Wait(latchSeconds);

        if (skipRequested) yield break;
        yield return rig.DropLock(side, config, () => skipRequested);
    }

    /// <summary>Lands every part on its final pose and hands everything back. Also the skip, and
    /// the way out when the box is switched off half-way.</summary>
    private void FinishOpening()
    {
        if (opening != null) StopCoroutine(opening);
        opening = null;

        state = stockedItem != null ? BoxState.Stocked : BoxState.Empty;
        rig.SnapOpen(config, state == BoxState.Stocked);

        cinematic.End(this);
        skipRequested = false;

        StopPresenting();
    }

    /// <summary>Already solved when it loads: open, stocked, no shot, no hold.</summary>
    private void OpenAtOnce()
    {
        if (config == null || rig == null) return;

        StopPresenting();
        stockedItem = ResolveReward();
        if (stockedItem != null) rig.ShowItem();
        state = stockedItem != null ? BoxState.Stocked : BoxState.Empty;
        rig.SnapOpen(config, state == BoxState.Stocked);
    }

    private IEnumerator Wait(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds && !skipRequested)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    // Legacy input, like the escape and the wake-up: the project runs both input backends.
    private bool SkipPressed()
    {
        if (!config.Skippable) return false;
        if (PauseManager.IsGameplayInputBlocked || LoadingScreen.IsLoading) return false;
        return Input.GetKeyDown(config.SkipKey);
    }

    private SO_InventoryItem ResolveReward()
    {
        if (rewardOverride != null) return rewardOverride;
        if (receivedReward != null) return receivedReward;

        if (puzzleSource is IPuzzleRewardSource source)
        {
            if (string.Equals(source.PuzzleId, puzzleId, StringComparison.Ordinal)) return source.RewardItem;

            Debug.LogWarning($"[{nameof(PuzzleRewardBox)}] '{name}' points at '{puzzleSource.name}', whose " +
                             $"id is '{source.PuzzleId}', not '{puzzleId}'. Select the box to re-link it.", this);
        }
        return null;
    }

    // ── Interaction ─────────────────────────────────────────────────────────

    public InteractionPromptKind Kind => state == BoxState.Stocked ? InteractionPromptKind.Item : InteractionPromptKind.Common;
    public Sprite PromptIcon => state == BoxState.Stocked && stockedItem != null ? stockedItem.ItemIcon : null;

    public override string GetPromptText()
    {
        if (config == null) return string.Empty;
        switch (state)
        {
            case BoxState.Locked: return config.LockedPrompt;
            case BoxState.Stocked: return string.Format(config.TakePromptFormat, stockedItem.ItemName);
            default: return string.Empty;
        }
    }

    /// <summary>"Locked" while locked, the hands-full reason while the item cannot be carried yet.
    /// Non-empty info is also what keeps the prompt up while E is refused.</summary>
    public override string GetInfoText()
    {
        if (config == null) return string.Empty;
        if (state == BoxState.Locked) return config.LockedInfo;
        if (state != BoxState.Stocked || !InventoryManager.Exists) return string.Empty;

        InventoryManager inventory = InventoryManager.Instance;
        if (inventory.CanCarry(stockedItem)) return string.Empty;

        SO_InventoryItem carried = inventory.CarriedSpecial;
        return carried != null && inventory.SpecialItemRules != null
            ? string.Format(inventory.SpecialItemRules.BlockedPickupFormat, carried.ItemName)
            : string.Empty;
    }

    protected override bool CanInteractInCloseRange()
    {
        if (state != BoxState.Stocked || stockedItem == null) return false;
        return !InventoryManager.Exists || InventoryManager.Instance.CanCarry(stockedItem);
    }

    protected override void OnInteract()
    {
        if (!InventoryManager.Exists)
        {
            Debug.LogWarning($"[{nameof(PuzzleRewardBox)}] No InventoryManager — '{stockedItem.ItemName}' " +
                             "stays in the box.", this);
            return;
        }

        PlayPickupSound(stockedItem);
        InventoryManager.Instance.AddItem(stockedItem);

        stockedItem = null;
        state = BoxState.Empty;
        rig.HideItem();
        rig.SetLightOn(false);
    }

    public override void OnInteractAttemptBlocked()
    {
        if (state == BoxState.Locked && config != null) PlaySound(config.LockedSoundId);
    }

    public override bool IsRepeatable() => state != BoxState.Empty;

    /// <summary>Emptied (or opened with nothing in it): no prompt, no highlight.</summary>
    public override bool IsFinished() => state == BoxState.Empty;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>Right-click the component in Play mode: solves this box's puzzle the way the puzzle
    /// itself would (no reward is delivered, so the box falls back to the puzzle's Reward Item).</summary>
    [ContextMenu("Test: solve this box's puzzle (Play mode)")]
    private void TestSolvePuzzle()
    {
        if (!Application.isPlaying || !PuzzleStateManager.Exists)
        {
            Debug.LogWarning($"[{nameof(PuzzleRewardBox)}] Only in Play mode, once the game is running.", this);
            return;
        }
        PuzzleStateManager.Instance.SetPuzzleCompleted(puzzleId);
    }
#endif

    private void PlayPickupSound(SO_InventoryItem item)
    {
        if (config.CategoryConfig == null || item == null) return;
        PlaySound(config.CategoryConfig.Get(item.Category).pickupSoundId);
    }

    private void PlaySound(string soundId)
    {
        if (string.IsNullOrWhiteSpace(soundId) || !AudioManager.Exists) return;
        AudioManager.Instance.PlaySFX(soundId, transform.position);
    }
}
