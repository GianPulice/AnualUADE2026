using System;
using UnityEngine;

public static class InteractionEvents
{
    public static event Action<IInteractable> OnTargetChanged;
    public static void TargetChanged(IInteractable newTarget) => OnTargetChanged?.Invoke(newTarget);

    // The player pressed interact on a target that accepted it (raised after Interact ran). For
    // systems that react to player activity rather than to one object, like the Architect's
    // inactivity timer.
    public static event Action<IInteractable> OnInteracted;

    /// <summary>
    /// <see cref="OnInteracted"/> plus where on the target the interaction landed: the world point
    /// the crosshair's probe hit, or null when the target was not picked by the crosshair (a box
    /// held as the forced target, a zone prompt). For what has to look at the spot and not only at
    /// the object, like the arm that reaches out (<see cref="PlayerGrabGesture"/>). Read at the
    /// press: a pickup is gone by the time anything else looks for it.
    /// </summary>
    public static event Action<IInteractable, Vector3?> OnInteractedAt;

    public static void Interacted(IInteractable interactable) => Interacted(interactable, null);

    public static void Interacted(IInteractable interactable, Vector3? point)
    {
        OnInteracted?.Invoke(interactable);
        OnInteractedAt?.Invoke(interactable, point);
    }

    // Fired by an interactable when its own state changes and the currently displayed prompt
    // would go stale (e.g. a door that just opened needs to advertise "Close" instead of
    // "Open" without waiting for the player to look away and back).
    public static event Action OnPromptRefreshRequested;
    public static void RequestPromptRefresh() => OnPromptRefreshRequested?.Invoke();

    /// <summary>
    /// A message from the game itself about an interaction rather than the prompt of something the
    /// player is looking at — a key used up, a reward left on the floor. <c>InteractionNotificationFeed</c>
    /// stacks it with the other interaction notifications and drops it after <c>seconds</c>.
    /// </summary>
    public static event Action<string, float> OnGlobalMessage;

    /// <summary>Posts a global message. Ignored when the text is blank.</summary>
    public static void RaiseGlobalMessage(string text, float seconds = 3f)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        OnGlobalMessage?.Invoke(text, seconds);
    }
}
