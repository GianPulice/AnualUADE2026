using UnityEngine;

/// <summary>
/// How the interaction prompt should PRESENT an interactable. Optional: an interactable that does
/// not implement it is shown as <see cref="InteractionPromptKind.Common"/> with no icon, which is
/// what every door, valve, panel and note wants.
///
/// It is deliberately NOT part of <see cref="IInteractable"/>. Presentation is the prompt's
/// concern, not something the nine existing interactables should have to answer, and adding a
/// member there would force all of them to grow a field they have no opinion about.
/// </summary>
public interface IPromptPresentation
{
    /// <summary>Which of the prompt's variants this interactable is shown with.</summary>
    InteractionPromptKind Kind { get; }

    /// <summary>
    /// Icon for the prompt's well — the item being picked up, or the one a socket is asking for.
    /// Null means the well stays hidden, so a pickup whose item has no icon simply looks like a
    /// common prompt instead of showing an empty frame.
    /// </summary>
    Sprite PromptIcon { get; }
}

public enum InteractionPromptKind
{
    /// <summary>Doors, valves, panels, notes — anything that is not about an item.</summary>
    Common,

    /// <summary>Taking an item or putting one into a socket.</summary>
    Item,
}
