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
/// On the injured clips, the hurt foot's events carry the String "drag" (see <see cref="DragTag"/>).
///
/// Where the frames came from: sampled off the rig, not placed by eye. Each event sits where that
/// foot's lowest point first comes down to within 1.5 cm of the floor. Placing them by eye is what
/// had Running's two steps at 0.23 / 0.57 of the cycle while the feet land at 0.34 / 0.85 — a
/// 1/3 : 2/3 rhythm against legs that go 1/2 : 1/2, which is a sprint that sounds out of step.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/Audio/Footstep Animation Relay")]
public class FootstepAnimationRelay : MonoBehaviour
{
    /// <summary>
    /// The AnimationEvent String that marks a footfall of the hurt leg in the injured clips. That
    /// foot plays the limp drag instead of a step, so the step-drag pattern is tied to the leg that
    /// actually limps instead of to a counter that a skipped or doubled event puts out of phase.
    /// </summary>
    public const string DragTag = "drag";

    [Tooltip("Auto-resolved from the parents if left empty.")]
    [SerializeField] private FootstepEmitter emitter;

    [Tooltip("Events from a clip blended in below this weight are dropped.\n\n" +
             "During a crossfade (walk to run, walk to crouch, healthy to injured) BOTH states fire " +
             "their events, so a footfall of the clip fading out and one of the clip fading in land " +
             "a few frames apart: two steps on top of each other, which is the 'golpeteo' of " +
             "WIR-023. 0.5 keeps exactly the clip the pose is mostly made of.")]
    [SerializeField, Range(0f, 1f)] private float minClipWeight = 0.5f;

    private bool warned;

    private void Awake()
    {
        if (emitter == null) emitter = GetComponentInParent<FootstepEmitter>();
    }

    /// <summary>
    /// The AnimationEvent target. Keep this name — the clips reference it by string. Takes the
    /// event itself so it can read the blend weight of the clip that fired it and its tag.
    /// </summary>
    public void Step(AnimationEvent evt)
    {
        // animatorClipInfo is only filled in for events fired by an Animator; an event raised any
        // other way (Animation component, a test) has weight 0 and is let through.
        if (evt != null && evt.animatorClipInfo.clip != null &&
            evt.animatorClipInfo.weight < minClipWeight) return;

        if (emitter != null)
        {
            emitter.Step(evt != null && evt.stringParameter == DragTag);
            return;
        }

        if (warned) return;
        warned = true;
        Debug.LogWarning($"[{nameof(FootstepAnimationRelay)}] '{name}' found no FootstepEmitter in " +
                         "its parents, so the animation's footstep events are going nowhere.", this);
    }
}
