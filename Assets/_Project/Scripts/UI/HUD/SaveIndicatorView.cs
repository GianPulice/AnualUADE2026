using TMPro;
using UnityEngine;

/// <summary>
/// The "SAVING" corner badge: a spinning block ring and a label, bottom-right, shown every time the
/// run is written to its save slot (<see cref="SaveGame.OnSlotSaved"/>).
///
/// The write itself takes a few milliseconds, far too short to read, so the badge stays up for
/// <see cref="visibleSeconds"/> and then fades out. A second save while it is up restarts the clock.
///
/// It runs on unscaled time: a save can land while the game is paused or slowed (the end of a
/// cinematic), and the badge must still spin and go away.
///
/// SETUP: the CanvasSaveIndicator prefab, one instance in the LevelUI scene. It has its own canvas,
/// above the HUD, so whatever hides the HUD (cinematics, the inventory) does not hide a save. Keep
/// the GameObject active: the badge hides itself through the CanvasGroup.
/// </summary>
public class SaveIndicatorView : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;

    [Tooltip("Rotated in steps, like an old OS busy cursor. Usually a UIRingArc with blocks.")]
    [SerializeField] private RectTransform spinner;

    [SerializeField] private TMP_Text label;

    [Header("Text")]
    [SerializeField] private string savingText = "SAVING";

    [Tooltip("Dots appended to the text, one more every Dot Interval, then back to none.")]
    [SerializeField, Range(0, 5)] private int maxDots = 3;

    [SerializeField, Min(0.05f)] private float dotInterval = 0.35f;

    [Header("Timing (real seconds)")]
    [Tooltip("How long the badge stays fully visible after a save.")]
    [SerializeField, Min(0.1f)] private float visibleSeconds = 2.5f;

    [SerializeField, Min(0f)] private float fadeInSeconds = 0.2f;
    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.5f;

    [Header("Spinner")]
    [Tooltip("Degrees per step. Matching the ring's block size makes the blocks hop cell to cell.")]
    [SerializeField] private float stepDegrees = 30f;

    [SerializeField, Min(0.01f)] private float stepInterval = 0.08f;

    // Seconds since the badge was asked to show; negative = hidden.
    private float elapsed = -1f;
    private float spinTimer;
    private int dots;
    private float dotTimer;

    // Awake/OnDestroy and not OnEnable/OnDisable: SaveGame's event is static, and a save that lands
    // while this object happens to be disabled must not be missed (same rule as the HUD toasts).
    private void Awake()
    {
        SaveGame.OnSlotSaved += HandleSlotSaved;
        Hide();
    }

    private void OnDestroy() => SaveGame.OnSlotSaved -= HandleSlotSaved;

    private void HandleSlotSaved(int slotIndex) => Show();

    /// <summary>Shows the badge for its full time, or restarts that time if it is already up.</summary>
    [ContextMenu("Show (preview)")]
    public void Show()
    {
        // Already fading in: keep its progress so a double save does not flicker.
        if (elapsed < 0f || elapsed > fadeInSeconds) elapsed = elapsed < 0f ? 0f : fadeInSeconds;

        dots = 0;
        dotTimer = 0f;
        RefreshLabel();
    }

    public void Hide()
    {
        elapsed = -1f;
        if (group != null) group.alpha = 0f;
    }

    private void Update()
    {
        if (elapsed < 0f) return;

        float dt = Time.unscaledDeltaTime;
        elapsed += dt;

        if (group != null) group.alpha = AlphaAt(elapsed);

        if (elapsed >= fadeInSeconds + visibleSeconds + fadeOutSeconds)
        {
            Hide();
            return;
        }

        Spin(dt);
        AnimateDots(dt);
    }

    private float AlphaAt(float t)
    {
        if (t < fadeInSeconds) return fadeInSeconds <= 0f ? 1f : t / fadeInSeconds;

        float fadeOutStart = fadeInSeconds + visibleSeconds;
        if (t < fadeOutStart) return 1f;

        return fadeOutSeconds <= 0f ? 0f : 1f - Mathf.Clamp01((t - fadeOutStart) / fadeOutSeconds);
    }

    private void Spin(float dt)
    {
        if (spinner == null) return;

        spinTimer += dt;
        while (spinTimer >= stepInterval)
        {
            spinTimer -= stepInterval;
            // Clockwise, like the ring's own angles.
            spinner.localRotation *= Quaternion.Euler(0f, 0f, -stepDegrees);
        }
    }

    private void AnimateDots(float dt)
    {
        if (maxDots <= 0) return;

        dotTimer += dt;
        if (dotTimer < dotInterval) return;

        dotTimer -= dotInterval;
        dots = (dots + 1) % (maxDots + 1);
        RefreshLabel();
    }

    private void RefreshLabel()
    {
        if (label == null) return;
        // Padded to the longest so the text does not shift as the dots change.
        label.text = savingText + new string('.', dots) + new string(' ', Mathf.Max(0, maxDots - dots));
    }
}
