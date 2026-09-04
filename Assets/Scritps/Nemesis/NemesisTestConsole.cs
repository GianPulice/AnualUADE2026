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
/// <b>There is deliberately no "force state" button.</b> A state is requested by writing
/// <c>NextState</c>, and <c>NemesisDecision</c> is the only thing allowed to write it — a second
/// writer makes the FSM transition every frame and never execute a single frame of any state, which
/// reads in game as a monster that looks straight at you and twitches. See docs/CLAUDE.md § Nemesis:
/// the decision layer. So this panel changes the INPUTS the ladder reads instead, and lets the
/// ladder reach the state on its own. That is also the more honest test: it exercises the real
/// path.
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

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey)) isOpen = !isOpen;
    }

    private void OnGUI()
    {
        if (!isOpen)
        {
            GUI.Label(new Rect(10f, 10f, 300f, 20f), $"[{toggleKey}] Nemesis test console");
            return;
        }

        GUILayout.BeginArea(new Rect(10f, 10f, 330f, 340f), GUI.skin.box);
        GUILayout.Label("NEMESIS TEST CONSOLE", GUI.skin.box);

        DrawStatus();
        GUILayout.Space(6f);
        DrawSituations();

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

#endif
}
