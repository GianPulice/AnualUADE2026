using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Blends the <see cref="SO_VisionFogConfig"/> of the active clips by the weight Timeline
/// gives them (crossfade between overlapping clips) and pushes the result straight to the
/// shader globals via <see cref="VisionRangeController.ApplyPreviewBlend"/>.
///
/// It only writes when there is at least one clip with weight > 0. If the playhead is outside
/// every clip, it touches nothing — so in Play mode the <see cref="VisionRangeController"/>
/// stays the owner of the state outside the stretches with an active Timeline.
/// </summary>
public class VisionFogMixerBehaviour : PlayableBehaviour
{
    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        var controller = playerData as VisionRangeController;
        if (controller == null) return;

        int inputCount = playable.GetInputCount();

        float totalWeight = 0f;
        float visionStart = 0f, visionEnd = 0f, lightPreservation = 0f, densityPower = 0f;
        float playerLightRange = 0f, playerLightIntensity = 0f, blurStrength = 0f;
        Color fogColor = Color.clear;
        Color playerLightColor = Color.clear;

        for (int i = 0; i < inputCount; i++)
        {
            float weight = playable.GetInputWeight(i);
            if (weight <= 0f) continue;

            var inputPlayable = (ScriptPlayable<VisionFogBehaviour>)playable.GetInput(i);
            SO_VisionFogConfig config = inputPlayable.GetBehaviour().config;
            if (config == null) continue;

            totalWeight          += weight;
            visionStart           += config.visionStart * weight;
            visionEnd              += config.visionEnd * weight;
            lightPreservation      += config.lightPreservation * weight;
            densityPower            += config.densityPower * weight;
            playerLightRange        += config.playerLightRange * weight;
            playerLightIntensity    += config.playerLightIntensity * weight;
            blurStrength             += config.blurStrength * weight;
            fogColor               += config.fogColor * weight;
            playerLightColor        += config.playerLightColor * weight;
        }

        if (totalWeight <= 0.0001f) return;

        controller.ApplyPreviewBlend(visionStart, visionEnd, fogColor, lightPreservation,
            densityPower, playerLightRange, playerLightIntensity, playerLightColor, blurStrength);
    }
}

/// <summary>
/// Timeline track for authoring/scrubbing vision fog transitions without touching code.
/// Bind it to a <see cref="VisionRangeController"/> in the tuning scene; each clip
/// references a <see cref="SO_VisionFogConfig"/> and overlapping clips give you a free crossfade.
///
/// Meant for editor preview (dragging the playhead without pressing Play). To trigger real
/// transitions during gameplay keep using LightZone + PushConfig/PopConfig — this track
/// does not negotiate with the controller's config stack, so if it ever plays at the same
/// time as an active LightZone, they will fight each other.
/// </summary>
[TrackClipType(typeof(VisionFogClip))]
[TrackBindingType(typeof(VisionRangeController))]
public class VisionFogTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        return ScriptPlayable<VisionFogMixerBehaviour>.Create(graph, inputCount);
    }
}
