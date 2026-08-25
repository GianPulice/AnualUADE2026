using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Coordinator for the factory ambience. Owns the profile stack, resolves the mixer routing once,
/// and hands it to each layer.
///
/// Lives in the GAMEPLAY scene, not in Data — the same choice VisionRangeController makes, and for
/// the same reason: the ambience belongs to the level and should die with it. Nothing here needs to
/// survive a scene unload.
///
/// PROFILE STACK
/// AmbienceZone triggers push a profile on enter and pop it on exit. The innermost zone wins, so
/// nesting works with no extra wiring: a small room inside a large hall applies the room while the
/// player is in it and falls back to the hall on the way out. An out-of-order pop — leaving the
/// outer zone while still inside the inner one — removes the entry without disturbing what is
/// currently audible. This is the same stack contract as VisionRangeController.PushConfig /
/// PopConfig.
///
/// INITIALIZATION IS PUSHED, NOT PULLED
/// This component's Start resolves the buses and then explicitly calls Initialize on each layer.
/// The layers do nothing in their own Start. That removes all cross-component ordering ambiguity
/// without needing a [DefaultExecutionOrder] attribute, and it is why the mixer lookup can safely
/// happen in Start: AudioManager builds its groups in its own Awake, which has already run by then
/// because it lives in the persistent Data scene.
///
/// Designer setup:
///   1. Empty GameObject in the gameplay scene, named AmbienceController.
///   2. Add this component — the layer components arrive automatically via [RequireComponent].
///   3. Drag the four Ambience sub-groups from MasterMixer.mixer into the Buses table. If you skip
///      this the system still works, routed flat to the Ambience bus, but the balance between the
///      layers will be wrong.
///   4. Assign defaultProfile — the ambience for anywhere not covered by an AmbienceZone.
///
/// FadeOutAll/FadeInAll now have a caller: NemesisChaseMusic ducks this layer for its chase-music
/// takeover, driven by NemesisEvents.OnChaseStarted/OnChaseEnded.
///
/// KNOWN GAP: the continuous proximity-reactive layer is still not wired. The signal it needs is
/// already public as NemesisEvents.OnProximityChanged, and the hook it would drive is
/// SetTensionScalars below, which currently has no caller.
/// </summary>
[RequireComponent(typeof(AmbienceBedLayer), typeof(AmbienceDriftLayer), typeof(AmbienceEventScheduler))]
[RequireComponent(typeof(AmbienceEventPool), typeof(AmbiencePlacementResolver))]
public class AmbienceController : MonoBehaviour
{
    [Header("Mixer routing")]
    [Tooltip("The four Ambience sub-groups. Leave empty to route everything flat to AudioManager's " +
             "Ambience bus (audible, but without the per-layer balance).")]
    [SerializeField] private AmbienceBusTable buses = new AmbienceBusTable();

    [Header("Profiles")]
    [Tooltip("Ambience for anywhere not covered by an AmbienceZone trigger. This is the bottom of " +
             "the stack and is never popped.")]
    [SerializeField] private SO_AmbienceProfile defaultProfile;

    [Header("Debug")]
    [Tooltip("Logs every profile push and pop. Useful while placing zone triggers.")]
    [SerializeField] private bool logProfileChanges = true;

    private AmbienceBedLayer bedLayer;
    private AmbienceDriftLayer driftLayer;
    private AmbienceEventScheduler eventScheduler;
    private AmbienceEventPool eventPool;
    private AmbiencePlacementResolver placementResolver;

    private readonly List<SO_AmbienceProfile> profileStack = new List<SO_AmbienceProfile>();

    // False until Start has validated and initialized every layer.
    //
    // A zone trigger can fire before that — a player spawning already inside a zone is the obvious
    // case — so pushes and pops are always recorded on the stack, and only the APPLICATION is
    // deferred. Start ends with ApplyActiveProfile, which picks up whatever accumulated. Dropping
    // the push instead would leave the zone permanently unapplied, because AmbienceZone latches
    // isPlayerInside and never re-enters.
    private bool isOperational;

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// The profile currently driving the layers: the top of the zone stack, or the default profile
    /// when the player is not inside any zone. May be null if no default was assigned.
    /// </summary>
    public SO_AmbienceProfile ActiveProfile =>
        profileStack.Count > 0 ? profileStack[profileStack.Count - 1] : defaultProfile;

    /// <summary>Applies <paramref name="profile"/> as the innermost zone. Called by AmbienceZone on enter.</summary>
    public void PushProfile(SO_AmbienceProfile profile)
    {
        if (profile == null) return;

        profileStack.Add(profile);

        if (logProfileChanges)
            Debug.Log($"[{nameof(AmbienceController)}] Push '{profile.DisplayName}' " +
                      $"(stack depth {profileStack.Count}).", this);

        if (isOperational) ApplyActiveProfile();
    }

    /// <summary>
    /// Removes <paramref name="profile"/> from the stack. Called by AmbienceZone on exit.
    ///
    /// Only re-applies when the removed entry was the top one. Popping something from the middle of
    /// the stack means the player left an outer zone while still standing in an inner one, and in
    /// that case what they hear must not change.
    /// </summary>
    public void PopProfile(SO_AmbienceProfile profile)
    {
        if (profile == null || profileStack.Count == 0) return;

        // LastIndexOf, not IndexOf: the same profile asset can legitimately appear twice in the
        // stack when two zones that share it overlap, and the innermost occurrence is the one this
        // exit corresponds to.
        int index = profileStack.LastIndexOf(profile);
        if (index < 0) return;

        bool wasTop = index == profileStack.Count - 1;
        profileStack.RemoveAt(index);

        if (logProfileChanges)
            Debug.Log($"[{nameof(AmbienceController)}] Pop '{profile.DisplayName}' " +
                      $"(stack depth {profileStack.Count}, wasTop={wasTop}).", this);

        if (wasTop && isOperational) ApplyActiveProfile();
    }

    /// <summary>
    /// Re-pushes the active profile's values into every layer.
    ///
    /// Exposed rather than done every frame. VisionRangeController re-reads its config in LateUpdate
    /// so a designer can tweak the asset live, but re-applying an ambience profile per frame would
    /// fight the bed layer's same-clip guard and gain nothing, so live tweaking is opt-in through
    /// this method and the context menu below.
    /// </summary>
    [ContextMenu("Reapply Active Profile")]
    public void ReapplyActiveProfile() => ApplyActiveProfile();

    /// <summary>
    /// Seam for the future enemy-proximity layer. Scales the bed level; the event rate and sub
    /// scalars are accepted now so the eventual AmbienceTensionDriver can be a single new file with
    /// no edits here. All three default to 1 and nothing calls this yet.
    /// </summary>
    public void SetTensionScalars(float bedScale, float eventRateScale, float subScale)
    {
        if (bedLayer != null) bedLayer.SetVolumeScale(bedScale);
        if (driftLayer != null) driftLayer.SetSubScale(subScale);
        if (eventScheduler != null) eventScheduler.SetIntervalScale(eventRateScale);
    }

    /// <summary>Fades every ambient layer out, e.g. for a chase-music takeover.</summary>
    public void FadeOutAll(float seconds)
    {
        if (bedLayer != null) bedLayer.FadeOut(seconds);
        if (driftLayer != null) driftLayer.FadeOut(seconds);
        if (eventPool != null) eventPool.StopAll();
    }

    /// <summary>Fades every ambient layer back in after a <see cref="FadeOutAll"/>.</summary>
    public void FadeInAll(float seconds)
    {
        if (bedLayer != null) bedLayer.FadeIn(seconds);
        if (driftLayer != null) driftLayer.FadeIn(seconds);
    }

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        ResolveHierarchyReferences();

        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        ResolveBusRouting();

        // Push, don't pull: the layers do nothing in their own Start, so the order in which they
        // come up is decided here rather than by Unity's component ordering.
        bedLayer.Initialize(buses);
        driftLayer.Initialize(buses, bedLayer);
        eventPool.Initialize(buses);
        eventScheduler.Initialize(this, placementResolver, eventPool);

        isOperational = true;

        ApplyActiveProfile();
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private void ResolveHierarchyReferences()
    {
        // Explicit == null rather than ??=, because Unity overloads the null operator on
        // UnityEngine.Object: a destroyed component would pass a ??= check and be kept.
        if (bedLayer == null) bedLayer = GetComponent<AmbienceBedLayer>();
        if (driftLayer == null) driftLayer = GetComponent<AmbienceDriftLayer>();
        if (eventScheduler == null) eventScheduler = GetComponent<AmbienceEventScheduler>();
        if (eventPool == null) eventPool = GetComponent<AmbienceEventPool>();
        if (placementResolver == null) placementResolver = GetComponent<AmbiencePlacementResolver>();
    }

    /// <summary>
    /// Collects every missing reference into a single LogError rather than failing one at a time,
    /// following NemesisStateManager. Returns false if the component cannot run.
    /// </summary>
    private bool ValidateReferences()
    {
        string missing = "";

        if (bedLayer == null) missing += $"\n  - {nameof(AmbienceBedLayer)} component";
        if (driftLayer == null) missing += $"\n  - {nameof(AmbienceDriftLayer)} component";
        if (eventScheduler == null) missing += $"\n  - {nameof(AmbienceEventScheduler)} component";
        if (eventPool == null) missing += $"\n  - {nameof(AmbienceEventPool)} component";
        if (placementResolver == null) missing += $"\n  - {nameof(AmbiencePlacementResolver)} component";
        if (defaultProfile == null) missing += "\n  - defaultProfile (SO_AmbienceProfile)";

        if (missing.Length == 0) return true;

        Debug.LogError($"[{nameof(AmbienceController)}] Disabled — missing references:{missing}", this);
        return false;
    }

    private void ResolveBusRouting()
    {
        AudioMixerGroup ambienceBus = AudioManager.Exists ? AudioManager.Instance.AmbienceGroup : null;

        buses.SetFallback(ambienceBus);

        if (buses.IsEmpty)
        {
            Debug.LogWarning($"[{nameof(AmbienceController)}] No Ambience sub-groups assigned. " +
                             "Every layer is routed flat to the Ambience bus, so the balance " +
                             "between them is wrong and the Sub bus has no band-limiting. Run " +
                             "Tools/Audio/Create or Update Master Mixer, then drag Bed, Events, " +
                             "Texture and Sub into the Buses table.", this);
        }
        else if (!buses.IsFullyAssigned)
        {
            Debug.LogWarning($"[{nameof(AmbienceController)}] Some Ambience sub-groups are " +
                             "assigned and some are not. The unassigned ones fall back to the " +
                             "Ambience bus at 0 dB, which will be louder than intended.", this);
        }

        if (ambienceBus == null && !buses.IsFullyAssigned)
        {
            Debug.LogWarning($"[{nameof(AmbienceController)}] AudioManager was not found, so " +
                             "there is no fallback bus either. The ambience will be audible but " +
                             "completely unmixed — it bypasses the AudioMixer and the player's " +
                             "volume sliders. Press Play from the Bootstrap scene so the " +
                             "persistent Data scene loads.", this);
        }
    }

    private void ApplyActiveProfile()
    {
        SO_AmbienceProfile profile = ActiveProfile;

        if (bedLayer != null) bedLayer.ApplyProfile(profile);
        if (driftLayer != null) driftLayer.ApplyProfile(profile);

        // The scheduler reads the profile itself when it fires, so it needs no ApplyProfile call —
        // a zone change takes effect on the next event rather than resetting the timer, which is the
        // right behaviour: crossing a doorway should not trigger a clang.
        AmbienceEvents.RaiseProfileChanged(profile);
    }
}
