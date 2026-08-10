using UnityEngine;

/// <summary>
/// A safe point in the level. When it activates it becomes the place the player reappears at
/// after being caught by the Nemesis, and the puzzle progress at that moment is snapshotted.
///
/// Two activation conditions are supported, chosen per instance:
///   - <c>PhysicalTrigger</c>: the player walks into this object's trigger collider. Needs a
///     Collider with <c>Is Trigger</c> on. Good for corridors and room thresholds.
///   - <c>PuzzleCompleted</c>: the puzzle named in <see cref="puzzleId"/> is solved. Ties the
///     checkpoint to progress rather than to geography.
///   - <c>Either</c>: whichever happens first (default).
///
/// A checkpoint only ever activates once. Walking back through an earlier checkpoint after a
/// later one is already active must not move the respawn point backwards.
/// </summary>
public class Checkpoint : MonoBehaviour
{
    public enum EActivationMode
    {
        PhysicalTrigger,
        PuzzleCompleted,
        Either,
    }

    [SerializeField] private EActivationMode activationMode = EActivationMode.Either;

    [Tooltip("Where the player reappears, facing this transform's forward. " +
             "If empty, this object's own transform is used.")]
    [SerializeField] private Transform respawnPoint;

    [Tooltip("Puzzle whose completion activates this checkpoint. " +
             "Only read in PuzzleCompleted / Either mode.")]
    [SerializeField] private string puzzleId;

    [Tooltip("Tag the trigger filters by. Only read in PhysicalTrigger / Either mode.")]
    [SerializeField] private string playerTag = "Player";

    private bool hasActivated;

    public Transform RespawnPoint => respawnPoint != null ? respawnPoint : transform;
    public string PuzzleId => puzzleId;
    public bool HasActivated => hasActivated;

    private bool ListensToPuzzle =>
        activationMode == EActivationMode.PuzzleCompleted || activationMode == EActivationMode.Either;

    private bool ListensToTrigger =>
        activationMode == EActivationMode.PhysicalTrigger || activationMode == EActivationMode.Either;

    private void OnEnable()
    {
        if (!ListensToPuzzle) return;
        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
    }

    private void OnDisable()
    {
        if (!ListensToPuzzle) return;
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
    }

    private void Start()
    {
        // Catch-up: the puzzle may already be solved by the time this checkpoint loads (the
        // level comes in additively, or a snapshot was restored). The event fires once and only
        // on the transition, so without this the checkpoint would never activate.
        if (!ListensToPuzzle || string.IsNullOrWhiteSpace(puzzleId)) return;
        if (!PuzzleStateManager.Exists) return;

        if (PuzzleStateManager.Instance.IsPuzzleCompleted(puzzleId)) Activate();
    }

    private void HandlePuzzleCompleted(string completedId)
    {
        if (string.IsNullOrWhiteSpace(puzzleId)) return;
        if (completedId != puzzleId) return;

        Activate();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!ListensToTrigger) return;
        if (!other.CompareTag(playerTag)) return;

        Activate();
    }

    private void Activate()
    {
        if (hasActivated) return;

        if (!CheckpointManager.Exists)
        {
            Debug.LogWarning($"[Checkpoint] '{name}' met its activation condition but there is no " +
                             $"CheckpointManager in the scene — the respawn point was not set.", this);
            return;
        }

        hasActivated = true;
        CheckpointManager.Instance.ActivateCheckpoint(this);
    }

    private void OnDrawGizmos()
    {
        Transform point = RespawnPoint;

        Gizmos.color = hasActivated ? new Color(0.10f, 0.42f, 0.10f) : new Color(0.53f, 0.53f, 0.53f);
        Gizmos.DrawWireSphere(point.position, 0.4f);
        Gizmos.DrawRay(point.position, point.forward);
    }
}
