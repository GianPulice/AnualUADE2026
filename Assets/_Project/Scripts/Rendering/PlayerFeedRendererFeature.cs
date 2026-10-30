using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The fullscreen pass of the player's camera feed (<c>PlayerCamera.mat</c>), queued only on the
/// frames the player's own camera rig is on the air (<see cref="PlayerCameraFeed.FindLive"/>). Any
/// other shot — a cinematic, a security camera, the defeat camera — goes out without it.
///
/// Same settings as a Full Screen Pass Renderer Feature. On PC_Renderer it sits BEFORE PSXEffect
/// (same injection point, earlier in the list): the feed, overlay included, then goes through the
/// game's PSX filter like the rest of the image. Its place relative to SecurityCameraFeed does not
/// matter: the two are never on the air together.
/// </summary>
public class PlayerFeedRendererFeature : FullScreenPassRendererFeature
{
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Game cameras only: the Scene view goes on showing the plain scene during Play.
        if (renderingData.cameraData.cameraType != CameraType.Game) return;

        PlayerCameraFeed feed = PlayerCameraFeed.FindLive(renderingData.cameraData.camera);
        if (feed == null || !feed.PushGlobals(renderingData.cameraData.camera)) return;

        base.AddRenderPasses(renderer, ref renderingData);
    }
}
