/// <summary>
/// Marker for interactables that are part of a puzzle (sockets, valves, sequence panels). Systems
/// that care about puzzle activity rather than any interaction — the Architect's inactivity timer —
/// check for it on <see cref="InteractionEvents.OnInteracted"/>.
/// </summary>
public interface IPuzzleInteractable
{
}
