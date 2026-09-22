using UnityEngine;

/// <summary>
/// Holds a fog preset on top of the stack for as long as this object is active. One job: push on
/// enable, pop on disable. What turns it on and off is somebody else's business — in the escape
/// cinematic it is an Activation Track, so the shot that needs the fog gone owns exactly the frames
/// it covers.
///
/// Why it exists: a shot often needs a different fog than the gameplay around it. The escape's shot
/// 2A frames the Nemesis ~21 m from the camera, where the Dark preset leaves 0.44 % of the light, so
/// it pushes a wider band for its frames. (It used to push visionStart == visionEnd, which makes the
/// shader skip the fog entirely: the fog was centred on the player, ~13 m off camera. The escape now
/// centres it on the camera for the cinematic — VisionRangeController.SetCentreOverride — so the
/// shot keeps a fog of its own, WIR-040.)
///
/// Because the pop lives in OnDisable, every way out is covered without bookkeeping: the track
/// ending, a skip jumping past it, the director stopping, the scene unloading.
///
/// Left out on purpose: no trigger volume (that is <see cref="LightZone"/>), no blending of its own
/// (the preset's transitionDuration decides that), and no player check — a cinematic shot is not
/// where the player is.
/// </summary>
public class VisionFogOverride : MonoBehaviour
{
    [Tooltip("El preset que se aplica mientras este objeto está activo. Para apagar la niebla: " +
             "visionStart == visionEnd.")]
    [SerializeField] private SO_VisionFogConfig config;

    private VisionRangeController controller;
    private bool pushed;

    private void OnEnable()
    {
        if (config == null || pushed) return;

        // FindAnyObjectByType, as LightZone does: the controller drives global shader uniforms, so
        // there is only ever one, and it lives in the persistent Data scene.
        if (controller == null) controller = FindAnyObjectByType<VisionRangeController>();
        if (controller == null)
        {
            Debug.LogWarning($"[{nameof(VisionFogOverride)}] No VisionRangeController in the scene: " +
                             $"'{name}' leaves the fog as it is.", this);
            return;
        }

        controller.PushConfig(config);
        pushed = true;
    }

    // The same preset pushed twice would take two pops, so the guard matters: a pop without a push
    // would remove somebody else's entry.
    private void OnDisable()
    {
        if (!pushed) return;
        if (controller != null) controller.PopConfig(config);
        pushed = false;
    }
}
