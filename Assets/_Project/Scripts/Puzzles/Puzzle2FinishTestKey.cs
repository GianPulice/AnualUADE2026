#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// F12 finishes Zona 2 in Play Mode: every id the Ventilation Hub's stabilization waits for (the
/// three installs) and then the stabilization itself (<c>puzzle_central_piso2</c>), so the exit's
/// gate opens and its gauge turns green without solving or carrying anything. With F1
/// (<c>DevLevelKeys</c>, Zona 2's hub) it is the short way to test the exit.
///
/// The ids are not written here: they are the RequiredPuzzleIds and PuzzleId of the
/// SO_StabilizationPuzzleData on every <see cref="StabilizationPanelInteractable"/> in the loaded
/// scenes. It does exactly what the hub and the panel do when they complete —
/// <see cref="PuzzleStateManager"/>'s SetPuzzleCompleted — so every listener (gate, gauge, monitor,
/// module) reacts as in a real run. It does not hand over rewards, solve the sub-puzzles' own panels
/// (their "solved" ids stay pending, so the Nemesis wake that keys on them does not fire), or move the
/// player. F11 (<see cref="Puzzle2SubPuzzlesTestKey"/>) is the half of this that leaves the
/// stabilization to be played.
///
/// Editor and Development Build only, with no setup: the object that polls the key builds itself on
/// the first frame, like <see cref="Sp1TestKey"/>.
/// </summary>
public class Puzzle2FinishTestKey : MonoBehaviour
{
    private const KeyCode Key = KeyCode.F12;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var host = new GameObject("Puzzle 2 Finish Test Key (F12, editor + dev)") { hideFlags = HideFlags.DontSave };
        host.AddComponent<Puzzle2FinishTestKey>();
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
            Debug.LogWarning($"[{nameof(Puzzle2FinishTestKey)}] No PuzzleStateManager yet — press F12 " +
                             "once the game is running, it lives in the Data scene.");
            return;
        }

        StabilizationPanelInteractable[] panels = FindObjectsByType<StabilizationPanelInteractable>(FindObjectsInactive.Include);
        if (panels.Length == 0)
        {
            Debug.LogWarning($"[{nameof(Puzzle2FinishTestKey)}] F12: no {nameof(StabilizationPanelInteractable)} " +
                             "in the loaded scenes — load the level with the Ventilation Hub first.");
            return;
        }

        int completed = 0;
        foreach (StabilizationPanelInteractable panel in panels)
        {
            SO_StabilizationPuzzleData data = panel.PuzzleData;
            if (data == null) continue;

            // The installs first: the stabilization is only ever completed after them.
            foreach (string id in data.RequiredPuzzleIds) completed += Complete(id);
            completed += Complete(data.PuzzleId);
        }

        Debug.Log(completed > 0
            ? $"[{nameof(Puzzle2FinishTestKey)}] F12: {completed} puzzle(s) completed. Zona 2 is stabilized: " +
              "the exit gate is opening."
            : $"[{nameof(Puzzle2FinishTestKey)}] F12: Zona 2 was already stabilized.");
    }

    private static int Complete(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || PuzzleStateManager.Instance.IsPuzzleCompleted(id)) return 0;

        PuzzleStateManager.Instance.SetPuzzleCompleted(id);
        return 1;
    }
}
#endif
