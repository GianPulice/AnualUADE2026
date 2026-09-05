using UnityEngine;

/// <summary>
/// Forwards a footstep AnimationEvent to the <see cref="FootstepEmitter"/> on an ancestor.
///
/// Unity delivers an AnimationEvent by name to the GameObject that owns the **Animator**. On the
/// player that is the rig child (`HS_CHARA_RIG [New]`), while the emitter lives on the prefab root
/// next to the Rigidbody and the state machine — where it belongs, since it reads the root's
/// position. Without this relay the clips would fire into nothing and Unity would log
/// "has no receiver" once per step.
///
/// It is a relay and not a second emitter on the rig for one reason: the emitter measures
/// displacement of its own transform for the teleport guard, and the rig child is animated in
/// place by root motion decisions that have nothing to do with where the character actually is.
///
/// SETUP: on the GameObject with the Animator. Nothing to wire — it finds the emitter upward.
/// Then add an AnimationEvent calling <c>Step</c> on each footfall frame of the locomotion clips.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/Audio/Footstep Animation Relay")]
public class FootstepAnimationRelay : MonoBehaviour
{
    [Tooltip("Auto-resolved from the parents if left empty.")]
    [SerializeField] private FootstepEmitter emitter;

    private bool warned;

    private void Awake()
    {
        if (emitter == null) emitter = GetComponentInParent<FootstepEmitter>();
    }

    /// <summary>The AnimationEvent target. Keep this name — the clips reference it by string.</summary>
    public void Step()
    {
        if (emitter != null)
        {
            emitter.Step();
            return;
        }

        if (warned) return;
        warned = true;
        Debug.LogWarning($"[{nameof(FootstepAnimationRelay)}] '{name}' found no FootstepEmitter in " +
                         "its parents, so the animation's footstep events are going nowhere.", this);
    }
}
