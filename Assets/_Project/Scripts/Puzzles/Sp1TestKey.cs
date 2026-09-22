#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// F3 completes SP1 (the electrical panel) in Play Mode, so the SP2 light switches have power
/// without solving the sequence first — the short way to look at the lamps, their beams and their
/// pools in the fog. F3 is free: F4, F6, F8, F9 and F10 are taken by the escape sequence, the skill
/// check, the module explosion, the Nemesis debug HUD and the Nemesis test console.
///
/// It does exactly what the panel does when it is solved — <see cref="PuzzleStateManager"/>'s
/// SetPuzzleCompleted — so everything gated behind SP1 reacts the same way. It does not hand over
/// the panel's reward item, and the panel itself still shows as unsolved.
///
/// Editor only, and with no setup at all: the whole file is compiled out of a build, and the object
/// that polls the key builds itself on the first frame, so it needs no place in any scene.
/// </summary>
public class Sp1TestKey : MonoBehaviour
{
    /// <summary>Id of the SP1 sequence, from SO_Sequence_SP1_PanelElectrico.</summary>
    private const string Sp1PuzzleId = "sp1_panel_electrico";

    private const KeyCode Key = KeyCode.F3;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var host = new GameObject("SP1 Test Key (F3, editor only)") { hideFlags = HideFlags.DontSave };
        host.AddComponent<Sp1TestKey>();
        DontDestroyOnLoad(host);
    }

    // Legacy input, like the other debug keys: the project runs both input backends.
    private void Update()
    {
        if (!Input.GetKeyDown(Key)) return;

        // Same guard as the other test keys: a key pressed under a menu belongs to the menu.
        if (PauseManager.IsGameplayInputBlocked) return;

        if (!PuzzleStateManager.Exists)
        {
            Debug.LogWarning($"[{nameof(Sp1TestKey)}] No PuzzleStateManager yet — press F3 once the " +
                             "game is running, it lives in the Data scene.");
            return;
        }

        if (PuzzleStateManager.Instance.IsPuzzleCompleted(Sp1PuzzleId))
        {
            Debug.Log($"[{nameof(Sp1TestKey)}] SP1 was already completed: the switches have power.");
            return;
        }

        PuzzleStateManager.Instance.SetPuzzleCompleted(Sp1PuzzleId);
        Debug.Log($"[{nameof(Sp1TestKey)}] SP1 completed. The SP2 light switches have power now — " +
                  "walk up to one and turn the lamps on.");
    }
}
#endif
