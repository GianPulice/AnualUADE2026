using UnityEngine;

/// <summary>
/// The thud of the Nemesis's dragging arm coming down on the floor. One job: when the walk clip's
/// "ArmThud" event fires, play one low impact at the hand, in 3D, on the Nemesis bus.
///
/// ── WHY AN ANIMATION EVENT ─────────────────────────────────────────────────
///
/// The walk carries one arm out in front and paddles the other along the floor (see
/// <see cref="NemesisArmWallGuard"/>). The paddling arm comes down once per stride, and when it does
/// is a fact of the clip, not of the Nemesis's speed: Patrol and Chase both play E_Walk, Chase at
/// twice the rate. An event on the frame the hand lands follows both for free, the same way the
/// footfall events do, and nothing here reads the FSM.
///
/// The event sits where the arm's lowest finger first comes within about 2.5 cm of the floor in
/// E_Walk (frame 21 of 60, at 24 fps): sampled off the rig, not placed by eye. Re-measure it if the
/// clip is retimed.
///
/// ── WHAT IT DOES NOT DO ────────────────────────────────────────────────────
///
/// It makes sound and nothing else: no noise for the Nemesis to hear (noise is the player's sphere,
/// see docs/CLAUDE.md), and no occlusion — a one-shot through AudioManager's pool, like the drop
/// cues. It does not make the footsteps either: those are <see cref="FootstepEmitter"/>'s.
///
/// SETUP: on the GameObject with the Animator, next to FootstepAnimationRelay (Unity delivers an
/// AnimationEvent to the Animator's own GameObject). Assign the dragging hand and a clip. With no
/// clip it does nothing and says so once.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("WIRED/Nemesis/Arm Thud")]
public class NemesisArmThud : MonoBehaviour
{
    /// <summary>The AnimationEvent function name the clip calls. Keep it: the clip references it by
    /// string.</summary>
    public const string EventName = nameof(ArmThud);

    [Tooltip("The hand that lands on the floor (hand.L: the arm that paddles). The sound comes from " +
             "here. Empty = from this object.")]
    [SerializeField] private Transform hand;

    [Tooltip("One is picked at random per thud. Empty = silent, with a warning.")]
    [SerializeField] private AudioClip[] clips = System.Array.Empty<AudioClip>();

    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    [Tooltip("Pitch drawn per thud. Well below 1: a step played down here reads as an arm's weight " +
             "and not as a foot. Replace the clip with a real thud and bring this back up.")]
    [SerializeField] private Vector2 pitchRange = new Vector2(0.5f, 0.58f);

    [Header("3D falloff")]
    [Tooltip("Metres at full volume.")]
    [SerializeField, Min(0f)] private float minDistance = 2f;

    [Tooltip("Metres at which it is gone. A few, on purpose: it is heard when the Nemesis is close.")]
    [SerializeField, Min(0.1f)] private float maxDistance = 10f;

    [Tooltip("Linear is the only one that reaches silence at Max Distance; Logarithmic keeps a " +
             "tail past it.")]
    [SerializeField] private AudioRolloffMode rolloff = AudioRolloffMode.Linear;

    [Header("Guards")]
    [Tooltip("Events from a clip blended in below this weight are dropped. Patrol and Chase both play " +
             "E_Walk, so crossfading between them fires the event from both: two thuds a few frames " +
             "apart. Same guard as FootstepAnimationRelay.")]
    [SerializeField, Range(0f, 1f)] private float minClipWeight = 0.5f;

    [Tooltip("Shortest time allowed between two thuds, in seconds. The fastest real gap is the " +
             "walk's cycle at Chase speed, 1.25 s.")]
    [SerializeField, Min(0f)] private float minInterval = 0.4f;

    private float lastThudTime = float.NegativeInfinity;
    private bool warned;

    /// <summary>The AnimationEvent target. Takes the event itself to read the blend weight of the
    /// clip that fired it.</summary>
    public void ArmThud(AnimationEvent evt)
    {
        if (evt != null && evt.animatorClipInfo.clip != null &&
            evt.animatorClipInfo.weight < minClipWeight) return;

        if (Time.time - lastThudTime < minInterval) return;

        AudioClip clip = PickClip();
        if (clip == null)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning($"[{nameof(NemesisArmThud)}] '{name}' has no clip, so the arm never makes " +
                             "a sound. Assign one.", this);
            return;
        }

        AudioManager manager = AudioManager.Instance;
        if (manager == null) return;

        lastThudTime = Time.time;

        Vector3 at = hand != null ? hand.position : transform.position;
        float pitch = Random.Range(pitchRange.x, pitchRange.y);
        manager.PlayClip(clip, SO_SoundData.SoundCategory.Nemesis, at, volume, pitch,
                         minDistance, maxDistance, rolloff);
    }

    private AudioClip PickClip()
    {
        if (clips == null || clips.Length == 0) return null;

        int start = Random.Range(0, clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            AudioClip clip = clips[(start + i) % clips.Length];
            if (clip != null) return clip;
        }

        return null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (maxDistance <= minDistance) maxDistance = minDistance + 0.1f;

        // Unity zero-fills a new Vector2 and a pitch of 0 never advances the clip (see
        // SO_FootstepBank.OnValidate).
        if (pitchRange.x <= 0f || pitchRange.y <= 0f) pitchRange = new Vector2(0.5f, 0.58f);
        else if (pitchRange.x > pitchRange.y) pitchRange = new Vector2(pitchRange.y, pitchRange.x);
    }
#endif
}
