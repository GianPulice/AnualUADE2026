using UnityEngine;

/// <summary>
/// Wall occlusion for one-shot sounds played through the AudioManager pool, which has none of its
/// own: one linecast from the sound to the AudioListener, and a hit turns the volume down. The
/// same rule FootstepEmitter applies to the footsteps, here for anything else that needs it (the
/// Central Puzzle 2 boiler, which must not be heard from the lever room).
///
/// One linecast per call: meant for discrete sounds (a knock, a click), not for every frame.
/// </summary>
public static class AudioOcclusion
{
    private static AudioListener cachedListener;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cachedListener = null;

    /// <summary>
    /// 1 with a clear line from <paramref name="from"/> to the listener, <paramref name="occludedVolume"/>
    /// through anything on <paramref name="occluders"/>. 1 when there is no listener or no mask.
    /// </summary>
    public static float VolumeMultiplier(Vector3 from, LayerMask occluders, float occludedVolume)
    {
        if (occluders.value == 0) return 1f;

        // Any and not First: First is deprecated for relying on instance ID order, and a scene only
        // ever has one listener. Cleared by Unity when its scene unloads, so a stale one is re-found.
        if (cachedListener == null) cachedListener = Object.FindAnyObjectByType<AudioListener>();
        if (cachedListener == null) return 1f;

        return Physics.Linecast(from, cachedListener.transform.position, occluders, QueryTriggerInteraction.Ignore)
            ? Mathf.Clamp01(occludedVolume)
            : 1f;
    }
}
