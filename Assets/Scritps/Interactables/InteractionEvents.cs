using System;

public static class InteractionEvents
{
    public static event Action<IInteractable> OnTargetChanged;
    public static void TargetChanged(IInteractable newTarget) => OnTargetChanged?.Invoke(newTarget);

    // Fired by an interactable when its own state changes and the currently displayed prompt
    // would go stale (e.g. a door that just opened needs to advertise "Close" instead of
    // "Open" without waiting for the player to look away and back).
    public static event Action OnPromptRefreshRequested;
    public static void RequestPromptRefresh() => OnPromptRefreshRequested?.Invoke();
}
