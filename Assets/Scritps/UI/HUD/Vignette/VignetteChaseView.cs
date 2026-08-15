using UnityEngine;

/// <summary>
/// The "it is hunting you" vignette. Only shows while the Nemesis is actually chasing, and how
/// opaque it gets tracks how close it is: faint when the chase opens across the room, near solid
/// when it is right behind you.
///
/// Distance comes from NemesisEvents.OnProximityChanged, the same normalized 0..1 the proximity
/// vignette already runs on (0 = at or beyond SO_NemesisData.proximityRadius, 1 = on top of the
/// player), rather than from a distance measured here: this view lives in the additively loaded
/// LevelUI scene and could not hold a reference to the Nemesis or the player even if it wanted to.
/// The chase flag gates it, so proximity while merely patrolling nearby does not light this up —
/// that is the other vignette's job.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class VignetteChaseView : BaseScreenView
{
    [Tooltip("Alpha while being chased from the far edge of the Nemesis's proximityRadius (or " +
             "past it — the Nemesis keeps chasing for visionLossGracePeriod after losing sight, " +
             "and the player can outrun the radius in that window).\n\n" +
             "This is the floor, not the starting point: it is what keeps the vignette readable " +
             "during a long chase. Set it to 0 for a straight proportional ramp that fades out " +
             "completely at max distance.")]
    [SerializeField, Range(0f, 1f)] private float minAlpha = 0.15f;

    [Tooltip("Alpha with the Nemesis right on top of the player. Also what shows through the " +
             "capture itself, which counts as being chased until the respawn resolves it.")]
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.7f;

    private bool isChasing;

    /// <summary>0 = at/beyond proximityRadius, 1 = on top of the player. Only read while
    /// chasing, so a stale value from before the chase never reaches the screen.</summary>
    private float proximity;

    private float TargetAlpha => isChasing ? Mathf.Lerp(minAlpha, maxAlpha, proximity) : 0f;

    void Awake()
    {
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha = 0f;

        NemesisEvents.OnChaseStarted     += HandleChaseStarted;
        NemesisEvents.OnChaseEnded       += HandleChaseEnded;
        NemesisEvents.OnProximityChanged += HandleProximityChanged;
    }

    void OnDestroy()
    {
        NemesisEvents.OnChaseStarted     -= HandleChaseStarted;
        NemesisEvents.OnChaseEnded       -= HandleChaseEnded;
        NemesisEvents.OnProximityChanged -= HandleProximityChanged;
    }

    /// <summary>
    /// CaptureFadeView switches this object off for the whole capture and back on at the respawn
    /// point. Update does not run while it is off, so without this the vignette would come back
    /// still showing the alpha it froze at mid-capture — a red flash on a screen that is about to
    /// be revealed. Snapping is right here and not a fade: the screen is still black.
    /// </summary>
    private void OnEnable() => canvasGroup.alpha = TargetAlpha;

    private void HandleChaseStarted() => isChasing = true;
    private void HandleChaseEnded()   => isChasing = false;

    private void HandleProximityChanged(float t) => proximity = Mathf.Clamp01(t);

    /// <summary>
    /// Alpha is driven from here rather than written straight from the proximity handler so that
    /// the fade in at the start of a chase, the distance ramp during it and the fade out at the
    /// end are all one movement, capped at the same rate. Writing the target directly would pop
    /// on the frame the chase starts, and the base class's async Fade cannot be used for the ramp:
    /// it would be restarted every frame by a target that keeps moving.
    ///
    /// fadeDuration is read as "seconds to cross the full 0..1 alpha range", so the existing
    /// inspector value keeps meaning what it did for the old on/off fade.
    ///
    /// Scaled deltaTime on purpose: a pause menu freezes the game, and the Nemesis stops emitting
    /// proximity while paused anyway (NemesisStateManager.Update bails on PauseManager), so the
    /// vignette holding still is the honest reading.
    /// </summary>
    private void Update()
    {
        float target = TargetAlpha;

        if (fadeDuration <= 0f)
        {
            canvasGroup.alpha = target;
            return;
        }

        canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, target,
                                              Time.deltaTime / fadeDuration);
    }
}
