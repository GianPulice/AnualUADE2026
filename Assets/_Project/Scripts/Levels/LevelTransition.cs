using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Leaves the level for the next one: empties the inventory and asks the ScreenManager for the
/// next scene group. Everything about it — where it goes, when, and whether a puzzle has to be
/// solved first — is in its <see cref="SO_LevelTransition"/>.
///
/// Three ways in (the asset's Trigger):
///  • the player walking into the collider on this object (Is Trigger),
///  • the required puzzle being completed,
///  • <see cref="RequestTransition"/>, for whoever knows when the level is really over: a Timeline
///    Signal at the end of a cinematic, an animation event, a UnityEvent.
/// In every case the required puzzle is checked again, and the transition happens once.
///
/// Module and puzzle state need nothing here: they live in the Data scene and outlive the level.
/// </summary>
[RequireComponent(typeof(Collider))]
public class LevelTransition : MonoBehaviour
{
    private const string PlayerTag = "Player";

    [SerializeField] private SO_LevelTransition transition;

    [Tooltip("The channel the ScreenManager listens to. Already set on the prefab.")]
    [SerializeField] private ScreenEventChannel screenChannel;

    private bool started;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        if (transition == null)
            Debug.LogError($"[{nameof(LevelTransition)}] No SO_LevelTransition on '{name}'.", this);
        else if (transition.Trigger == LevelTransitionTrigger.PuzzleCompleted &&
                 string.IsNullOrWhiteSpace(transition.RequiredPuzzleId))
            Debug.LogError($"[{nameof(LevelTransition)}] '{transition.name}' waits for a puzzle but its " +
                           "Required Puzzle Id is empty: it will never fire.", this);

        if (screenChannel == null)
            Debug.LogError($"[{nameof(LevelTransition)}] No ScreenEventChannel on '{name}'.", this);

        PuzzleStateManager.OnPuzzleCompleted += HandlePuzzleCompleted;
    }

    private void OnDestroy()
    {
        PuzzleStateManager.OnPuzzleCompleted -= HandlePuzzleCompleted;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (transition == null || transition.Trigger != LevelTransitionTrigger.PlayerEntersTrigger) return;
        if (!other.CompareTag(PlayerTag)) return;
        RequestTransition();
    }

    private void HandlePuzzleCompleted(string puzzleId)
    {
        if (transition == null || transition.Trigger != LevelTransitionTrigger.PuzzleCompleted) return;
        if (puzzleId != transition.RequiredPuzzleId) return;
        RequestTransition();
    }

    /// <summary>
    /// Leaves the level, if the required puzzle is done and it has not been left already. Public
    /// for Timeline Signals, animation events and UnityEvents.
    /// </summary>
    public void RequestTransition()
    {
        if (started || transition == null || screenChannel == null) return;
        if (!IsUnlocked()) return;

        if (string.IsNullOrWhiteSpace(transition.NextGroupLabel))
        {
            Debug.LogError($"[{nameof(LevelTransition)}] '{transition.name}' has no Next Group Label.", this);
            return;
        }

        started = true;
        RunAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private bool IsUnlocked()
    {
        if (string.IsNullOrWhiteSpace(transition.RequiredPuzzleId)) return true;
        return PuzzleStateManager.Exists &&
               PuzzleStateManager.Instance.IsPuzzleCompleted(transition.RequiredPuzzleId);
    }

    private async UniTaskVoid RunAsync(CancellationToken token)
    {
        if (transition.DelaySeconds > 0f)
            await UniTask.Delay(System.TimeSpan.FromSeconds(transition.DelaySeconds), cancellationToken: token);

        // A scene change already under way (a retry, the menu) wins: going on would fight it.
        if (ScreenManager.Exists && ScreenManager.Instance.IsTransitioning)
        {
            started = false;
            return;
        }

        if (transition.ClearInventory && InventoryManager.Exists)
            InventoryManager.Instance.ClearAll();

        Debug.Log($"[{nameof(LevelTransition)}] Leaving for '{transition.NextGroupLabel}'.", this);
        screenChannel.RaisePushScreen(transition.NextGroupLabel);
    }

    private void OnDrawGizmos()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.25f);
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.9f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
