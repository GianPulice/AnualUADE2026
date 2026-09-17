using System;

public static class InteractionEvents
{
    public static event Action<IInteractable> OnTargetChanged;
    public static void TargetChanged(IInteractable newTarget) => OnTargetChanged?.Invoke(newTarget);

    // The player pressed interact on a target that accepted it (raised after Interact ran). For
    // systems that react to player activity rather than to one object, like the Architect's
    // inactivity timer.
    public static event Action<IInteractable> OnInteracted;
    public static void Interacted(IInteractable interactable) => OnInteracted?.Invoke(interactable);

    // Fired by an interactable when its own state changes and the currently displayed prompt
    // would go stale (e.g. a door that just opened needs to advertise "Close" instead of
    // "Open" without waiting for the player to look away and back).
    public static event Action OnPromptRefreshRequested;
    public static void RequestPromptRefresh() => OnPromptRefreshRequested?.Invoke();

    /// <summary>
    /// A message from the game itself rather than from something the player is looking at — an
    /// item that arrived without a pickup, a system telling the player where it stands.
    /// <c>InteractionPromptView</c> shows it in its Global variant and drops it after
    /// <c>seconds</c>.
    ///
    /// The prompt is a single slot, so a global message and an interaction prompt compete for it:
    /// the message wins until it expires, except against a target the player can actually act on.
    /// </summary>
    public static event Action<string, float> OnGlobalMessage;

    /// <summary>Posts a global message. Ignored when the text is blank.</summary>
    public static void RaiseGlobalMessage(string text, float seconds = 3f)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        OnGlobalMessage?.Invoke(text, seconds);
    }
}
