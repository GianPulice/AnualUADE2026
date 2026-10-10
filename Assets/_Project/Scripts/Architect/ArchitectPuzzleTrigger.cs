using System.Collections;
using UnityEngine;

/// <summary>
/// Plays an Architect context line when a puzzle is completed: <see cref="ArchitectZoneTrigger"/>
/// for a moment instead of a place (ARC_CTX_07: SP1 solved, the box room's lights have power).
///
/// Only the live completion counts. Everything else gated on a puzzle id subscribes and then
/// catches up in Start (<see cref="NemesisRoute"/>, <see cref="Checkpoint"/>); this deliberately
/// does not. A level loaded, or a checkpoint restored, with the puzzle already solved is not news,
/// and <see cref="PuzzleStateManager.OnPuzzleCompleted"/> is raised by neither.
///
/// The line is asked for one frame late. Whatever else answers the same completion (a module
/// resolving plays ARC_08 and chains its own context line) takes the voice first, whichever of the
/// two subscribed first, and this line queues behind it instead of cutting ARC_08 out.
///
/// Needs no collider and no particular place: any active object in the gameplay scene. Once per
/// run is enforced by the controller, as long as the line's category in the bank is Context.
/// </summary>
public class ArchitectPuzzleTrigger : MonoBehaviour
{
    [Tooltip("The puzzle whose completion plays the line. Only the moment it is solved counts: " +
             "a level that loads with it already solved stays silent.")]
    [PuzzleId]
    [SerializeField] private string puzzleId;

    [Tooltip("The line to play. Its category in the bank must be Context for it to play once per run.")]
    [SerializeField] private ArchitectLineID line = ArchitectLineID.ContextBoxLights;

    // OnEnable/OnDisable, like the other puzzle-gated components: switched off, it must not speak.
    private void OnEnable() => PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
    private void OnDisable() => PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;

    private void Start()
    {
        // An empty id matches nothing and says nothing: the line would just never play.
        if (string.IsNullOrWhiteSpace(puzzleId))
            Debug.LogWarning($"[{nameof(ArchitectPuzzleTrigger)}] '{name}' has no puzzle id: {line} never plays.", this);
    }

    private void HandlePuzzleCompleted(string completedId)
    {
        if (string.IsNullOrWhiteSpace(puzzleId) || completedId != puzzleId) return;

        StartCoroutine(TriggerNextFrame());
    }

    private IEnumerator TriggerNextFrame()
    {
        // A frame, not a time: puzzles are solved inside modals, where timeScale is 0.
        yield return null;

        if (ArchitectVoiceController.Instance == null)
        {
            Debug.LogWarning($"[{nameof(ArchitectPuzzleTrigger)}] '{name}': no ArchitectVoiceController loaded.", this);
            yield break;
        }

        ArchitectVoiceController.Instance.TriggerContext(line);
    }
}
