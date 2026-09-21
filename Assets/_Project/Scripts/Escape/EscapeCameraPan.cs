using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// The last shot of the opening cinematic: the player's own gameplay camera orbits from the
/// player's right round to the nape, exactly like the end of the wake-up. One job. Because it
/// drives the gameplay rig's own axes, the last frame of the pan IS the gameplay camera: there is
/// nothing to cut to, and control comes back looking where the pan ended.
///
/// It reuses the mechanics of <see cref="WakeUpCameraPan"/> (the rig is bound in world space, a
/// lower horizontal value is further to the right) but not its wake-up specifics: no lying pivot
/// drop, no shoulder-offset release. The camera ends at the gameplay height and shoulder offset.
///
/// Look input is off for the whole cinematic (<see cref="CinematicState"/>), so nothing fights
/// the pan. Sits on the escape sequence object; it finds the rig at runtime, so the Player prefab
/// needs no edit.
/// </summary>
public class EscapeCameraPan : MonoBehaviour
{
    [Tooltip("Cuántos grados a la DERECHA del jugador (visto desde atrás) arranca la cámara. Menor " +
             "= arranca más cerca de la espalda; mayor = más de costado, y el giro es más largo.")]
    [SerializeField, Range(0f, 180f)] private float startAngle = 55f;

    [Tooltip("Cuánto más BAJA (grados) arranca la cámara que donde termina. Sube mientras orbita.")]
    [SerializeField, Range(0f, 40f)] private float startLowering = 5f;

    [Tooltip("Forma del giro a lo largo de su duración.")]
    [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private CinemachineOrbitalFollow orbital;
    private bool active;
    private float elapsed;
    private float duration;
    private float endVertical;

    public bool IsPanning => active;

    /// <summary>Starts the orbit; it lands behind the player after <paramref name="seconds"/>.</summary>
    public void Begin(float seconds)
    {
        if (!FindRig()) return;

        endVertical = orbital.VerticalAxis.Value;
        duration = Mathf.Max(0.1f, seconds);
        elapsed = 0f;
        active = true;

        Apply(0f);
    }

    /// <summary>Lands on the end framing at once. What a skip needs, and the net if the pan's
    /// marker was removed from the Timeline.</summary>
    public void Snap()
    {
        if (!FindRig()) return;

        if (!active) endVertical = orbital.VerticalAxis.Value;
        active = false;

        Apply(1f);
    }

    // Update and not LateUpdate: CinemachineBrain reads the axes in its own LateUpdate.
    private void Update()
    {
        if (!active) return;

        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);
        Apply(progress);

        if (progress >= 1f) active = false;
    }

    private void Apply(float progress)
    {
        float k = Mathf.Clamp01(ease.Evaluate(progress));

        // Behind the player is the player's own yaw. Lower = further right; the unwrapped lerp is
        // wrapped on write, so the orbit goes the way startAngle says and not the short way round.
        float horizontal = WakeUpCameraPan.PlayerYaw() - startAngle * (1f - k);
        orbital.HorizontalAxis.Value = WakeUpCameraPan.WrapToRange(horizontal, orbital.HorizontalAxis.Range);
        orbital.VerticalAxis.Value = endVertical - startLowering * (1f - k);
    }

    private bool FindRig()
    {
        if (orbital != null) return true;

        PlayerCameraController controller = FindAnyObjectByType<PlayerCameraController>();
        orbital = controller != null ? controller.GetComponent<CinemachineOrbitalFollow>() : null;

        if (orbital == null)
            Debug.LogWarning($"[{nameof(EscapeCameraPan)}] No player camera rig found: the last " +
                             "shot of the cinematic cannot pan.", this);
        return orbital != null;
    }
}
