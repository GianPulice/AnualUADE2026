using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// PROVISIONAL continuous camera shake, until the camera gets a proper shake system (the M2 chest
/// penalty's shake is still a TODO in PlayerStateManager). Today its only user is the Central
/// Puzzle 2 stabilization, whose shake calms down check by check.
///
/// Any system sets its own shake under a key and clears it when done; the strongest one wins. The
/// shake is a Perlin wobble of the camera's aim, added after Cinemachine's own noise by a
/// <see cref="CameraShakeExtension"/> that this puts on the live Cinemachine camera on demand — so
/// no prefab or scene needs it in advance.
///
/// Amplitude is in degrees of camera rotation; frequency is how fast it wobbles.
/// </summary>
public static class CameraShake
{
    private struct Shake
    {
        public float amplitude;
        public float frequency;
    }

    private static readonly Dictionary<object, Shake> sources = new Dictionary<object, Shake>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => sources.Clear();

    /// <summary>Strongest amplitude among the active sources, in degrees.</summary>
    public static float Amplitude { get; private set; }

    /// <summary>Frequency of the strongest source.</summary>
    public static float Frequency { get; private set; }

    /// <summary>Starts or updates the shake of <paramref name="source"/>. Amplitude 0 = none.</summary>
    public static void Set(object source, float amplitude, float frequency)
    {
        if (source == null) return;
        sources[source] = new Shake { amplitude = Mathf.Max(0f, amplitude), frequency = Mathf.Max(0f, frequency) };
        Recompute();
        if (Amplitude > 0f) EnsureExtensionOnLiveCamera();
    }

    /// <summary>Stops the shake of <paramref name="source"/>.</summary>
    public static void Clear(object source)
    {
        if (source == null || !sources.Remove(source)) return;
        Recompute();
    }

    private static void Recompute()
    {
        Amplitude = 0f;
        Frequency = 0f;
        foreach (Shake shake in sources.Values)
        {
            if (shake.amplitude <= Amplitude) continue;
            Amplitude = shake.amplitude;
            Frequency = shake.frequency;
        }
    }

    private static void EnsureExtensionOnLiveCamera()
    {
        Camera main = Camera.main;
        CinemachineBrain brain = main != null ? main.GetComponent<CinemachineBrain>() : null;
        if (brain == null) brain = Object.FindAnyObjectByType<CinemachineBrain>();
        if (brain == null) return;

        if (brain.ActiveVirtualCamera is CinemachineVirtualCameraBase live &&
            live.GetComponent<CameraShakeExtension>() == null)
            live.gameObject.AddComponent<CameraShakeExtension>();
    }
}
