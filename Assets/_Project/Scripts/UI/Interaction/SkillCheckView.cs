using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// View of the skill check (Central Puzzle 2 — Ventilation Hub): a Win95 window with a dial inside —
/// a block ring as the track, the success zone and its perfect slice as arcs over it, and a needle
/// that sweeps one lap from twelve o'clock with a fading trail behind it. Under the dial, a status
/// line and one pip per check.
///
/// It only draws. It does not know the data asset, the timing or the input: the controller tells it
/// what to show, and angles arrive already in clock degrees (0 = twelve o'clock, clockwise), the
/// convention <see cref="UIRingArc"/> draws in.
///
/// Colours are theme tokens, read here instead of through a <see cref="UIThemeApplier"/> on the nodes
/// that change at runtime: an applier would repaint them on every enable. The one exception is the
/// amber of the perfect slice, which is the module timer's colour, since a perfect gives time back
/// to that timer. Feedback tweens run on unscaled time, like every other UI animation.
/// </summary>
public class SkillCheckView : BaseScreenView
{
    private const float ShakeCycles = 3f;

    [Header("Theme")]
    [SerializeField] private SO_UIThemeConfig theme;

    [Header("Dial")]
    [Tooltip("Shaken on a miss and pulsed on a hit. Its own node, so the feedback never fights the " +
             "window's signal transition.")]
    [SerializeField] private RectTransform dial;
    [SerializeField] private UIRingArc track;
    [SerializeField] private UIRingArc zone;
    [SerializeField] private UIRingArc perfectZone;
    [Tooltip("Rotated around the dial's centre. At rest it points at twelve o'clock.")]
    [SerializeField] private RectTransform needlePivot;
    [SerializeField] private Graphic needle;
    [Tooltip("Arc that follows the needle, fading out behind it (its own UIRingArc fade).")]
    [SerializeField] private UIRingArc needleTrail;
    [SerializeField, Range(0f, 180f)] private float trailDegrees = 50f;

    [Header("Readouts")]
    [Tooltip("\"2/4\" inside the ring: the check being played over the total.")]
    [SerializeField] private TMP_Text stepText;
    [SerializeField] private TMP_Text statusText;
    [Tooltip("One per check, in order. Extra pips are hidden.")]
    [SerializeField] private Graphic[] pips = new Graphic[0];

    [Header("Colours")]
    [Tooltip("Perfect slice and the PERFECT result. The module timer's amber.")]
    [SerializeField] private Color perfectColor = new Color(1f, 0.6f, 0f);

    [Header("Strings")]
    [SerializeField] private string statusPrefix = "> ";
    [SerializeField] private string standbyString = "STAND BY";
    [SerializeField] private string readyString = "PRESS [E]";
    [SerializeField] private string goodString = "GOOD";
    [SerializeField] private string perfectString = "PERFECT";
    [SerializeField] private string missString = "MISS";
    [SerializeField] private string completeString = "SYSTEM STABILIZED";
    [SerializeField] private string failedString = "SEQUENCE FAILED - RESTART";

    [Header("Feel")]
    [SerializeField, Min(0f)] private float shakeDistance = 10f;
    [SerializeField, Min(0.02f)] private float shakeDuration = 0.3f;
    [SerializeField, Min(1f)] private float pulseScale = UITweenDefaults.FeedbackOvershoot;
    [SerializeField, Min(0.02f)] private float pulseDuration = 0.18f;

    private Vector2 dialRest;
    private bool dialRestCaptured;

    private void OnDestroy()
    {
        if (dial != null) LeanTween.cancel(dial.gameObject);
    }

    // -- Public API (called by the controller) -------------------

    /// <summary>A fresh sequence: every pip pending, the dial empty.</summary>
    public void Setup(int totalSteps)
    {
        StopFeedback();
        System.Array.Clear(pipResults, 0, pipResults.Length);
        if (track != null) track.color = Disabled;
        SetStep(0, totalSteps);
        SetPips(0, totalSteps);
        ShowStandby();
    }

    /// <summary>Between checks: the ring stays, the zone and the needle go.</summary>
    public void ShowStandby()
    {
        HideZone();
        SetNeedleVisible(false);
        SetStatus(standbyString, Muted);
    }

    /// <summary>The warning: the zone pops up where it landed and the needle waits at twelve.</summary>
    public void ShowCheck(float zoneStart, float zoneWidth, float perfectWidth, int stepIndex, int totalSteps)
    {
        StopFeedback();

        if (zone != null)
        {
            zone.color = Secondary;
            zone.SetArc(zoneStart, zoneWidth);
        }
        if (perfectZone != null)
        {
            perfectZone.color = perfectColor;
            perfectZone.SetArc(zoneStart, perfectWidth);
        }

        SetNeedle(0f);
        SetNeedleVisible(true);
        SetStep(stepIndex, totalSteps);
        SetPips(stepIndex, totalSteps);
        SetStatus(readyString, Primary);
    }

    public void SetNeedle(float angle)
    {
        // Unity's Z rotation runs counter-clockwise; the clock runs the other way.
        if (needlePivot != null) needlePivot.localRotation = Quaternion.Euler(0f, 0f, -angle);

        // The trail never reaches back past twelve o'clock: the lap starts there.
        float trail = Mathf.Min(trailDegrees, angle);
        if (needleTrail != null) needleTrail.SetArc(angle - trail, trail);
    }

    /// <summary>Holds the verdict on screen: the zone takes its colour, the dial shakes or pulses.</summary>
    public void ShowResult(SkillCheckResult result, int playedIndex, int totalSteps)
    {
        Color color = result switch
        {
            SkillCheckResult.Perfect => perfectColor,
            SkillCheckResult.Good => Primary,
            _ => Accent
        };

        if (zone != null) zone.color = result == SkillCheckResult.Perfect ? Primary : color;
        if (perfectZone != null) perfectZone.color = color;

        SetStatus(result switch
        {
            SkillCheckResult.Perfect => perfectString,
            SkillCheckResult.Good => goodString,
            _ => missString
        }, color);
        if (playedIndex >= 0 && playedIndex < pipResults.Length)
            pipResults[playedIndex] = result == SkillCheckResult.Miss ? PipMiss : PipHit;
        SetPips(playedIndex + 1, totalSteps);

        if (result == SkillCheckResult.Miss) Shake();
        else Pulse();
    }

    /// <summary>The round ended with a miss in it: the red pips stay up while the failure holds.</summary>
    public void ShowFailed(int totalSteps)
    {
        HideZone();
        SetNeedleVisible(false);
        if (track != null) track.color = Accent;
        SetPips(totalSteps, totalSteps);
        SetStatus(failedString, Accent);
        Shake();
    }

    /// <summary>Every check passed: the whole ring lights up.</summary>
    public void ShowComplete(int totalSteps)
    {
        HideZone();
        SetNeedleVisible(false);
        if (track != null) track.color = Primary;
        SetStep(totalSteps, totalSteps);
        SetPips(totalSteps, totalSteps);
        SetStatus(completeString, Primary);
        Pulse();
    }

    // -- Drawing -------------------

    private void HideZone()
    {
        if (zone != null) zone.SetSweep(0f);
        if (perfectZone != null) perfectZone.SetSweep(0f);
    }

    private void SetNeedleVisible(bool visible)
    {
        if (needle != null) needle.enabled = visible;
        if (needleTrail != null) needleTrail.enabled = visible;
    }

    private void SetStep(int stepIndex, int totalSteps)
    {
        if (stepText == null) return;
        int shown = Mathf.Clamp(stepIndex + 1, 1, Mathf.Max(1, totalSteps));
        stepText.text = $"{shown}/{totalSteps}";
    }

    private const byte PipNone = 0, PipHit = 1, PipMiss = 2;

    // What each check of the current round came out as. Sized for any pip count the prefab has.
    private byte[] pipResults = new byte[32];

    /// <summary>Played checks lit (hit) or red (miss), the current one dimmed, the rest off.</summary>
    private void SetPips(int stepsDone, int totalSteps)
    {
        for (int i = 0; i < pips.Length; i++)
        {
            Graphic pip = pips[i];
            if (pip == null) continue;

            bool used = i < totalSteps;
            pip.gameObject.SetActive(used);
            if (!used) continue;

            byte played = i < pipResults.Length ? pipResults[i] : PipNone;
            pip.color = i < stepsDone
                ? (played == PipMiss ? Accent : Primary)
                : i == stepsDone ? Muted : Disabled;
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
