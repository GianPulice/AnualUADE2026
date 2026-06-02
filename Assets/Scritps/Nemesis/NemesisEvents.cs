using System;

public static class NemesisEvents
{
    public static event Action OnChaseStarted;
    public static event Action OnChaseEnded;

    // Valor normalizado [0,1]: 0 = lejos/fuera de rango, 1 = distancia mínima.
    // El Nemesis es responsable de calcular y disparar este evento cada frame.
    public static event Action<float> OnProximityChanged;

    public static void ChaseStarted()                    => OnChaseStarted?.Invoke();
    public static void ChaseEnded()                      => OnChaseEnded?.Invoke();
    public static void ProximityChanged(float t)         => OnProximityChanged?.Invoke(t);
}
