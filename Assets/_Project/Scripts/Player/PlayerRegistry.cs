using System;
using UnityEngine;

/// <summary>
/// Single point of access to the player from anywhere in the project.
///
/// Replaces the <c>GameObject.FindGameObjectWithTag("Player")</c> polling that several systems
/// were each doing on their own (VisionRangeController, NemesisStateManager). The player lives
/// in an additive gameplay scene, so consumers loaded before it could not resolve it at Start
/// and had to keep re-searching every N frames.
///
/// It is a <c>static class</c> and not a <see cref="Singleton{T}"/> on purpose: this project's
/// Singleton does not auto-create, it needs a GameObject placed in the scene plus a
/// <c>CreateSingleton()</c> call in Awake. A registry that the player itself writes to from its
/// own <c>Awake</c> cannot depend on another MonoBehaviour having woken up first.
///
/// Usage from a component that needs to react when the player appears:
/// <code>
/// private void OnEnable()  => PlayerRegistry.SubscribeAndCatchUp(HandlePlayer);
/// private void OnDisable() => PlayerRegistry.Unsubscribe(HandlePlayer);
/// </code>
/// </summary>
public static class PlayerRegistry
{
    private static PlayerStateManager current;

    /// <summary>The registered player, or null if it has not spawned yet.</summary>
    public static PlayerStateManager Current => current;

    public static bool HasPlayer => current != null;

    /// <summary>Convenience for the many consumers that only need the position.</summary>
    public static Transform CurrentTransform => current != null ? current.transform : null;

    /// <summary>Raised right after a player registers itself.</summary>
    public static event Action<PlayerStateManager> OnPlayerRegistered;

    /// <summary>Raised right before the registered player goes away (scene unload, destroy).</summary>
    public static event Action<PlayerStateManager> OnPlayerUnregistered;

    /// <summary>
    /// Static state survives leaving Play mode when "Enter Play Mode Options" has domain reload
    /// disabled, which would leave a destroyed player cached on the next run. Unity calls this
    /// before the first scene loads on every Play, which is exactly when we want a clean slate.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        current = null;
        OnPlayerRegistered = null;
        OnPlayerUnregistered = null;
    }

    public static void Register(PlayerStateManager player)
    {
        if (player == null) return;
        if (ReferenceEquals(current, player)) return;

        if (current != null)
        {
            Debug.LogWarning($"[PlayerRegistry] '{player.name}' is registering while " +
                             $"'{current.name}' was still registered. The newcomer wins — check " +
                             $"there are not two players in the loaded scenes.", player);
        }

        current = player;
        OnPlayerRegistered?.Invoke(player);
    }

    public static void Unregister(PlayerStateManager player)
    {
        // Only the currently registered player may clear the slot: a stale object being
        // destroyed after a replacement registered must not wipe the good reference.
        if (player == null || !ReferenceEquals(current, player)) return;

        current = null;
        OnPlayerUnregistered?.Invoke(player);
    }

    /// <summary>
    /// Subscribes to <see cref="OnPlayerRegistered"/> and, if the player is already registered,
    /// invokes the callback immediately.
    ///
    /// Without the catch-up, any consumer enabled after the player woke up would wait forever
    /// for an event that already fired — which is the exact ordering problem the polling was
    /// working around.
    /// </summary>
    public static void SubscribeAndCatchUp(Action<PlayerStateManager> callback)
    {
        if (callback == null) return;

        OnPlayerRegistered += callback;
        if (current != null) callback(current);
    }

    public static void Unsubscribe(Action<PlayerStateManager> callback)
    {
        if (callback == null) return;
        OnPlayerRegistered -= callback;
    }
}
