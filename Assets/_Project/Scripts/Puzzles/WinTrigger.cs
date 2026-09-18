using UnityEngine;

/// <summary>
/// Ends the slice with a win when the player walks into it — the "TO BE CONTINUED" past the gate
/// to Zone 2.
///
/// Reports through <see cref="GameResultManager.ReportWin"/>, the same channel the loss paths use,
/// so WinController opens the win screen with the run's time and resolved-module count and nothing
/// here has to know about UI. ReportWin is already once-per-run; the local flag only saves the
/// repeated lookups while the player stands inside.
///
/// SETUP: a GameObject with a Collider (Is Trigger — set automatically when the component is added)
/// placed on the FAR side of the gate, so it only fires once the player has actually crossed it.
/// Fill <see cref="requiredPuzzleId"/> with the hub puzzle the gate opens on, so slipping through a
/// gap in the geometry before the gate is open cannot end the run early. Leave it empty to win on
/// contact alone.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WinTrigger : MonoBehaviour
{
    [Tooltip("Puzzle that must be completed for this trigger to count — the one the gate opens on " +
             "(the hub with the three cores). Empty = win on contact, no condition.")]
    [SerializeField, PuzzleId] private string requiredPuzzleId;

    private const string PlayerTag = "Player";

    private bool hasFired;

    private void Reset()
    {
        // A solid collider would stop the player in the doorway instead of reporting anything.
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasFired) return;
        if (!other.CompareTag(PlayerTag)) return;
        if (!IsUnlocked()) return;

        hasFired = true;

        float time = ModuleManager.Exists ? ModuleManager.Instance.SessionTime : 0f;
        int resolved = ModuleManager.Exists ? ModuleManager.Instance.GetResolvedCount() : 0;

        GameResultManager.ReportWin(time, resolved);
    }

    private bool IsUnlocked()
    {
        if (string.IsNullOrWhiteSpace(requiredPuzzleId)) return true;

        return PuzzleStateManager.Exists &&
               PuzzleStateManager.Instance.IsPuzzleCompleted(requiredPuzzleId);
    }

    private void OnDrawGizmos()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.25f);
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
