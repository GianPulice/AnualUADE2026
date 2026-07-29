using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Behaviour that lives inside a <see cref="VisionFogTrack"/> clip. It only carries the
/// reference to the preset that clip represents — all mixing/blending happens in
/// <see cref="VisionFogMixerBehaviour"/>.
/// </summary>
public class VisionFogBehaviour : PlayableBehaviour
{
    public SO_VisionFogConfig config;
}

/// <summary>
/// Timeline clip meaning "the fog looks like this <see cref="SO_VisionFogConfig"/>
/// during this stretch". Meant for tuning in the editor: overlapping two clips on the track
/// produces an automatic crossfade (Timeline handles the weight blend via ClipCaps.Blending,
/// by dragging the clip corners) without having to write custom curves.
/// </summary>
[Serializable]
public class VisionFogClip : PlayableAsset, ITimelineClipAsset
{
    public SO_VisionFogConfig config;

    public ClipCaps clipCaps => ClipCaps.Blending;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<VisionFogBehaviour>.Create(graph);
        playable.GetBehaviour().config = config;
        return playable;
    }
}
