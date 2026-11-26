/// <summary>
/// A puzzle that can start over: the five controllers that hand out a reward (the Puzzle 1 panel,
/// boxes and valves, the lever panel, the switch row and the keypad). <see cref="PuzzleCaptureRestart"/>
/// calls it when a capture takes the reward the player was carrying.
///
/// The two halves are separate because they run at different times: <see cref="ResetState"/> has to
/// be repeatable (a checkpoint's snapshot, taken while the puzzle was solved, puts the "solved" back
/// at every later capture until the puzzle is solved again), while <see cref="ResetObjects"/> moves
/// things in the world and runs once.
/// </summary>
public interface IPuzzleRestartable
{
    string PuzzleId { get; }

    /// <summary>The item the puzzle hands out. What is carried of it is what a capture takes.</summary>
    SO_InventoryItem RewardItem { get; }

    /// <summary>
    /// The progress the puzzle keeps in <see cref="PuzzleStateManager"/> back to a fresh start: no
    /// "solved", valves and switches at their start positions, no box in a basket. Touches nothing in
    /// the world, and is safe to call again.
    /// </summary>
    void ResetState();

    /// <summary>
    /// What the player can see and touch back where it began: the panel open for a new attempt, the
    /// handles and wheels at their start pose, the boxes off their baskets and free to push.
    /// Called right after <see cref="ResetState"/>.
    /// </summary>
    void ResetObjects();
}
