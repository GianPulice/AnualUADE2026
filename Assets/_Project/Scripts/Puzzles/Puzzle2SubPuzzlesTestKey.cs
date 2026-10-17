#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// F11 completes the three sub-puzzles of Central Puzzle 2 (boilers, switches, mechanism) in Play
/// Mode, so the Ventilation Hub's stabilization panel opens without solving them first — the short
/// way to test the stabilization checks. F11 is free: F2 to F10 are taken by the other debug keys.
///
/// The ids are not written here: they are the RequiredPuzzleIds of the SO_StabilizationPuzzleData
/// on every <see cref="StabilizationPanelInteractable"/> in the loaded scenes, so the key follows
/// whatever the hub asks for. It does exactly what each sub-puzzle does when solved —
/// <see cref="PuzzleStateManager"/>'s SetPuzzleCompleted — so the monitor and the panel react the
/// same way. It does not hand over rewards, and the sub-puzzles' own objects (levers, switches)
/// stay where they were.
///
/// Editor and Development Build only, and with no setup at all: the whole file is compiled out of a
/// release build, and the object that polls the key builds itself on the first frame, like
/// <see cref="Sp1TestKey"/>.
/// </summary>
public class Puzzle2SubPuzzlesTestKey : MonoBehaviour
{
    private const KeyCode Key = KeyCode.F11;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var host = new GameObject("Puzzle 2 Sub-Puzzles Test Key (F11, editor + dev)") { hideFlags = HideFlags.DontSave };
        host.AddComponent<Puzzle2SubPuzzlesTestKey>();
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
            Debug.LogWarning($"[{nameof(Puzzle2SubPuzzlesTestKey)}] No PuzzleStateManager yet — press F11 " +
                             "once the game is running, it lives in the Data scene.");
            return;
        }

        StabilizationPanelInteractable[] panels = FindObjectsByType<StabilizationPanelInteractable>(FindObjectsInactive.Include);
        if (panels.Length == 0)
        {
            Debug.LogWarning($"[{nameof(Puzzle2SubPuzzlesTestKey)}] F11: no {nameof(StabilizationPanelInteractable)} " +
                             "in the loaded scenes — load the level with the Ventilation Hub first.");
            return;
        }

        int completed = 0;
        foreach (StabilizationPanelInteractable panel in panels)
        {
            if (panel.PuzzleData == null) continue;

            foreach (string id in panel.PuzzleData.RequiredPuzzleIds)
            {
                if (string.IsNullOrWhiteSpace(id) || PuzzleStateManager.Instance.IsPuzzleCompleted(id)) continue;

                PuzzleStateManager.Instance.SetPuzzleCompleted(id);
                completed++;
            }
        }

        Debug.Log(completed > 0
            ? $"[{nameof(Puzzle2SubPuzzlesTestKey)}] F11: {completed} sub-puzzle(s) completed. The stabilization " +
              "panel can be used now."
            : $"[{nameof(Puzzle2SubPuzzlesTestKey)}] F11: every sub-puzzle was already completed.");
    }
}
#endif
