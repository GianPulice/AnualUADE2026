using System.Collections;
using UnityEngine;

/// <summary>
/// The switch row of the switch room (Central Puzzle 2 — SP2). Its <see cref="SwitchInteractable"/>
/// children are switches 0, 1, 2… in hierarchy order; the ones past the asset's switch count are
/// hidden.
///
/// Positions live in <see cref="PuzzleStateManager"/> (as 0/1 valve positions, see
/// <see cref="SO_SwitchPuzzleData.SwitchStateKey"/>), so a checkpoint rollback puts the switches
/// back where they were. Checked on every flip, as the spec asks: the moment the last switch lands
/// right, the puzzle completes and the row locks.
/// </summary>
public class SwitchPanelController : MonoBehaviour
{
    [SerializeField] private SO_SwitchPuzzleData puzzleData;

    private SwitchInteractable[] switches = new SwitchInteractable[0];

    public SO_SwitchPuzzleData PuzzleData => puzzleData;

    public bool IsCompleted =>
        puzzleData != null && PuzzleStateManager.Exists &&
        PuzzleStateManager.Instance.IsPuzzleCompleted(puzzleData.PuzzleId);

    private void Awake()
    {
        if (puzzleData == null)
        {
            Debug.LogError($"[{nameof(SwitchPanelController)}] No SO_SwitchPuzzleData on '{name}'.", this);
            return;
        }

        switches = GetComponentsInChildren<SwitchInteractable>(true);
        if (switches.Length < puzzleData.SwitchCount)
            Debug.LogWarning($"[{nameof(SwitchPanelController)}] '{name}' has {switches.Length} switches " +
                             $"but '{puzzleData.name}' asks for {puzzleData.SwitchCount}.", this);

        for (int i = 0; i < switches.Length; i++)
        {
            switches[i].Bind(this, i);
            switches[i].gameObject.SetActive(i < puzzleData.SwitchCount);
        }
    }

    private void Start() => StartCoroutine(RegisterStartPositions());

    /// <summary>
    /// Puts the starting positions on record once the Data scene's manager exists — the same wait
    /// the valves do — and snaps every switch to what is stored (a checkpoint, a previous visit).
    /// </summary>
    private IEnumerator RegisterStartPositions()
    {
        if (!PuzzleStateManager.Exists)
            yield return new WaitUntil(() => PuzzleStateManager.Exists);

        for (int i = 0; i < switches.Length && i < puzzleData.SwitchCount; i++)
        {
            PuzzleStateManager.Instance.SetValvePosition(puzzleData.SwitchStateKey(i), IsUp(i) ? 1 : 0);
            switches[i].SnapToState();
        }
    }

    public bool IsUp(int index)
    {
        if (puzzleData == null) return false;
        int fallback = puzzleData.StartUp ? 1 : 0;
        if (!PuzzleStateManager.Exists) return fallback == 1;
        return PuzzleStateManager.Instance.GetValvePosition(puzzleData.SwitchStateKey(index), fallback) == 1;
    }

    /// <summary>Flips one switch and checks the whole row.</summary>
    public void Flip(int index)
    {
        if (puzzleData == null || IsCompleted || !PuzzleStateManager.Exists) return;

        PuzzleStateManager.Instance.SetValvePosition(puzzleData.SwitchStateKey(index), IsUp(index) ? 0 : 1);

        if (AudioManager.Exists && !string.IsNullOrEmpty(puzzleData.FlipSoundId))
            AudioManager.Instance.PlaySFX(puzzleData.FlipSoundId, switches[index].transform.position);

        CheckSwitches();
    }

    private void CheckSwitches()
    {
        for (int i = 0; i < puzzleData.SwitchCount; i++)
            if (IsUp(i) != puzzleData.IsCorrectUp(i)) return;

        PuzzleStateManager.Instance.SetPuzzleCompleted(puzzleData.PuzzleId);

        if (AudioManager.Exists && !string.IsNullOrEmpty(puzzleData.CompletedSoundId))
            AudioManager.Instance.PlaySFX(puzzleData.CompletedSoundId, transform.position);

        Debug.Log($"[{nameof(SwitchPanelController)}] Puzzle completed: {puzzleData.PuzzleId}");
    }
}
