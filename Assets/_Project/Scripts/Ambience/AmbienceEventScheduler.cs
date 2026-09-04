using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ambience Layer 2 — decides WHEN and WHICH ambient one-shot plays. AmbiencePlacementResolver
/// decides where, AmbienceEventPool plays it.
///
/// THE RHYTHM, AND WHY IT IS NOT WHAT THE SPEC ASKED FOR
/// The spec proposes: wait 8-35 s, then a 30% chance to play nothing and re-roll the timer. The mean
/// arithmetic works out — 21.5 s / 0.7 = 30.7 s between sounds — but the SHAPE does not. The gap is a
/// geometric mixture of uniforms: P(1 cycle) = 0.70, P(2) = 0.21, P(3) = 0.063. A single cycle can
/// never exceed 35 s, so P(gap greater than 60 s) works out to about 7.6%. The 30% skip buys almost
/// no long silences; it only multiplies the mean.
///
/// What the spec actually describes wanting — "20 seconds of nothing, CLANG, then another 40 seconds
/// of nothing" — is a long TAIL, so the randomness belongs in the length of the wait rather than in
/// whether the wait repeats. Hence: always play at the end of the wait, but 22% of the time extend
/// the wait by 40-100 s. Mean gap 34.4 s, close to the original, but 22% of gaps now land between
/// 48 s and 130 s.
///
/// The spec-literal behaviour is still available: set skipChance to 0.3 and longSilenceChance to 0.
///
/// SILENCE IS PART OF THE SYSTEM. This is the component where that gets decided, so resist the urge
/// to tighten the gaps because a playtest felt empty. Empty is the point — a dead building that only
/// occasionally seems to move on its own is far more unsettling than a steady stream of events.
/// </summary>
public class AmbienceEventScheduler : MonoBehaviour
{
    [Header("Rhythm")]
    [Tooltip("Seconds to wait before the next event. See the class summary for what these numbers " +
             "produce.")]
    [SerializeField] private Vector2 gapRange = new Vector2(8f, 30f);

    [Tooltip("Chance the wait is extended into a long silence. This is what actually produces the " +
             "emptiness the design wants.")]
    [SerializeField, Range(0f, 1f)] private float longSilenceChance = 0.22f;

    [Tooltip("Extra seconds added when a long silence is rolled.")]
    [SerializeField] private Vector2 longSilenceExtra = new Vector2(40f, 100f);

    [Tooltip("The spec-literal alternative: chance that the timer expires and nothing plays, " +
             "re-rolling instead. Left at 0 by default because it does not deliver long silences " +
             "(see the class summary). Set it to 0.3 and longSilenceChance to 0 to A/B the original.")]
    [SerializeField, Range(0f, 1f)] private float skipChance = 0f;

    [Tooltip("Seconds of quiet after the level loads, on top of the first random gap. Stops a clang " +
             "from landing on top of the load fade.")]
    [SerializeField, Min(0f)] private float entryGraceSeconds = 4f;

    [Header("Repetition")]
    [Tooltip("How many recently played clips are remembered.\n\n" +
             "The window is clamped against the size of the tier being rolled, so a six-entry RARE " +
             "tier is never starved down to a single deterministic choice.")]
    [SerializeField, Range(1, 8)] private int historyCapacity = 3;

    [Tooltip("Weight multiplier applied to a recently played clip. A SOFT penalty rather than a hard " +
             "exclusion: the anti-repetition rule really exists for COMMON, which has enough entries " +
             "to spare, and a hard version of it is what breaks the small RARE tier.")]
    [SerializeField, Range(0f, 1f)] private float recentPenalty = 0.08f;

    [Header("Placement failure")]
    [Tooltip("Short retry window used when no valid position could be found, instead of waiting a " +
             "whole gap.")]
    [SerializeField] private Vector2 placementRetryRange = new Vector2(2f, 5f);

    [Tooltip("Consecutive placement failures before a warning is logged. Repeated failures mean the " +
             "zone geometry is hostile — usually the layer masks are wrong, or the area is not " +
             "covered by NavMesh.")]
    [SerializeField, Min(1)] private int placementFailureWarnThreshold = 5;

    [Header("Debug")]
    [Tooltip("Logs the tier, clip, distance, occlusion and anchor of every event.")]
    [SerializeField] private bool debugLogEvents = false;

    [Tooltip("Logs the derived timing statistics on Start, so the rhythm is tuned against real " +
             "numbers instead of guesses.")]
    [SerializeField] private bool logDerivedStatistics = true;

    [Tooltip("EDITOR ONLY — clamps the wait to 1-2 seconds so the system can be auditioned in " +
             "thirty seconds instead of ten minutes. Has no effect in a build, and warns loudly on " +
             "Start if it was left on.\n\n" +
             "The field is serialized unconditionally on purpose: wrapping it in #if UNITY_EDITOR " +
             "would make the serialized layout differ between the editor and a build.")]
    [SerializeField] private bool debugRapidFire = false;

    private AmbienceController controller;
    private AmbiencePlacementResolver resolver;
    private AmbienceEventPool pool;

    private Camera listenerCamera;
    private Transform playerTransform;

    private float countdown;
    private bool initialized;

    private float intervalScale = 1f;   // set by the future tension layer

    private int consecutivePlacementFailures;
    private bool warnedAboutEmptyTierFallback;

    private readonly List<AudioClip> recentClips = new List<AudioClip>();
    private readonly Dictionary<AudioClip, float> lastPlayedTime = new Dictionary<AudioClip, float>();
    private readonly List<float> weightScratch = new List<float>();

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Wires the collaborators and arms the first timer. Called by AmbienceController in Start.</summary>
    public void Initialize(AmbienceController ambienceController,
                           AmbiencePlacementResolver placementResolver,
                           AmbienceEventPool eventPool)
    {
        controller = ambienceController;
        resolver = placementResolver;
        pool = eventPool;

        countdown = entryGraceSeconds + Random.Range(gapRange.x, gapRange.y);
        initialized = true;

        if (logDerivedStatistics) LogDerivedStatistics();

        if (debugRapidFire)
        {
            Debug.LogWarning($"[{nameof(AmbienceEventScheduler)}] debugRapidFire is ON. Events fire " +
                             "every 1-2 seconds. This is an auditioning aid — turn it off before " +
                             "judging the pacing, and before building.", this);
        }
    }

    /// <summary>
    /// Multiplies the wait between events. Below 1 makes the building busier, above 1 emptier.
    /// Reserved for the enemy-proximity layer; nothing calls this yet.
    /// </summary>
    public void SetIntervalScale(float scale) => intervalScale = Mathf.Max(0.01f, scale);

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        // SubscribeAndCatchUp in OnEnable is the documented idiom for PlayerRegistry specifically —
        // it is the exception to the project's "static events in Awake/OnDestroy" rule, because the
        // player lives in an additive scene that may load after this component.
        PlayerRegistry.SubscribeAndCatchUp(HandlePlayerRegistered);
    }

    private void OnDisable()
    {
        PlayerRegistry.Unsubscribe(HandlePlayerRegistered);
    }

    private void Update()
    {
        if (!initialized) return;

        // IsPaused, deliberately NOT IsGameplayInputBlocked: opening the inventory should not stop
        // the factory. And scaled deltaTime, so a paused or slowed game thins the events out — the
        // opposite choice from the volume envelopes, which use unscaled time because a frozen fade
        // is audible while a frozen timer is not.
        if (PauseManager.Exists && PauseManager.Instance.IsPaused) return;

        if (!IsReady()) return;

        countdown -= Time.deltaTime;
        if (countdown > 0f) return;

        TryFireEvent();
    }

    // ── Firing ───────────────────────────────────────────────────────────────

    private void TryFireEvent()
    {
        SO_AmbienceProfile profile = controller.ActiveProfile;
        SO_AmbienceEventBank bank = profile != null ? profile.EventBank : null;

        if (bank == null || !bank.HasAnyPlayableEntry())
        {
            ScheduleNextGap(profile);
            return;
        }

        // The spec-literal skip branch, off by default.
        if (skipChance > 0f && Random.value < skipChance)
        {
            ScheduleNextGap(profile);
            return;
        }

        SO_AmbienceEventBank.ETier tier = RollTier(profile);
        SO_AmbienceEventBank.Entry[] entries = ResolveTierWithFallback(bank, ref tier);

        if (entries == null)
        {
            ScheduleNextGap(profile);
            return;
        }

        SO_AmbienceEventBank.Entry entry = PickEntry(entries);
        if (entry == null)
        {
            ScheduleNextGap(profile);
            return;
        }

        if (!resolver.TryResolvePosition(entry, listenerCamera, playerTransform.position, tier,
                                         out AmbiencePlacement placement))
        {
            HandlePlacementFailure();
            return;
        }

        consecutivePlacementFailures = 0;

        if (!pool.TryPlay(entry, placement, out float volume, out float pitch))
        {
            // Every source busy. Skip rather than growing the pool or cutting something off — if six
            // ambient one-shots are already ringing, the building is not short of atmosphere.
            ScheduleNextGap(profile);
            return;
        }

        RegisterPlayed(entry.clip);

        AmbienceEvents.RaiseEventPlayed(new AmbienceEventPlayback(
            entry.clip, placement.Position, tier, placement.Occluded, placement.Anchor, volume, pitch));

        if (debugLogEvents) LogEvent(entry, tier, placement, volume, pitch);

        ScheduleNextGap(profile);
    }

    private void HandlePlacementFailure()
    {
        consecutivePlacementFailures++;

        // A short retry rather than a whole gap: a failure usually means the player is momentarily
        // somewhere awkward, not that the area is unusable.
        countdown = Random.Range(placementRetryRange.x, placementRetryRange.y);

        if (consecutivePlacementFailures != placementFailureWarnThreshold) return;

        Debug.LogWarning($"[{nameof(AmbienceEventScheduler)}] {consecutivePlacementFailures} " +
                         "consecutive placement failures near " +
                         $"{playerTransform.position}.\n  {resolver.GetRejectionSummary()}", this);
    }

    private void ScheduleNextGap(SO_AmbienceProfile profile)
    {
#if UNITY_EDITOR
        if (debugRapidFire)
        {
            countdown = Random.Range(1f, 2f);
            return;
        }
#endif

        float wait = Random.Range(gapRange.x, gapRange.y);

        if (Random.value < longSilenceChance)
            wait += Random.Range(longSilenceExtra.x, longSilenceExtra.y);

        float profileScale = profile != null ? profile.EventIntervalScale : 1f;

        countdown = wait * profileScale * intervalScale;
    }

    // ── Selection ────────────────────────────────────────────────────────────

    private SO_AmbienceEventBank.ETier RollTier(SO_AmbienceProfile profile)
    {
        float common   = profile != null ? profile.CommonWeight   : 0.6f;
        float uncommon = profile != null ? profile.UncommonWeight : 0.3f;
        float rare     = profile != null ? profile.RareWeight     : 0.1f;

        float total = common + uncommon + rare;
        if (total <= 0f) return SO_AmbienceEventBank.ETier.Common;

        float roll = Random.value * total;

        if (roll < common) return SO_AmbienceEventBank.ETier.Common;
        if (roll < common + uncommon) return SO_AmbienceEventBank.ETier.Uncommon;
        return SO_AmbienceEventBank.ETier.Rare;
    }

    /// <summary>
    /// The entries for the rolled tier, falling down through Uncommon to Common if it is empty. A
    /// bank with only common sounds in it is a legitimate work-in-progress state, so this warns once
    /// rather than every time.
    /// </summary>
    private SO_AmbienceEventBank.Entry[] ResolveTierWithFallback(
        SO_AmbienceEventBank bank, ref SO_AmbienceEventBank.ETier tier)
    {
        for (int t = (int)tier; t >= 0; t--)
        {
            SO_AmbienceEventBank.Entry[] entries = bank.GetTier((SO_AmbienceEventBank.ETier)t);
            if (entries.Length == 0) continue;

            if (t != (int)tier && !warnedAboutEmptyTierFallback)
            {
                warnedAboutEmptyTierFallback = true;
                Debug.LogWarning($"[{nameof(AmbienceEventScheduler)}] Bank '{bank.name}' has an " +
                                 $"empty {tier} tier; falling back to " +
                                 $"{(SO_AmbienceEventBank.ETier)t}. This message appears once.", this);
            }

            tier = (SO_AmbienceEventBank.ETier)t;
            return entries;
        }

        return null;
    }

    /// <summary>
    /// Weighted pick with a soft repetition penalty.
    ///
    /// Degrades safely by construction:
    ///   6 entries, 3 recent  — the 3 untouched ones share about 92% of the weight; the recent ones
    ///                          stay reachable at about 8%. No starvation.
    ///   4 entries, 3 recent  — the untouched one gets about 80%, the others about 7% each. Still
    ///                          non-degenerate.
    ///   2 entries            — the window clamps to 1, and the hard "never twice in a row" rule
    ///                          alone alternates them, which is correct.
    ///   1 entry              — the hard rule is skipped, so the single clip plays. No deadlock.
    /// </summary>
    private SO_AmbienceEventBank.Entry PickEntry(SO_AmbienceEventBank.Entry[] entries)
    {
        int playableCount = CountPlayable(entries);
        if (playableCount == 0) return null;

        int window = Mathf.Min(historyCapacity, Mathf.Max(1, playableCount - 1));
        AudioClip lastClip = recentClips.Count > 0 ? recentClips[recentClips.Count - 1] : null;

        weightScratch.Clear();
        float total = 0f;

        for (int i = 0; i < entries.Length; i++)
        {
            SO_AmbienceEventBank.Entry entry = entries[i];

            if (entry == null || entry.clip == null)
            {
                weightScratch.Add(0f);
                continue;
            }

            float weight = 1f;

            // Hard rule, and the only hard one: never the same clip twice in a row.
            if (entry.clip == lastClip && playableCount >= 2)
            {
                weight = 0f;
            }
            else if (IsRecent(entry.clip, window))
            {
                weight *= recentPenalty;
            }

            if (entry.cooldown > 0f &&
                lastPlayedTime.TryGetValue(entry.clip, out float playedAt) &&
                Time.time - playedAt < entry.cooldown)
            {
                weight = 0f;
            }

            weightScratch.Add(weight);
            total += weight;
        }

        // Every candidate blocked by a cooldown or the previous-clip rule: ignore all of it and pick
        // uniformly rather than playing nothing. A slightly repetitive factory beats a silent one.
        if (total <= 0f) return PickUniform(entries, playableCount);

        float roll = Random.value * total;

        for (int i = 0; i < weightScratch.Count; i++)
        {
            roll -= weightScratch[i];
            if (roll > 0f) continue;
            return entries[i];
        }

        return PickUniform(entries, playableCount);
    }

    private static SO_AmbienceEventBank.Entry PickUniform(SO_AmbienceEventBank.Entry[] entries,
                                                          int playableCount)
    {
        int target = Random.Range(0, playableCount);

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] == null || entries[i].clip == null) continue;
            if (target == 0) return entries[i];
            target--;
        }

        return null;
    }

    private static int CountPlayable(SO_AmbienceEventBank.Entry[] entries)
    {
        int count = 0;
        for (int i = 0; i < entries.Length; i++)
            if (entries[i] != null && entries[i].clip != null) count++;
        return count;
    }

    private bool IsRecent(AudioClip clip, int window)
    {
        int start = Mathf.Max(0, recentClips.Count - window);

        for (int i = start; i < recentClips.Count; i++)
            if (recentClips[i] == clip) return true;

        return false;
    }

    private void RegisterPlayed(AudioClip clip)
    {
        recentClips.Add(clip);
        while (recentClips.Count > historyCapacity) recentClips.RemoveAt(0);

        lastPlayedTime[clip] = Time.time;
    }

    // ── Readiness ────────────────────────────────────────────────────────────

    private bool IsReady()
    {
        if (controller == null || resolver == null || pool == null) return false;

        if (playerTransform == null)
        {
            if (!PlayerRegistry.HasPlayer) return false;
            playerTransform = PlayerRegistry.CurrentTransform;
        }

        if (listenerCamera == null)
        {
            // The AudioListener lives on the scene camera, not on the player prefab, so this is the
            // real listener. Re-acquired rather than cached once: the camera belongs to the gameplay
            // scene and can be replaced.
            listenerCamera = Camera.main;
            if (listenerCamera == null) return false;
        }

        return true;
    }

    private void HandlePlayerRegistered(PlayerStateManager player)
    {
        playerTransform = player != null ? player.transform : null;
    }

    // ── Debug ────────────────────────────────────────────────────────────────

    private void LogEvent(SO_AmbienceEventBank.Entry entry, SO_AmbienceEventBank.ETier tier,
                          AmbiencePlacement placement, float volume, float pitch)
    {
        float distance = Vector3.Distance(listenerCamera.transform.position, placement.Position);
        string anchorName = placement.Anchor != null ? placement.Anchor.name : "random";
        string label = string.IsNullOrEmpty(entry.label) ? entry.clip.name : entry.label;

        Debug.Log($"[Ambience] {tier} '{label}' at {placement.Position} d={distance:F1}m " +
                  $"occluded={placement.Occluded} anchor={anchorName} " +
                  $"vol={volume:F2} pitch={pitch:F2}");
    }

    /// <summary>
    /// Logs what the current settings actually produce. With no automated tests in this project,
    /// tuning against derived numbers rather than against a vague feeling is the difference between
    /// a designed rhythm and a guessed one.
    /// </summary>
    private void LogDerivedStatistics()
    {
        float meanWait = (gapRange.x + gapRange.y) * 0.5f;
        float meanExtra = (longSilenceExtra.x + longSilenceExtra.y) * 0.5f;

        float meanGap = meanWait + longSilenceChance * meanExtra;

        // The skip branch multiplies the mean by the expected number of cycles per sound.
        if (skipChance > 0f && skipChance < 1f) meanGap /= (1f - skipChance);

        SO_AmbienceProfile profile = controller != null ? controller.ActiveProfile : null;
        float common   = profile != null ? profile.CommonWeight   : 0.6f;
        float uncommon = profile != null ? profile.UncommonWeight : 0.3f;
        float rare     = profile != null ? profile.RareWeight     : 0.1f;

        float total = common + uncommon + rare;
        if (total <= 0f) total = 1f;

        string TierLine(string name, float weight)
        {
            float share = weight / total;
            if (share <= 0f) return $"  {name,-9} never (weight 0)";
            float interval = meanGap / share;
            return $"  {name,-9} every {interval,6:F0}s   ({interval / 60f:F1} min)";
        }

        Debug.Log($"[{nameof(AmbienceEventScheduler)}] Derived timing for profile " +
                  $"'{(profile != null ? profile.DisplayName : "none")}':\n" +
                  $"  Mean gap between events: {meanGap:F1}s  ({60f / meanGap:F2} per minute)\n" +
                  TierLine("COMMON", common) + "\n" +
                  TierLine("UNCOMMON", uncommon) + "\n" +
                  TierLine("RARE", rare) + "\n" +
                  $"  Over a 20-minute session: about {1200f / meanGap:F0} events total, " +
                  $"{1200f / meanGap * (rare / total):F0} of them rare.\n" +
                  "  Note: at these rates a player may never hear one or two specific rare clips in " +
                  "a single run. That is a feature — they stay novel on a replay — but it is worth " +
                  "knowing before commissioning six rare recordings.", this);
    }

#if UNITY_EDITOR
    [ContextMenu("Fire One Event Now")]
    private void DebugFireNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning($"[{nameof(AmbienceEventScheduler)}] Only works in Play mode.", this);
            return;
        }

        if (!initialized || !IsReady())
        {
            Debug.LogWarning($"[{nameof(AmbienceEventScheduler)}] Not ready — needs a controller, " +
                             "a registered player and a Camera.main.", this);
            return;
        }

        TryFireEvent();
    }
#endif
}
