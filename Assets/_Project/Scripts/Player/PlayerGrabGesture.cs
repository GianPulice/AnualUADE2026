using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// The player reaches out with the arm nearest to what they take (a pickup, an item out of a reward
/// box), use (a door) or climb into (a locker). Push boxes are left out on purpose: they have their
/// own pushing animation. One job: listen to <see cref="InteractionEvents.OnInteractedAt"/> and play
/// the gesture.
///
/// THE ARM GOES TO THE OBJECT, THE WAY THE CLIP DID. The point is where the crosshair's probe hit the
/// target, read at the press (a pickup is gone a frame later). The arm is chosen by distance from each
/// shoulder to that point, with a bias that gives the right hand the tie
/// (<see cref="SO_GrabGestureConfig.RightArmBias"/>).
///
/// The motion is the old clip's, measured and stored in the config: every frame the hand is put on the
/// clip's own path (a lift, a swing out, a short hold, a quick return, mirrored for the right arm),
/// plus the way from that path to the object, by how far into the reach it is — so at the peak the hand
/// is on the object and everywhere else it is still on the clip's arc, not on a straight line.
/// <see cref="ArmReachIK"/> then bends the arm to that hand and the clip's elbow, in LateUpdate, over
/// the pose the Animator has just left. An object past the arm's length is reached toward with the arm
/// stretched, which is how a door out of reach still looks grabbed.
///
/// THE ARM CAN NEVER GO BEHIND THE BODY. The direction to the object is clamped to
/// <see cref="SO_GrabGestureConfig.MaxAngleFromBody"/> either side of where the body faces, so there
/// is no place for the hand to go but in front; and once the object is past that angle (walking
/// through the door being opened, turning away), the whole reach fades out and hands the arm back to
/// the walking pose instead of dragging it round.
///
/// Each arm has its own progress and its own task, so a second grab with the other hand lets the first
/// arm finish coming back instead of snapping.
///
/// FALLBACK. If the rig's arm bones are not found, or the config turns the reach off, the old fixed
/// clip plays on the "Grab Arm" layer (left arm only, masked by what the clip animates). Its weight
/// starts at 0 and is faded in, held and faded out here; the clip starts and ends in the rig's bind
/// pose, not in the idle one, so the fades are what hide that.
/// </summary>
public class PlayerGrabGesture : MonoBehaviour
{
    [SerializeField] private SO_GrabGestureConfig config;
    [SerializeField] private PlayerStateManager player;

    private Animator animator;

    // ── Fallback clip ───────────────────────────────────────────────────────
    private int layer = -1;
    private int stateHash;
    private CancellationTokenSource gestureCts;

    // ── Reach (IK) ──────────────────────────────────────────────────────────
    private sealed class Arm
    {
        public bool IsLeft;
        public Transform Upper, Fore, Hand;
        public Vector3 Target;
        public bool Active;
        public float Progress;   // 0 to 1 over the reach's seconds
        public CancellationTokenSource Cts;

        public bool IsComplete => Upper != null && Fore != null && Hand != null;
    }

    private readonly Arm rightArm = new Arm { IsLeft = false };
    private readonly Arm leftArm = new Arm { IsLeft = true };
    private bool bonesSearched;

    private void Awake()
    {
        if (player == null) player = GetComponentInParent<PlayerStateManager>();
        InteractionEvents.OnInteractedAt += HandleInteracted;
    }

    private void OnDestroy()
    {
        InteractionEvents.OnInteractedAt -= HandleInteracted;
        Cancel();
        CancelReach(rightArm);
        CancelReach(leftArm);
    }

    private void OnDisable()
    {
        // A disabled player has no animation to blend into: leave the arms as the Animator has them.
        CancelReach(rightArm);
        CancelReach(leftArm);
        rightArm.Active = false;
        leftArm.Active = false;
    }

    private void HandleInteracted(IInteractable interactable, Vector3? point)
    {
        if (!IsGrab(interactable)) return;

        if (TryStartReach(interactable, point)) return;

        if (!Resolve()) return;

        Cancel();
        gestureCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        PlayAsync(gestureCts.Token).Forget();
    }

    // A locker only on the way in: getting out is E from inside it, with nothing in front to reach for.
    private static bool IsGrab(IInteractable interactable) =>
        interactable is PickupInteractable ||
        interactable is DoorInteractable ||
        interactable is PuzzleRewardBox ||
        (interactable is HidingSpot spot && !spot.IsOccupied);

    // ── Reach: the arm goes to the spot ─────────────────────────────────────

    private bool TryStartReach(IInteractable interactable, Vector3? point)
    {
        if (config == null || !config.ReachToObject || player == null) return false;
        if (animator == null) animator = player.AnimController;
        if (animator == null) return false;
        if (!ResolveBones()) return false;

        Vector3 target = point ?? FallbackPoint(interactable);
        if (float.IsNaN(target.x) || float.IsNaN(target.y) || float.IsNaN(target.z)) return false;

        // The nearest arm; the right one keeps ties and anything within the bias.
        float toRight = (target - rightArm.Upper.position).magnitude;
        float toLeft = (target - leftArm.Upper.position).magnitude;
        Arm arm = toLeft + config.RightArmBias < toRight ? leftArm : rightArm;

        // A second grab with this arm while it is still out does not start from the beginning (the
        // hand would snap back to the hip): it goes on from where it is, no later than the peak.
        float startAt = arm.Active ? Mathf.Min(arm.Progress, PeakProgress()) : 0f;

        arm.Target = target;
        CancelReach(arm);
        arm.Cts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        ReachAsync(arm, startAt, arm.Cts.Token).Forget();
        return true;
    }

    // When the target was not picked by the crosshair (it has no hit point): the object itself.
    private static Vector3 FallbackPoint(IInteractable interactable)
    {
        if (interactable is Component component && component != null)
        {
            Collider collider = component.GetComponentInChildren<Collider>();
            return collider != null ? collider.bounds.center : component.transform.position;
        }
        return new Vector3(float.NaN, float.NaN, float.NaN);
    }

    // Looked up once, by name, under the Animator: the prefab needs no references for this.
    private bool ResolveBones()
    {
        if (!bonesSearched)
        {
            bonesSearched = true;
            string prefix = config.BonePrefix ?? string.Empty;
            FindBones(rightArm, prefix + "Right");
            FindBones(leftArm, prefix + "Left");

            if (!rightArm.IsComplete || !leftArm.IsComplete)
            {
                Debug.LogWarning($"[{nameof(PlayerGrabGesture)}] Arm bones '{prefix}Right/LeftArm', " +
                                 $"'...ForeArm' and '...Hand' not found under '{animator.name}': the " +
                                 "grab gesture falls back to the fixed clip.", this);
            }
        }

        return rightArm.IsComplete && leftArm.IsComplete;
    }

    private void FindBones(Arm arm, string side)
    {
        Transform root = animator.transform;
        arm.Upper = FindDeep(root, side + "Arm");
        arm.Fore = FindDeep(root, side + "ForeArm");
        arm.Hand = FindDeep(root, side + "Hand");
    }

    private static Transform FindDeep(Transform parent, string boneName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == boneName) return child;

            Transform deeper = FindDeep(child, boneName);
            if (deeper != null) return deeper;
        }
        return null;
    }

    private void CancelReach(Arm arm)
    {
        if (arm.Cts == null) return;
        arm.Cts.Cancel();
        arm.Cts.Dispose();
        arm.Cts = null;
    }

    // Progress runs 0 to 1 over the reach's seconds, at the clip's own speed: the path and the profile
    // are indexed by it, so the arm moves at the clip's rhythm.
    private async UniTaskVoid ReachAsync(Arm arm, float startAt, CancellationToken token)
    {
        float seconds = Mathf.Max(0.1f, config.ReachSeconds);
        arm.Active = true;
        arm.Progress = startAt;

        try
        {
            while (arm.Progress < 1f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                arm.Progress += Time.deltaTime / seconds;
            }
        }
        catch (OperationCanceledException)
        {
            // A new grab with this arm took over and goes on from this progress.
            return;
        }

        arm.Active = false;
        arm.Progress = 0f;
    }

    // After the Animator, so the bones are in this frame's pose when they are bent.
    private void LateUpdate()
    {
        ApplyReach(rightArm);
        ApplyReach(leftArm);
    }

    private void ApplyReach(Arm arm)
    {
        if (!arm.Active || !arm.IsComplete || config == null || animator == null) return;

        ArmReach.Apply(animator.transform, arm.Upper, arm.Fore, arm.Hand, arm.IsLeft,
                       arm.Target, arm.Progress, PeakProgress(), config);
    }

    // Where the profile peaks: the moment the hand is on the object.
    private float PeakProgress() => ArmReach.PeakProgress(config.ReachProfile);

    // ── Fallback: the fixed clip ────────────────────────────────────────────

    private bool Resolve()
    {
        if (config == null || player == null) return false;
        if (animator == null) animator = player.AnimController;
        if (animator == null) return false;
        if (layer < 0)
        {
            layer = animator.GetLayerIndex(config.LayerName);
            stateHash = Animator.StringToHash(config.StateName);
            if (layer < 0 || !animator.HasState(layer, stateHash))
            {
                Debug.LogWarning($"[{nameof(PlayerGrabGesture)}] No layer '{config.LayerName}' with state " +
                                 $"'{config.StateName}' in the player's Animator: no grab gesture.", this);
                layer = -1;
                return false;
            }
            animator.SetLayerWeight(layer, 0f);
        }
        return true;
    }

    private void Cancel()
    {
        if (gestureCts == null) return;
        gestureCts.Cancel();
        gestureCts.Dispose();
        gestureCts = null;
    }

    private async UniTaskVoid PlayAsync(CancellationToken token)
    {
        float startWeight = animator.GetLayerWeight(layer);
        animator.Play(stateHash, layer, 0f);
        animator.SetFloat(SpeedParam, config.Speed);
        try
        {
            // How long the gesture lasts on screen: the clip's own length over the state's speed (the
            // config's, through the grabSpeed parameter), read once the state is playing.
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
            AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(layer);
            float clipLength = clips.Length > 0 && clips[0].clip != null ? clips[0].clip.length : state.length;
            float rate = Mathf.Abs(state.speed * state.speedMultiplier);
            float length = Mathf.Max(0.05f, rate > 0.001f ? clipLength / rate : clipLength);

            float fadeIn = Mathf.Min(config.FadeInSeconds, length * 0.5f);
            float fadeOut = Mathf.Min(config.FadeOutSeconds, length * 0.5f);

            await FadeWeight(startWeight, 1f, fadeIn, token);
            float hold = length - fadeIn - fadeOut;
            if (hold > 0f) await UniTask.Delay(TimeSpan.FromSeconds(hold), cancellationToken: token);
            await FadeWeight(1f, 0f, fadeOut, token);
        }
        catch (OperationCanceledException)
        {
            // A new gesture took over and fades in from wherever this one was left.
            return;
        }
        animator.SetLayerWeight(layer, 0f);
    }

    private static readonly int SpeedParam = Animator.StringToHash("grabSpeed");

    private async UniTask FadeWeight(float from, float to, float seconds, CancellationToken token)
    {
        if (seconds <= 0f) { animator.SetLayerWeight(layer, to); return; }
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            // Smoothstep: eases in and out instead of a linear ramp.
            float k = t / seconds;
            animator.SetLayerWeight(layer, Mathf.Lerp(from, to, k * k * (3f - 2f * k)));
            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }
        animator.SetLayerWeight(layer, to);
    }
}
