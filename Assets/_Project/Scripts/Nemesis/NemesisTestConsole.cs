using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// On-screen controls for driving the Nemesis by hand while testing. Added automatically by
/// <c>Tools/Nemesis/Build Nemesis Test Scene</c>; drop it on the Nemesis in any other scene to get
/// the same panel.
///
/// It is the other half of <c>NemesisDebugHUD</c>: that one answers "what is it doing and why",
/// this one answers "put it in the situation I want to look at". Most Nemesis behaviour only
/// appears after a minute of walking into position, and reproducing a specific case — losing you at
/// a doorway, refusing an unsafe spawn, giving up on the lift — is otherwise a matter of waiting
/// for it to happen again.
///
/// <b>Most of the panel changes the INPUTS the ladder reads</b> and lets the ladder reach a state
/// on its own, rather than putting the Nemesis into one. That is the more honest test — it
/// exercises the real path — and it is why the buttons are all situations rather than states.
///
/// <b>The one exception is pinning (keys 1-6), and where it hooks in is the whole reason it is
/// safe.</b> A state is requested by writing <c>NextState</c>, and <c>NemesisDecision</c> is the
/// only thing allowed to write it: a second writer makes the FSM transition every frame and never
/// execute a single frame of any state, which reads in game as a monster that looks straight at you
/// and twitches. Pinning does not write NextState — it overrides what the ladder ANSWERS, upstream
/// of the single writer, so the state is entered once and then runs completely normally. See
/// <c>NemesisDecision.Decide</c> and docs/CLAUDE.md § Nemesis: the decision layer.
///
/// What it cannot do is give a state a reason to exist: pinned Traversing with the player on your
/// own floor stands still, because no route crosses the lift. That is the state working.
///
/// <b>The body only compiles in the Editor and in development builds.</b> The class itself always
/// exists so a scene that references it does not come up with a missing script; in a release build
/// it is an empty MonoBehaviour with no Update and no OnGUI, so it costs nothing.
/// </summary>
public class NemesisTestConsole : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD

    [Tooltip("Key that shows and hides the panel. F9 is the debug HUD, so this sits next to it.")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F10;

    [Tooltip("Gap left between the two when either is warped onto the other. Kept off zero so the " +
             "warp lands beside its target rather than inside it, which would read as a capture " +
             "rather than a teleport.")]
    [SerializeField, Min(0.5f)] private float warpOffset = 3f;

    private NemesisStateManager nemesis;
    private bool isOpen;

    private void Awake() => nemesis = GetComponent<NemesisStateManager>();

    /// <summary>
    /// The states in the order the number keys pin them. Explicit and not
    /// <c>Enum.GetValues</c>: the enum is append-only for serialization reasons, so its order is a
    /// history of when things were added rather than a useful order to reach for while playing.
    /// </summary>
    private static readonly NemesisStateManager.ENemesisState[] PinOrder =
    {
        NemesisStateManager.ENemesisState.Patrolling,
        NemesisStateManager.ENemesisState.Investigating,
        NemesisStateManager.ENemesisState.Chasing,
        NemesisStateManager.ENemesisState.Searching,
        NemesisStateManager.ENemesisState.Traversing,
        NemesisStateManager.ENemesisState.Catch,
    };

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey)) isOpen = !isOpen;

        HandlePinKeys();
    }

    /// <summary>
    /// 1-6 pin a state, 0 hands it back to the ladder.
    ///
    /// Works with the panel closed, on purpose: pinning is something you want mid-chase, with both
    /// hands on the movement keys and no interest in reading a GUI.
    /// </summary>
    private void HandlePinKeys()
    {
        if (nemesis == null || nemesis.Decision == null) return;

        if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0))
        {
            nemesis.Decision.PinnedState = null;
            return;
        }

        for (int i = 0; i < PinOrder.Length; i++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha1 + i) && !Input.GetKeyDown(KeyCode.Keypad1 + i))
                continue;

            // Pressing the same number again releases it, so one key is both "show me this" and
            // "carry on", which is the only pair of actions this is ever used for.
            nemesis.Decision.PinnedState =
                nemesis.Decision.PinnedState == PinOrder[i] ? null : PinOrder[i];
            return;
        }
    }

    private void OnGUI()
    {
        if (!isOpen)
        {
            string pinned = nemesis != null && nemesis.Decision?.PinnedState != null
                ? $"   ESTADO FIJADO: {nemesis.Decision.PinnedState}  [0 suelta]"
                : string.Empty;

            GUI.Label(new Rect(10f, 10f, 520f, 20f),
                      $"[{toggleKey}] Nemesis test console{pinned}");
            return;
        }

        // Grown with the director section. Fixed rather than auto-sized because a panel that
        // resizes as zones come and go is harder to click than one that is simply big enough.
        GUILayout.BeginArea(new Rect(10f, 10f, 360f, 560f), GUI.skin.box);
        GUILayout.Label("NEMESIS TEST CONSOLE", GUI.skin.box);

        DrawStatus();
        GUILayout.Space(6f);
        DrawPinnedState();
        GUILayout.Space(6f);
        DrawSituations();
        GUILayout.Space(6f);
        DrawDirector();

        GUILayout.EndArea();
    }

    private void DrawStatus()
    {
        if (nemesis == null)
        {
            GUILayout.Label("No NemesisStateManager on this GameObject.");
            return;
        }

        PlayerStateManager player = PlayerRegistry.Current;

        GUILayout.Label($"Active:    {nemesis.IsActive}");
        GUILayout.Label($"State:     {(nemesis.CurrentStateKey.HasValue ? nemesis.CurrentStateKey.Value.ToString() : "-")}");
        GUILayout.Label($"Sees: {nemesis.HasVisualTarget}   Hears: {nemesis.HasAudioTarget}   Suspects: {nemesis.IsSuspicious}");
        GUILayout.Label($"Awareness: {nemesis.Awareness:0.00}");
        GUILayout.Label($"Hidden:    {(player != null ? player.IsHidden.ToString() : "-")}");

        // Straight-line, and labelled as such. NemesisNav measures over the NavMesh everywhere it
        // matters, and quoting a path distance here would imply this panel is a source of truth.
        GUILayout.Label(player != null
            ? $"Distance:  {Vector3.Distance(transform.position, player.transform.position):0.0} m (straight line)"
            : "Distance:  no player registered");

        // Not active while the scene is running means the spawn-in found nowhere safe and is
        // retrying — the single most confusing state to hit without being told.
        if (!nemesis.IsActive)
        {
            GUILayout.Label("Dormant. If a puzzle already activated it, it is waiting for a " +
                            "spawn point that is far, out of view and behind cover.");
        }
    }

    /// <summary>
    /// The pinned-state row.
    ///
    /// It is worth reading the caveat here rather than only in the code: what you get is the real
    /// state, entered for a reason you chose, which is not the same as the state having a reason to
    /// run. Pinned Traversing with the player on your own floor stands still — the route verdict
    /// says nothing crosses the lift, so there is nowhere to traverse to. That is the state working,
    /// not the pin failing.
    /// </summary>
    private void DrawPinnedState()
    {
        if (nemesis == null || nemesis.Decision == null) return;

        NemesisStateManager.ENemesisState? pinned = nemesis.Decision.PinnedState;

        GUILayout.Label(pinned.HasValue
            ? $"ESTADO FIJADO: {pinned.Value}  —  la escalera está en pausa"
            : "ESCALERA LIBRE  (1-6 fija un estado, 0 suelta)", GUI.skin.box);

        GUILayout.BeginHorizontal();

        for (int i = 0; i < PinOrder.Length; i++)
        {
            bool isPinned = pinned == PinOrder[i];
            string label = $"{i + 1}";

            // The number, and the state's initial under it, so the row is readable without having
            // to remember the order.
            if (GUILayout.Button(isPinned ? $"[{label}]" : label, GUILayout.Width(30f)))
                nemesis.Decision.PinnedState = isPinned ? (NemesisStateManager.ENemesisState?)null
                                                        : PinOrder[i];
        }

        if (GUILayout.Button("0", GUILayout.Width(30f))) nemesis.Decision.PinnedState = null;

        GUILayout.EndHorizontal();

        GUILayout.Label("1 Patrol · 2 Investig · 3 Chase · 4 Search · 5 Traverse · 6 Catch");
    }

    private void DrawSituations()
    {
        if (nemesis == null) return;

        PlayerStateManager player = PlayerRegistry.Current;
        if (player == null)
        {
            GUILayout.Label("No player registered — nothing to set up against.");
            return;
        }

        GUILayout.Label("SET UP A SITUATION", GUI.skin.box);

        // Through WarpTo and TeleportTo rather than assigning transform.position: those are the
        // entry points that snap onto the NavMesh, drop the cached route verdict, reset the stuck
        // watchdog and kill leftover momentum. Setting a transform directly leaves both sides
        // reasoning from where they used to be.
        if (GUILayout.Button("Nemesis behind the player"))
            nemesis.WarpTo(player.transform.position - player.transform.forward * warpOffset);

        if (GUILayout.Button("Nemesis in front of the player  (starts a chase)"))
            nemesis.WarpTo(player.transform.position + player.transform.forward * warpOffset);

        if (GUILayout.Button("Player onto the Nemesis"))
            player.TeleportTo(transform.position - transform.forward * warpOffset,
                              player.transform.rotation);

        GUILayout.Space(4f);

        if (GUILayout.Button(player.IsHidden ? "Leave hiding" : "Hide  (blinds its vision)"))
            player.IsHidden = !player.IsHidden;

        // The capture path proper: PlayerStateManager.OnCaptured is what the Nemesis calls, and it
        // is what CheckpointManager listens to. Setting IsDisabled by hand would freeze the player
        // without any of that, which is a different thing that looks the same for one frame.
        if (GUILayout.Button("Capture the player"))
            player.OnCaptured();
    }

    /// <summary>
    /// Drives <see cref="NemesisDirector"/> by hand.
    ///
    /// It belongs on this panel and not on a separate one for the reason in the class doc: the
    /// Director is an input-changer by construction — it moves the anchor, the weights, a noise —
    /// and never writes a state. Pressing these buttons exercises exactly the path a puzzle
    /// completion would, so what you see here is what will happen in the level, which is not true
    /// of any "force state" button.
    ///
    /// One button per zone rather than a text field: an id typed slightly wrong fails silently
    /// from the outside (the Director warns, but you have to be looking at the console), and the
    /// list doubles as a check that the zones in the scene registered themselves at all.
    /// </summary>
    private void DrawDirector()
    {
        GUILayout.Label("DIRECTOR", GUI.skin.box);

        IReadOnlyList<NemesisPressureZone> zones = NemesisPressureZone.Active;

        if (zones.Count == 0)
        {
            GUILayout.Label("No pressure zones in the scene. Add a NemesisPressureZone with an id.");
            return;
        }

        for (int i = 0; i < zones.Count; i++)
        {
            NemesisPressureZone zone = zones[i];
            if (zone == null) continue;

            float live = NemesisDirector.IntensityOf(zone.ZoneId);
            string label = live > 0f
                ? $"■ {zone.ZoneId}  ({live:0.00})"
                : $"Pressure: {zone.ZoneId}";

            if (GUILayout.Button(label))
                NemesisDirector.RequestPressure(zone.ZoneId, TestPressureIntensity, TestPressureDuration);
        }

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Release"))
            NemesisDirector.ReleasePressure();

        // With no zone: anywhere near the player that qualifies. The zone-confined version is what
        // a puzzle trigger uses; this one is the quickest way to look at the entrance itself.
        if (GUILayout.Button("Staged entrance"))
            NemesisDirector.RequestEntrance();

        GUILayout.EndHorizontal();
    }

    /// <summary>Full pressure from the console, on purpose: a test that has to be run at half
    /// strength to see anything is a test of the tuning, not of the feature.</summary>
    private const float TestPressureIntensity = 1f;

    /// <summary>Long enough to walk somewhere and watch the patrol drift, short enough that a
    /// forgotten press does not colour the rest of the session.</summary>
    private const float TestPressureDuration = 90f;

#endif
}
