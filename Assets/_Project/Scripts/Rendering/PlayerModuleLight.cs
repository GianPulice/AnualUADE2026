using UnityEngine;

/// <summary>
/// Where the player's module light is and how far it reaches, for gameplay that depends on it
/// (Central Puzzle 2 — the SP2 arrows that only show under it).
///
/// The module light the player sees is the vision fog's ("luz módulo", SO_VisionFogConfig
/// playerLightRange), pushed every frame by <see cref="VisionRangeController"/> as shader globals.
/// This reads those same globals back, so a room whose LightZone makes it darker shrinks the reach
/// here too, with no second value to keep in sync. Read-only: nothing here changes the light.
///
/// With no VisionRangeController running (a scene without the Data scene's fog), it answers with
/// the player's position and the caller's fallback range.
/// </summary>
public static class PlayerModuleLight
{
    private static VisionRangeController cachedController;
    private static float nextLookup;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cachedController = null;
        nextLookup = 0f;
    }

    /// <summary>
    /// The light's centre and radius right now. False when there is no player to carry it.
    /// </summary>
    public static bool TryGet(float fallbackRange, out Vector3 position, out float range)
    {
        Transform player = PlayerRegistry.CurrentTransform;
        position = player != null ? player.position : Vector3.zero;
        range = 0f;
        if (player == null) return false;

        VisionRangeController controller = Controller();
        if (controller != null && controller.isActiveAndEnabled)
        {
            Vector4 lightPosition = Shader.GetGlobalVector(VisionFogState.Ids.PlayerLightPosition);
            position = new Vector3(lightPosition.x, lightPosition.y, lightPosition.z);
            range = Shader.GetGlobalFloat(VisionFogState.Ids.PlayerLightRange);
            return true;
        }

        range = fallbackRange;
        return true;
    }

    // Looked up at most once a second: the controller lives in the persistent Data scene, so once
    // found it stays, and a scene without one should not pay a full scan every frame.
    private static VisionRangeController Controller()
    {
        if (cachedController != null) return cachedController;
        if (Time.unscaledTime < nextLookup) return null;

        nextLookup = Time.unscaledTime + 1f;
        cachedController = Object.FindAnyObjectByType<VisionRangeController>();
        return cachedController;
    }
}
