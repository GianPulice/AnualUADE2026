/// <summary>
/// A puzzle asset that hands out an item when it is solved. Implemented by the puzzle SOs that have
/// a Reward Item (SO_PuzzleData, SO_SequencePuzzleData, SO_ContainerPuzzleData, SO_ValvePuzzleData),
/// so a <see cref="PuzzleRewardBox"/> can read the reward of whichever puzzle its id names without
/// caring which of them it is. The puzzle types without a reward simply do not implement it.
/// </summary>
public interface IPuzzleRewardSource
{
    string PuzzleId { get; }
    SO_InventoryItem RewardItem { get; }
}
