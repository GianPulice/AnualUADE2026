using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// The black cover that hides a capture. Goes opaque when the Nemesis grabs the player, stays
/// black through the player's own teleport to the checkpoint AND the Nemesis's separate grace
/// period and reposition, and only clears once <see cref="NemesisEvents.OnCaptureResolved"/> says
/// the Nemesis has actually moved away.
///
/// THE REVEAL IS NOT KEYED TO THE PLAYER'S OWN RESPAWN, AND THAT USED TO BE THE BUG. The player
/// lands at the checkpoint on CheckpointManager's timing — a second or two — but the Nemesis is
/// still standing at the capture spot for its whole CaptureGracePeriod after that (by design: it
/// is the player's mercy window). Revealing the screen the moment the PLAYER is safe said nothing
/// about the Nemesis, and the fade would already be long gone by the time it warped away — so the
/// pop was in plain sight, potentially several seconds later, sometimes with the player already
/// back near the checkpoint watching it happen. Waiting for the Nemesis's own signal instead means
/// the cover cannot lift before the thing it exists to hide is actually gone.
///
/// It does not move anybody: the respawn is CheckpointManager's job and the reposition is the
/// Nemesis's own, both on their own timing. This view only covers the screen while both run.
///
/// Purely reactive by necessity — it lives in the additively loaded LevelUI scene and could not
/// hold an inspector reference to the Nemesis, the player or the CheckpointManager even if it
/// wanted to. Same shape as its two siblings under this HUD canvas (VignetteChaseView /
/// VignetteProximityView): subscribe in Awake, unsubscribe in OnDestroy.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class CaptureFadeView : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Timing")]
    [Tooltip("Seconds to go from clear to fully black once the Nemesis grabs the player.\n\n" +
             "Must stay BELOW CheckpointManager.captureCutsceneDelay (1.5s in " +
             "WIRED_Zona1_Blockout) — that is how long the manager waits before teleporting. If " +
             "this is longer the screen is still see-through when the move happens and the " +
             "player watches itself being dragged to the checkpoint.")]
    [SerializeField, Min(0f)] private float fadeInDuration = 0.6f;

    [Tooltip("Seconds to hold the screen fully black after the Nemesis has finished repositioning " +
             "(NemesisEvents.OnCaptureResolved), before revealing. Reads as a beat instead of a " +
             "hard cut, and gives the warp a frame to settle before anything is visible.")]
    [SerializeField, Min(0f)] private float blackHoldDuration = 0.4f;

    [Tooltip("Seconds to go from black back to clear at the respawn point.")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.8f;

    [Header("Overlays this one takes over from")]
    [Tooltip("Switched off for the whole capture and switched back on at the respawn point — " +
             "the chase and proximity vignettes under this same HUD canvas.\n\n" +
             "Auto-filled with every sibling BaseScreenView when the component is added. Clear " +
             "an entry to leave that overlay running through the capture.")]
    [SerializeField] private List<GameObject> overlaysHiddenDuringCapture = new List<GameObject>();

    private CancellationTokenSource fadeCts;

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        CollectSiblingOverlays();
    }

    /// <summary>
    /// Picks up the sibling overlays under this HUD canvas (VignetteChaseView,
    /// VignetteProximityView). They all derive from BaseScreenView and this component does not,
    /// so there is no need to filter this object out of the result.
    /// </summary>
    private void CollectSiblingOverlays()
    {
        overlaysHiddenDuringCapture.Clear();
        if (transform.parent == null) return;

        // Include inactive: an overlay that happens to be off in the prefab still has to come
        // back under this component's control rather than be dropped from the list.
        BaseScreenView[] siblings = transform.parent.GetComponentsInChildren<BaseScreenView>(true);
        for (int i = 0; i < siblings.Length; i++)
        {
            if (siblings[i] != null) overlaysHiddenDuringCapture.Add(siblings[i].gameObject);
        }
    }

    /// <summary>
    /// Turns the other overlays off while the black cover owns the screen, and back on once the
    /// player is at the respawn point.
    ///
    /// SetActive and not <c>enabled = false</c> on purpose: both vignettes subscribe to
    /// NemesisEvents in Awake and unsubscribe in OnDestroy, so a merely disabled component still
    /// gets its handler called and would keep writing its own alpha straight over this cover —
    /// the proximity one does it every single frame. Deactivating the object is what actually
    /// stops it from drawing. The handlers keep running against an inactive object, which is
    /// harmless and means each overlay is already showing the right value for the new position
    /// by the time it is switched back on.
    /// </summary>
    private void SetOverlaysActive(bool active)
    {
        for (int i = 0; i < overlaysHiddenDuringCapture.Count; i++)
        {
            GameObject overlay = overlaysHiddenDuringCapture[i];
            if (overlay != null) overlay.SetActive(active);
        }
    }

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

        // Never a raycast target. The prefab ships with blocksRaycasts on, and a full-screen
        // group swallowing clicks for the whole capture would be a bug, not a feature.
        canvasGroup.interactable   = false;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha          = 0f;

        // Awake/OnDestroy and not OnEnable/OnDisable, same as the vignette views: this object is
        // never toggled off, and OnEnable would silently stop working the day someone does.
        PlayerEvents.OnPlayerCaptured    += HandlePlayerCaptured;
        NemesisEvents.OnCaptureResolved  += HandleCaptureResolved;
        GameResultManager.OnGameResult   += HandleGameResult;
    }

    private void OnDestroy()
    {
        PlayerEvents.OnPlayerCaptured    -= HandlePlayerCaptured;
        NemesisEvents.OnCaptureResolved  -= HandleCaptureResolved;
        GameResultManager.OnGameResult   -= HandleGameResult;

        fadeCts?.Cancel();
        fadeCts?.Dispose();
    }

    /// <summary>
    /// Raised by PlayerStateManager.OnCaptured(), which NemesisCatchState calls the moment it
    /// reaches the player. Cover the screen before the respawn runs, and drop the other overlays
    /// once it is fully black — cutting them at the start instead would make the red chase
    /// vignette vanish in plain sight, on a screen that is still mostly see-through.
    /// </summary>
    private void HandlePlayerCaptured(PlayerStateManager player) =>
        FadeTo(1f, fadeInDuration, 0f, afterFade: () => SetOverlaysActive(false)).Forget();

    /// <summary>
    /// The Nemesis has finished repositioning after the capture — the player already landed at
    /// the checkpoint well before this, and has been waiting behind the black screen the whole
    /// time the Nemesis stood there running out its grace period. Only now is it gone from where
    /// it grabbed the player, so only now is it safe to lift the cover.
    ///
    /// The overlays come back while the screen is still black, so neither is seen popping in.
    /// </summary>
    private void HandleCaptureResolved() =>
        FadeTo(0f, fadeOutDuration, blackHoldDuration, beforeFade: () => SetOverlaysActive(true)).Forget();

    /// <summary>
    /// The run ended instead of respawning: CheckpointManager had nowhere to send the player and
    /// fell back to the defeat screen, so OnRespawned never fires. Without this the cover would
    /// stay black for good — and since CanvasResult sorts at the same order as this HUD canvas,
    /// whether the result screen draws over it comes down to hierarchy order. Clearing is what
    /// guarantees it is readable.
    /// </summary>
    private void HandleGameResult(GameResultModel result) =>
        FadeTo(0f, fadeOutDuration, 0f, beforeFade: () => SetOverlaysActive(true)).Forget();

    /// <summary>
    /// Fades the group to <paramref name="targetAlpha"/>, optionally after holding for
    /// <paramref name="delay"/> seconds first. <paramref name="beforeFade"/> runs once the hold
    /// is over and <paramref name="afterFade"/> once the target alpha is reached; both are
    /// skipped if a newer fade cancels this one first, which is what keeps the overlays from
    /// being switched back on by a reveal that never happened.
    ///
    /// Unscaled throughout, on purpose. CheckpointManager runs its whole capture sequence on
    /// unscaled time, so a pause menu opened mid-capture must not freeze this cover while the
    /// teleport underneath it keeps counting down — that is precisely the case where the player
    /// would end up seeing the move.
    /// </summary>
    private async UniTaskVoid FadeTo(float targetAlpha, float duration, float delay,
                                     Action beforeFade = null, Action afterFade = null)
    {
        // A second capture, or a result landing on top of a pending reveal, has to win outright
        // rather than blend with whatever is still running.
        fadeCts?.Cancel();
        fadeCts?.Dispose();
        fadeCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

        CancellationToken token = fadeCts.Token;

        try
        {
            if (delay > 0f)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(delay),
                                    DelayType.UnscaledDeltaTime,
                                    cancellationToken: token);
            }

            beforeFade?.Invoke();

            if (duration <= 0f)
            {
                canvasGroup.alpha = targetAlpha;
                afterFade?.Invoke();
                return;
            }

            float startAlpha = canvasGroup.alpha;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            canvasGroup.alpha = targetAlpha;
            afterFade?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer fade, or the scene unloaded mid-way. Either way the alpha now
            // belongs to whoever cancelled us — leave it exactly where it is.
        }
    }
}
