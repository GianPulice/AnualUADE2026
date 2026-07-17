using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Behaviour que vive dentro de un clip de <see cref="VisionFogTrack"/>. Solo transporta
/// la referencia al preset que representa ese clip — toda la mezcla/blend pasa por
/// <see cref="VisionFogMixerBehaviour"/>.
/// </summary>
public class VisionFogBehaviour : PlayableBehaviour
{
    public SO_VisionFogConfig config;
}

/// <summary>
/// Clip de Timeline que representa "la niebla se ve como este <see cref="SO_VisionFogConfig"/>
/// durante este tramo". Pensado para tuning en editor: superponer dos clips en la pista
/// produce un crossfade automático (Timeline maneja el blend de pesos vía ClipCaps.Blending,
/// arrastrando las esquinas del clip) sin necesidad de escribir curvas propias.
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
