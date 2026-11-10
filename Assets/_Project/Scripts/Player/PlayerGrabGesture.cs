using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// The player reaches out with the left arm when they take something (a pickup, an item out of a
/// reward box) or use a door. Push boxes are left out on purpose: they have their own pushing
/// animation. One job: listen to <see cref="InteractionEvents.OnInteracted"/> and play the gesture.
///
/// The clip lives on its own Animator layer, masked to the left arm, so the legs keep walking. The
/// layer's weight starts at 0 and is faded in, held and faded out here; the clip starts and ends
/// in the rig's bind pose, not in the idle one, so the fades are what hide that.
/// </summary>
public class PlayerGrabGesture : MonoBehaviour
{
    [SerializeField] private SO_GrabGestureConfig config;
    [SerializeField] private PlayerStateManager player;

    private Animator animator;
    private int layer = -1;
    private int stateHash;
    private CancellationTokenSource gestureCts;

    private void Awake()
    {
        if (player == null) player = GetComponentInParent<PlayerStateManager>();
        InteractionEvents.OnInteracted += HandleInteracted;
    }

    private void OnDestroy()
    {
        InteractionEvents.OnInteracted -= HandleInteracted;
        Cancel();
    }

    private void HandleInteracted(IInteractable interactable)
    {
        if (!IsGrab(interactable)) return;
        if (!Resolve()) return;

        Cancel();
        gestureCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        PlayAsync(gestureCts.Token).Forget();
    }

    private static bool IsGrab(IInteractable interactable) =>
        interactable is PickupInteractable || interactable is DoorInteractable || interactable is PuzzleRewardBox;

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
