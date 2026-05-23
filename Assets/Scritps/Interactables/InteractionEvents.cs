using System;

public static class InteractionEvents 
{
    public static event Action<IInteractable> OnTargetChanged;
    public static void TargetChanged(IInteractable newTarget) => OnTargetChanged?.Invoke(newTarget);    
}
