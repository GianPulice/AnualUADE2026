using System;
using UnityEngine;

public class NemesisEvents : MonoBehaviour
{
    public static event Action OnChaseStarted;
    public static event Action OnChaseEnded;

    public static void ChaseStarted() => OnChaseStarted?.Invoke();
    public static void ChaseEnded()   => OnChaseEnded?.Invoke();
}
