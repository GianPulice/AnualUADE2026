using UnityEngine;

/// <summary>
/// A safe point in the level. When it activates it becomes the place the player reappears at
/// after being caught by the Nemesis, and the puzzle progress at that moment is snapshotted.
///
/// Two activation conditions are supported, chosen per instance:
///   - <c>PhysicalTrigger</c>: the player walks into this object's trigger collider. Needs a
///     Collider with <c>Is Trigger</c> on. Good for corridors and room thresholds.
///   - <c>PuzzleCompleted</c>: any one of the puzzles in <see cref="puzzleIds"/> is solved. Ties
///     the checkpoint to progress rather than to geography. Several ids are allowed because a
///     room can be reached from more than one direction: the safe point is "the player got here",
///     and which puzzle got them here is not something the checkpoint should have to care about.
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

    [Tooltip("Puzzles whose completion activates this checkpoint — any one of them is enough. " +
             "Only read in PuzzleCompleted / Either mode.")]
    [PuzzleId]
    [SerializeField] private string[] puzzleIds = System.Array.Empty<string>();

    // Migration shim for the single-id field this replaced. Unity cannot carry a string into a
    // string[], so the checkpoints already authored in Zona 1 would silently lose their id and
    // never activate again — a checkpoint that quietly stops existing is close to impossible to
    // notice from play. Folded into puzzleIds and cleared on load; safe to delete once every
    // scene holding a Checkpoint has been opened and re-saved.
    [HideInInspector]
    [SerializeField] private string puzzleId;

    [Tooltip("Tag the trigger filters by. Only read in PhysicalTrigger / Either mode.")]
    [SerializeField] private string playerTag = "Player";

    private bool hasActivated;

    public Transform RespawnPoint => respawnPoint != null ? respawnPoint : transform;
    public bool HasActivated => hasActivated;

    private bool ListensToPuzzle =>
        activationMode == EActivationMode.PuzzleCompleted || activationMode == EActivationMode.Either;

    private bool ListensToTrigger =>
        activationMode == EActivationMode.PhysicalTrigger || activationMode == EActivationMode.Either;

    private void Awake()
    {
        MigrateLegacyPuzzleId();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Also here, so opening the scene rewrites the field and the next save drops the legacy
        // value for good, rather than migrating it again on every load forever.
        MigrateLegacyPuzzleId();
    }
#endif

    private void MigrateLegacyPuzzleId()
    {
        if (string.IsNullOrWhiteSpace(puzzleId)) return;

        puzzleIds ??= System.Array.Empty<string>();

        if (System.Array.IndexOf(puzzleIds, puzzleId) < 0)
        {
            System.Array.Resize(ref puzzleIds, puzzleIds.Length + 1);
            puzzleIds[^1] = puzzleId;
        }

        puzzleId = null;
    }

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
        if (!ListensToPuzzle || puzzleIds == null) return;
        if (!PuzzleStateManager.Exists) return;

        foreach (string id in puzzleIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (!PuzzleStateManager.Instance.IsPuzzleCompleted(id)) continue;

            Activate();
            return;
        }
    }

    private void HandlePuzzleCompleted(string completedId)
    {
        if (!Listens(completedId)) return;

        Activate();
    }

    /// <summary>Whether this checkpoint is waiting on that puzzle.</summary>
    private bool Listens(string completedId)
    {
        if (puzzleIds == null || string.IsNullOrWhiteSpace(completedId)) return false;

        foreach (string id in puzzleIds)
        {
            if (id == completedId) return true;
        }

        return false;
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
