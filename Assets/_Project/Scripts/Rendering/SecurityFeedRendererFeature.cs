using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The fullscreen pass of the security-camera look (<c>SecurityCamera.mat</c>), queued only on the
/// frames a <see cref="SecurityCameraFeed"/> shot is on the air. The rest of the game it costs
/// nothing — not even the colour copy that a plain Full Screen Pass with a pass-through shader would
/// still pay every frame.
///
/// Same settings as a Full Screen Pass Renderer Feature. On PC_Renderer it sits BEFORE PSXEffect
/// (same injection point, earlier in the list): the feed, overlay text included, then goes through
/// the game's PSX filter like the rest of the image.
/// </summary>
public class SecurityFeedRendererFeature : FullScreenPassRendererFeature
{
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Game cameras only: the Scene view goes on showing the plain scene during Play.
        if (renderingData.cameraData.cameraType != CameraType.Game) return;

        SecurityCameraFeed feed = SecurityCameraFeed.FindLive(renderingData.cameraData.camera);
        if (feed == null) return;

        feed.PushGlobals();
        base.AddRenderPasses(renderer, ref renderingData);
    }
}
