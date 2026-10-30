using System.Collections;
using UnityEngine;

/// <summary>
/// The switch row of the switch room (Central Puzzle 2 — SP2). Its <see cref="SwitchInteractable"/>
/// children are switches 0, 1, 2… in hierarchy order; the ones past the asset's switch count are
/// hidden.
///
/// Positions live in <see cref="PuzzleStateManager"/> (as valve positions: <see cref="Middle"/>,
/// <see cref="Down"/> or <see cref="Up"/>, see <see cref="SO_SwitchPuzzleData.SwitchStateKey"/>), so
/// a checkpoint rollback puts the switches back where they were. A switch that starts in the middle
/// goes up on its first flip and from then on only between up and down; the middle never matches
/// an arrow, so every switch has to be flipped at least once. Checked on every flip, as the spec
/// asks: the moment the last switch lands right, the puzzle completes and the row locks.
/// </summary>
public class SwitchPanelController : MonoBehaviour
{
    /// <summary>Never flipped yet: the handle stands straight out, neither up nor down.</summary>
    public const int Middle = -1;
    public const int Down = 0;
    public const int Up = 1;

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
            PuzzleStateManager.Instance.SetValvePosition(puzzleData.SwitchStateKey(i), Position(i));
            switches[i].SnapToState();
        }
    }

    /// <summary><see cref="Middle"/>, <see cref="Down"/> or <see cref="Up"/>.</summary>
    public int Position(int index)
    {
        if (puzzleData == null) return Middle;
        int start = StartPosition();
        if (!PuzzleStateManager.Exists) return start;
        return PuzzleStateManager.Instance.GetValvePosition(puzzleData.SwitchStateKey(index), start);
    }

    private int StartPosition() =>
        puzzleData.StartsAt == SO_SwitchPuzzleData.StartPosition.Up ? Up
        : puzzleData.StartsAt == SO_SwitchPuzzleData.StartPosition.Down ? Down
        : Middle;

    /// <summary>Flips one switch (up from the middle or from down, down from up) and checks the whole row.</summary>
    public void Flip(int index)
    {
        if (puzzleData == null || IsCompleted || !PuzzleStateManager.Exists) return;

        PuzzleStateManager.Instance.SetValvePosition(puzzleData.SwitchStateKey(index), Position(index) == Up ? Down : Up);

        if (AudioManager.Exists && !string.IsNullOrEmpty(puzzleData.FlipSoundId))
            AudioManager.Instance.PlaySFX(puzzleData.FlipSoundId, switches[index].transform.position);

        CheckSwitches();
    }

    private void CheckSwitches()
    {
        // A switch still in the middle matches neither answer.
        for (int i = 0; i < puzzleData.SwitchCount; i++)
            if (Position(i) != (puzzleData.IsCorrectUp(i) ? Up : Down)) return;

        PuzzleStateManager.Instance.SetPuzzleCompleted(puzzleData.PuzzleId);

        if (AudioManager.Exists && !string.IsNullOrEmpty(puzzleData.CompletedSoundId))
            AudioManager.Instance.PlaySFX(puzzleData.CompletedSoundId);   // 2D: heard alike from anywhere

        Debug.Log($"[{nameof(SwitchPanelController)}] Puzzle completed: {puzzleData.PuzzleId}");
    }
}
