using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Mezcla los <see cref="SO_VisionFogConfig"/> de los clips activos según el peso que
/// les da Timeline (crossfade entre clips solapados) y empuja el resultado directo a
/// los shader globals vía <see cref="VisionRangeController.ApplyPreviewBlend"/>.
///
/// Solo escribe cuando hay al menos un clip con peso > 0. Si el playhead está fuera de
/// cualquier clip, no toca nada — así en Play mode el <see cref="VisionRangeController"/>
/// sigue siendo dueño del estado fuera de los tramos con Timeline activo.
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
/// Pista de Timeline para autorear/scrubear transiciones de vision fog sin tocar código.
/// Bindeala a un <see cref="VisionRangeController"/> en la escena de tuning; cada clip
/// referencia un <see cref="SO_VisionFogConfig"/> y superponer clips da crossfade gratis.
///
/// Pensada para preview en editor (arrastrar el playhead sin dar Play). Para disparar
/// transiciones reales durante gameplay seguí usando LightZone + PushConfig/PopConfig —
/// esta pista no negocia con el stack de configs del controller, así que si en algún
/// momento se reproduce en simultáneo con una LightZone activa, van a pisarse.
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
