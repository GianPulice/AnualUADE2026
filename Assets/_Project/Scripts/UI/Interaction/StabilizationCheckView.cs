using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// View of the stabilization checks (Central Puzzle 2 — Ventilation Hub): a dial with a ring as the
/// track, the success zone as an arc over it and a needle turning around, plus a status line, a
/// "2/4" counter and one pip per check. It only draws: the controller says what to show, with
/// angles already in clock degrees (0 = twelve o'clock, clockwise), the convention UIRingArc draws in.
///
/// Colours are theme tokens read here. Feedback tweens run on unscaled time, like every UI animation.
/// </summary>
public class StabilizationCheckView : BaseScreenView
{
    private const float ShakeCycles = 3f;

    [Header("Theme")]
    [SerializeField] private SO_UIThemeConfig theme;

    [Header("Dial")]
    [Tooltip("Shaken on a failure and pulsed on a success.")]
    [SerializeField] private RectTransform dial;
    [SerializeField] private UIRingArc track;
    [SerializeField] private UIRingArc zone;
    [Tooltip("Rotated around the dial's centre. At rest it points at twelve o'clock.")]
    [SerializeField] private RectTransform needlePivot;
    [SerializeField] private Graphic needle;

    [Header("Readouts")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text stepText;
    [SerializeField] private TMP_Text statusText;
    [Tooltip("One per check, in order. Extra pips are hidden.")]
    [SerializeField] private Graphic[] pips = new Graphic[0];

    [Header("Strings")]
    [SerializeField] private string title = "VENTILATION STABILIZATION";
    [SerializeField] private string statusPrefix = "> ";
    [SerializeField] private string standbyString = "STAND BY";
    [SerializeField] private string readyString = "PRESS [E]   ([ESC] ABORT)";
    [SerializeField] private string successString = "STABLE";
    [Tooltip("{0} = seconds taken off the module.")]
    [SerializeField] private string failString = "FAILURE  -{0:0}s";
    [SerializeField] private string completeString = "SYSTEM STABILIZED";

    [Header("Feel")]
    [SerializeField, Min(0f)] private float shakeDistance = 10f;
    [SerializeField, Min(0.02f)] private float shakeDuration = 0.3f;
    [SerializeField, Min(1f)] private float pulseScale = 1.06f;
    [SerializeField, Min(0.02f)] private float pulseDuration = 0.18f;

    private Vector2 dialRest;
    private bool dialRestCaptured;

    private void OnDestroy()
    {
        if (dial != null) LeanTween.cancel(dial.gameObject);
    }

    /// <summary>A fresh sequence: every pip pending.</summary>
    public void Setup(int totalChecks)
    {
        StopFeedback();
        if (titleText != null) titleText.text = title;
        if (track != null) track.color = Disabled;
        SetStep(0, totalChecks);
        SetPips(0, totalChecks);
        ShowStandby();
    }

    public void ShowStandby()
    {
        HideZone();
        SetNeedleVisible(false);
        SetStatus(standbyString, Muted);
    }

    /// <summary>An attempt: the zone where it landed, the needle waiting at twelve.</summary>
    public void ShowCheck(float zoneStart, float zoneDegrees, int checkIndex, int totalChecks)
    {
        StopFeedback();
        if (zone != null)
        {
            zone.color = Secondary;
            zone.SetArc(zoneStart, zoneDegrees);
        }
        SetNeedle(0f);
        SetNeedleVisible(true);
        SetStep(checkIndex, totalChecks);
        SetPips(checkIndex, totalChecks);
        SetStatus(readyString, Primary);
    }

    public void SetNeedle(float angle)
    {
        // Unity's Z rotation runs counter-clockwise; the clock runs the other way.
        if (needlePivot != null) needlePivot.localRotation = Quaternion.Euler(0f, 0f, -angle);
    }

    public void ShowSuccess(int passedChecks, int totalChecks)
    {
        if (zone != null) zone.color = Primary;
        SetStatus(successString, Primary);
        SetPips(passedChecks, totalChecks);
        Pulse();
    }

    public void ShowFailure(float penaltySeconds)
    {
        if (zone != null) zone.color = Accent;
        SetStatus(string.Format(failString, penaltySeconds), Accent);
        Shake();
    }

    public void ShowComplete(int totalChecks)
    {
        HideZone();
        SetNeedleVisible(false);
        if (track != null) track.color = Primary;
        SetStep(totalChecks, totalChecks);
        SetPips(totalChecks, totalChecks);
        SetStatus(completeString, Primary);
        Pulse();
    }

    // -- Drawing -------------------

    private void HideZone()
    {
        if (zone != null) zone.SetSweep(0f);
    }

    private void SetNeedleVisible(bool visible)
    {
        if (needle != null) needle.enabled = visible;
    }

    private void SetStep(int checkIndex, int totalChecks)
    {
        if (stepText == null) return;
        int shown = Mathf.Clamp(checkIndex + 1, 1, Mathf.Max(1, totalChecks));
        stepText.text = $"{shown}/{totalChecks}";
    }

    /// <summary>Passed checks lit, the current one dimmed, the rest off.</summary>
    private void SetPips(int passed, int totalChecks)
    {
        for (int i = 0; i < pips.Length; i++)
        {
            Graphic pip = pips[i];
            if (pip == null) continue;

            bool used = i < totalChecks;
            pip.gameObject.SetActive(used);
            if (!used) continue;

            pip.color = i < passed ? Primary : i == passed ? Muted : Disabled;
        }
    }

    private void SetStatus(string text, Color color)
    {
        if (statusText == null) return;
        statusText.text = statusPrefix + text;
        statusText.color = color;
    }

    // -- Feedback -------------------

    private void Shake()
    {
        if (!PrepareDial()) return;

        LeanTween.value(dial.gameObject, 0f, 1f, shakeDuration)
                 .setOnUpdate(t =>
                 {
                     float offset = Mathf.Sin(t * Mathf.PI * 2f * ShakeCycles) * shakeDistance * (1f - t);
                     dial.anchoredPosition = dialRest + Vector2.right * offset;
                 })
                 .setOnComplete(() => dial.anchoredPosition = dialRest)
                 .setIgnoreTimeScale(true);
    }

    private void Pulse()
    {
        if (!PrepareDial()) return;

        dial.localScale = Vector3.one * pulseScale;
        LeanTween.scale(dial.gameObject, Vector3.one, pulseDuration)
                 .setEase(LeanTweenType.easeOutQuad)
                 .setIgnoreTimeScale(true);
    }

    private void StopFeedback() => PrepareDial();

    /// <summary>Cancels any running feedback and puts the dial back at rest. The rest position is
    /// read the first time, not in Awake: the view is set up while still inactive.</summary>
    private bool PrepareDial()
    {
        if (dial == null) return false;

        if (!dialRestCaptured)
        {
            dialRest = dial.anchoredPosition;
            dialRestCaptured = true;
        }

        LeanTween.cancel(dial.gameObject);
        dial.anchoredPosition = dialRest;
        dial.localScale = Vector3.one;
        return true;
    }

    private Color Primary => theme != null ? theme.TextPrimary : Color.white;
    private Color Secondary => theme != null ? theme.TextSecondary : new Color(0.73f, 0.73f, 0.73f);
    private Color Muted => theme != null ? theme.TextMuted : Color.gray;
    private Color Disabled => theme != null ? theme.TextDisabled : new Color(0.33f, 0.33f, 0.33f);
    private Color Accent => theme != null ? theme.Accent : new Color(0.8f, 0.1f, 0.1f);
}
