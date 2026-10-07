using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Applies <see cref="CameraShake"/> to the Cinemachine camera it sits on, after the Noise stage.
/// Added at runtime by CameraShake; it does nothing while no shake is set.
/// </summary>
[AddComponentMenu("")]
public class CameraShakeExtension : CinemachineExtension
{
    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Noise) return;

        float amplitude = CameraShake.Amplitude;
        if (amplitude <= 0f) return;

        float t = Time.time * CameraShake.Frequency;
        Vector3 wobble = new Vector3(
            Mathf.PerlinNoise(t, 0.31f) - 0.5f,
            Mathf.PerlinNoise(0.73f, t) - 0.5f,
            (Mathf.PerlinNoise(t, t * 0.5f) - 0.5f) * 0.5f) * (2f * amplitude);

        state.OrientationCorrection *= Quaternion.Euler(wobble);
    }
}
