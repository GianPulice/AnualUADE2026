#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Three debug keys for walking around the blockout with the Nemesis, plus the F10 console there.
///
/// <b>F5 — wake the Nemesis.</b> In WIRED_Zona1_Blockout it is <c>wakeOnlyFromScript</c>: it sleeps
/// until the escape cinematic, so out of the box there is nothing in the level to test against.
/// F5 calls <see cref="NemesisStateManager.Activate"/> — the same entry a puzzle uses, spawn-point
/// search included, so it appears where the level says it may and not on top of you. Idempotent:
/// pressing it again once it is awake does nothing. When there is nowhere safe to appear yet
/// (every spawn point too close or in view) it keeps retrying by itself, exactly as it would for a
/// puzzle. Editor and development builds.
///
/// <b>F2 — toggle the test zone.</b> The blockout has no test area of its own, so the "zone" is
/// the NemesisTestbed group. One press swaps to it, the next swaps back to the level — through the
/// <see cref="ScreenManager"/> like any scene change, so the loading screen and the input lock
/// apply. It is a scene swap, not a teleport: coming back reloads the level, so the wake-up
/// cinematic plays again (F skips it) and the level restarts from the top. In a release build
/// it warns and does nothing: <c>DevScenesBuildFilter</c> strips the Dev scenes from every build
/// except NemesisTestbed in a Development Build, so only there is there somewhere to go.
///
/// <b>F6 — go to Zona 2.</b> Puts the player at Zona 2's entry, any time in Play, through
/// <see cref="Zone2EntryTeleport.TeleportNow"/> — the same path the escape's end takes, so what F6
/// shows is what the real handover does. Needs that component in the loaded scenes (in Zona1, on
/// <c>Zona_2_Spawnpoint</c>); warns when there is none. Ignored while a cinematic owns the screen
/// (<see cref="CinematicState.IsPlaying"/>), and refused by the component itself while the player is
/// hidden or being captured. It only moves the player: nothing else is skipped or solved.
///
/// <b>F10 console in the blockout.</b> <c>NemesisTestConsole</c> is added to scenes by hand and the
/// blockout's Nemesis never had one. Rather than edit the scene, every Nemesis that turns up
/// without a console gets one here. Editor and development builds, like the console itself.
///
/// F2, F5 and F6 are this component's: the game itself uses W A S D, E, F, Tab, Esc, Ctrl, Shift,
/// Space, 1, 2, and the other debug keys are F3, F4, F7, F8, F9, F10 and 0-6 (see the list in
/// <c>docs/CLAUDE.md</c>).
///
/// No setup: the object that polls the keys builds itself on the first frame, like
/// <c>Sp1TestKey</c>. Ignored under a menu or during a scene change, like every gameplay key.
/// </summary>
public class DevLevelKeys : MonoBehaviour
{
    private const KeyCode WakeNemesisKey = KeyCode.F5;
    private const KeyCode TestZoneKey = KeyCode.F2;
    private const KeyCode Zone2Key = KeyCode.F6;

    /// <summary>The real level: Zona1 + LevelUI. The name is misleading, see <c>build-no-test-content</c>.</summary>
    private const string LevelGroup = "TestBlocking";

    /// <summary>NemesisTestbed + LevelUI.</summary>
    private const string TestZoneGroup = "TestNemesis";

    private const string TestZoneScene = "NemesisTestbed";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var host = new GameObject("Dev Level Keys (F2 / F5 / F6)") { hideFlags = HideFlags.DontSave };
        host.AddComponent<DevLevelKeys>();
        DontDestroyOnLoad(host);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        AttachConsoles();
    }

    private void OnDisable() => SceneManager.sceneLoaded -= HandleSceneLoaded;

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => AttachConsoles();

    // Legacy input, like the other debug keys: the project runs both input backends.
    private void Update()
    {
        if (PauseManager.IsGameplayInputBlocked) return;

        if (Input.GetKeyDown(WakeNemesisKey)) WakeNemesis();
        if (Input.GetKeyDown(TestZoneKey)) ToggleTestZone();

        // Not under a cinematic: it owns the player and the cameras, and would cut back to a body
        // that is no longer where its shots were framed.
        if (Input.GetKeyDown(Zone2Key) && !CinematicState.IsPlaying) GoToZone2();
    }

    /// <summary>Through the component's own teleport, never a copy of it: F6 must show what the
    /// escape's handover does.</summary>
    private static void GoToZone2()
    {
        Zone2EntryTeleport entry = FindAnyObjectByType<Zone2EntryTeleport>();
        if (entry == null)
        {
            Debug.LogWarning($"[{nameof(DevLevelKeys)}] F6: no {nameof(Zone2EntryTeleport)} in the " +
                             "loaded scenes (in Zona1 it goes on Zona_2_Spawnpoint).");
            return;
        }

        if (entry.TeleportNow())
            Debug.Log($"[{nameof(DevLevelKeys)}] F6: player moved to '{entry.Target.name}'.");
    }

    /// <summary>
    /// Gives every Nemesis in the loaded scenes an F10 console it does not already have. Runs after
    /// every scene load, because the level arrives additively long after this object exists.
    /// </summary>
    private static void AttachConsoles()
    {
        foreach (NemesisStateManager nemesis in FindObjectsByType<NemesisStateManager>(FindObjectsInactive.Include))
        {
            if (nemesis.GetComponent<NemesisTestConsole>() == null)
                nemesis.gameObject.AddComponent<NemesisTestConsole>();
        }
    }

    private static void WakeNemesis()
    {
        NemesisStateManager[] all = FindObjectsByType<NemesisStateManager>();

        if (all.Length == 0)
        {
            Debug.LogWarning($"[{nameof(DevLevelKeys)}] F5: no Nemesis in the loaded scenes.");
            return;
        }

        foreach (NemesisStateManager nemesis in all)
        {
            if (nemesis.IsActive)
            {
                Debug.Log($"[{nameof(DevLevelKeys)}] F5: '{nemesis.name}' is already awake.");
                continue;
            }

            nemesis.Activate();

            // Activate() puts it back to sleep when every spawn point is too close or in view; it
            // then retries on its own, so this is a "not yet" and worth saying so.
            Debug.Log(nemesis.IsActive
                ? $"[{nameof(DevLevelKeys)}] F5: '{nemesis.name}' awake."
                : $"[{nameof(DevLevelKeys)}] F5: '{nemesis.name}' has nowhere safe to appear yet " +
                  "(too close to you or in view). It keeps trying: walk on or turn away.");
        }
    }

    private static void ToggleTestZone()
    {
        if (!ScreenManager.Exists) return;

        ScreenManager screens = ScreenManager.Instance;
        if (screens.IsTransitioning) return;

        string current = screens.CurrentGroupLabel;
        if (current != LevelGroup && current != TestZoneGroup)
        {
            Debug.LogWarning($"[{nameof(DevLevelKeys)}] F2: the active group is '{current}'. " +
                             $"It only toggles between '{LevelGroup}' and '{TestZoneGroup}'.");
            return;
        }

        // The channel is a ScriptableObject the ScreenManager already holds, so it is loaded.
        ScreenEventChannel[] channels = Resources.FindObjectsOfTypeAll<ScreenEventChannel>();
        if (channels.Length == 0)
        {
            Debug.LogWarning($"[{nameof(DevLevelKeys)}] F2: no ScreenEventChannel is loaded.");
            return;
        }

        string target = current == TestZoneGroup ? LevelGroup : TestZoneGroup;

        // Dev scenes are stripped from every build (DevScenesBuildFilter). Swapping anyway would
        // unload the level and then fail to load the zone, leaving nothing on screen.
        if (target == TestZoneGroup && !Application.CanStreamedLevelBeLoaded(TestZoneScene))
        {
            Debug.LogWarning($"[{nameof(DevLevelKeys)}] F2: '{TestZoneScene}' is not in this build " +
                             "(only Development Builds carry it).");
            return;
        }

        Debug.Log($"[{nameof(DevLevelKeys)}] F2: '{current}' -> '{target}'.");
        channels[0].RaisePushScreen(target);
    }
}
#endif
