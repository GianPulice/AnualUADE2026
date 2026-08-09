using System;

public static class NemesisEvents
{
    public static event Action OnChaseStarted;
    public static event Action OnChaseEnded;

    // Normalized value [0,1]: 0 = far away / out of range, 1 = minimum distance.
    // The Nemesis is responsible for computing and raising this event every frame.
    public static event Action<float> OnProximityChanged;

    public static void ChaseStarted()                    => OnChaseStarted?.Invoke();
    public static void ChaseEnded()                      => OnChaseEnded?.Invoke();
    public static void ProximityChanged(float t)         => OnProximityChanged?.Invoke(t);
}
