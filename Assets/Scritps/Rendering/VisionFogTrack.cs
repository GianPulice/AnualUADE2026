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

        // Accumulate weighted, then divide once. The per-field arithmetic lives on
        // VisionFogState so that adding a fog parameter does not mean editing this loop —
        // which is exactly how the old version drifted out of sync with the config.
        var blended = VisionFogState.Disabled;
        float totalWeight = 0f;

        for (int i = 0; i < inputCount; i++)
        {
            float weight = playable.GetInputWeight(i);
            if (weight <= 0f) continue;

            var inputPlayable = (ScriptPlayable<VisionFogBehaviour>)playable.GetInput(i);
            SO_VisionFogConfig config = inputPlayable.GetBehaviour().config;
            if (config == null) continue;

            totalWeight += weight;
            blended.AddWeighted(VisionFogState.FromConfig(config), weight);
        }

        if (totalWeight <= 0.0001f) return;

        blended.Normalise(totalWeight);
        controller.ApplyPreviewBlend(blended);
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
