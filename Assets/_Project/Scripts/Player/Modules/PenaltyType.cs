/// <summary>
/// Which body part a module's penalty targets. Used to route ApplyPenalty(...) in the player
/// to the correct handler and to describe the module's role in ScriptableObject inspector.
/// </summary>
public enum PenaltyType
{
    Legs,
    Chest,
    Head
}
