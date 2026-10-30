#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// F3 completes SP1 (the electrical panel) in Play Mode, so the SP2 light switches have power
/// without solving the sequence first — the short way to look at the lamps, their beams and their
/// pools in the fog. F3 is free: F4, F6, F8, F9 and F10 are taken by the escape sequence, the
/// teleport to zone 2, the module explosion, the Nemesis debug HUD and the Nemesis test console.
///
/// It does exactly what the panel does when it is solved — <see cref="PuzzleStateManager"/>'s
/// SetPuzzleCompleted — so everything gated behind SP1 reacts the same way. It does not hand over
/// the panel's reward item, and the panel itself still shows as unsolved.
///
/// IN ZONA 2 IT ALSO COMPLETES ITS SP1, the boilers' lever sequence (<c>p2_sp1_calderas</c>), which is one
/// of the three sub-puzzles that wake the Nemesis there (<see cref="NemesisZoneWake"/>): F6 to get to Zona 2,
/// F3 to have it awake and patrolling, without playing the puzzle. "In Zona 2" is the player standing past
/// the entry point F6 takes them to (<see cref="Zone2EntryTeleport"/>).
///
/// Editor and Development Build only, and with no setup at all: the whole file is compiled out of a release build, and the object
/// that polls the key builds itself on the first frame, so it needs no place in any scene.
/// </summary>
public class Sp1TestKey : MonoBehaviour
{
    /// <summary>Id of the SP1 sequence, from SO_Sequence_SP1_PanelElectrico.</summary>
    private const string Sp1PuzzleId = "sp1_panel_electrico";

    /// <summary>Id of Zona 2's SP1 (the boilers), from SO_LeverSequence_SP1_Calderas.</summary>
    private const string Zone2Sp1PuzzleId = "p2_sp1_calderas";

    private const KeyCode Key = KeyCode.F3;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var host = new GameObject("SP1 Test Key (F3, editor + dev)") { hideFlags = HideFlags.DontSave };
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
        }
        else
        {
            PuzzleStateManager.Instance.SetPuzzleCompleted(Sp1PuzzleId);
            Debug.Log($"[{nameof(Sp1TestKey)}] SP1 completed. The SP2 light switches have power now — " +
                      "walk up to one and turn the lamps on.");
        }

        CompleteZone2Sp1();
    }

    /// <summary>In Zona 2, completes the boilers too, which wakes the Nemesis there. Does nothing
    /// elsewhere, or when it is already done.</summary>
    private static void CompleteZone2Sp1()
    {
        if (!IsInZone2()) return;

        if (PuzzleStateManager.Instance.IsPuzzleCompleted(Zone2Sp1PuzzleId))
        {
            Debug.Log($"[{nameof(Sp1TestKey)}] Zona 2's SP1 (boilers) was already completed.");
            return;
        }

        PuzzleStateManager.Instance.SetPuzzleCompleted(Zone2Sp1PuzzleId);
        Debug.Log($"[{nameof(Sp1TestKey)}] Zona 2's SP1 (boilers) completed: the Nemesis wakes up there.");
    }

    /// <summary>The player is in Zona 2: at or past the entry point of F6, which sits at its start. A
    /// level without that component has no Zona 2 to be in.</summary>
    private static bool IsInZone2()
    {
        Transform player = PlayerRegistry.CurrentTransform;
        if (player == null) return false;

        Zone2EntryTeleport entry = FindAnyObjectByType<Zone2EntryTeleport>();
        return entry != null && player.position.x >= entry.Target.position.x - 1f;
    }
}
#endif
