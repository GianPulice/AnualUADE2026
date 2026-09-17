using TMPro;
using UnityEngine;

/// <summary>
/// The help console at the top centre of the screen: a small panel with the terminal background at
/// 75% and no frame, that opens downwards, types "USE [WASD] TO MOVE" out with a blinking cursor,
/// and fades after a few seconds.
///
/// Rules:
///   - It fades after the hint's seconds (5 by default), or shortly after the player does what it
///     asks (<see cref="InputHint.dismissKeys"/> / <see cref="InputHint.dismissOnMove"/>).
///   - A different hint arriving replaces it at once; the same hint again is ignored.
///   - The countdown stops while a menu is open or the game is paused, and a hint raised under a
///     menu waits for it to close. The root's ModalVisibilityGate hides it under menus.
///
/// Separate from the interaction prompt on purpose: that one describes what the crosshair is on;
/// this one teaches a control, once per run.
/// </summary>
public class InputHintView : MonoBehaviour
{
    [Header("Nodes")]
    [Tooltip("The console panel. Opens by scaling Y from its pivot, so keep the pivot on the top edge.")]
    [SerializeField] private RectTransform panel;
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private TMP_Text label;
    [SerializeField] private TMPTypewriterReveal typewriter;

    [Header("Text")]
    [Tooltip("{0} = the keys, already bracketed and coloured. {1} = the action.")]
    [SerializeField] private string format = "> USE {0} TO {1}";
    [SerializeField] private bool uppercase = true;
    [SerializeField] private SO_UIThemeConfig theme;
    [SerializeField] private UIThemeRole keyRole = UIThemeRole.Accent;
    [SerializeField] private string cursorCharacter = "_";
    [SerializeField, Min(0.05f)] private float cursorBlinkInterval = 0.5f;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float openDuration = 0.14f;
    [Tooltip("How long the fade-out takes.")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.6f;
    [Tooltip("Fade-out when a different hint replaces this one. Short: the new one should not wait.")]
    [SerializeField, Min(0f)] private float replaceFadeDuration = 0.12f;
    [Tooltip("Seconds the hint lingers after the player does what it asks.")]
    [SerializeField, Min(0f)] private float afterInputSeconds = 0.8f;

    private InputHint current;
    private InputHint pending;
    private float remaining;
    private bool closing;

    private string body = string.Empty;
    private bool cursorOn = true;
    private float cursorTimer;

    private void Awake()
    {
        if (panelGroup != null) panelGroup.alpha = 0f;
        if (panel != null) panel.localScale = new Vector3(1f, 0f, 1f);
        if (label != null) label.text = string.Empty;

        InputHintEvents.OnHint += HandleHint;
        UIStateManager.OnModalPopped += HandleModalPopped;
    }

    private void OnDestroy()
    {
        InputHintEvents.OnHint -= HandleHint;
        UIStateManager.OnModalPopped -= HandleModalPopped;

        if (panel != null) LeanTween.cancel(panel.gameObject);
        if (panelGroup != null) LeanTween.cancel(panelGroup.gameObject);
    }

    private static bool IsBlocked => PauseManager.IsGameplayInputBlocked;

    private void HandleHint(InputHint hint)
    {
        if (current != null && !closing && current.Id == hint.Id) return;

        if (IsBlocked)
        {
            pending = hint;
            return;
        }

        Open(hint);
    }

    private void HandleModalPopped(IModalUI _)
    {
        if (pending == null || IsBlocked) return;

        InputHint next = pending;
        pending = null;
        Open(next);
    }

    private void Update()
    {
        // The pause menu is not always a modal, so a hint held back by it is released here too.
        if (pending != null && !IsBlocked) HandleModalPopped(null);

        if (current == null) return;

        TickCursor();

        if (closing || IsBlocked) return;

        if (PlayerDidIt(current)) remaining = Mathf.Min(remaining, afterInputSeconds);

        remaining -= Time.unscaledDeltaTime;
        if (remaining <= 0f) Close(fadeOutDuration, null);
    }

    // ── Open / close ─────────────────────────────────────────────────────────────────────

    private void Open(InputHint hint)
    {
        if (panel == null || panelGroup == null || label == null) return;

        // Something on screen: clear it quickly, then open the new one.
        if (current != null && panelGroup.alpha > 0f)
        {
            Close(replaceFadeDuration, () => Begin(hint));
            return;
        }

        Begin(hint);
    }

    private void Begin(InputHint hint)
    {
        LeanTween.cancel(panel.gameObject);
        LeanTween.cancel(panelGroup.gameObject);

        current = hint;
        remaining = hint.seconds;
        closing = false;

        body = BuildLine(hint);
        cursorOn = true;
        cursorTimer = 0f;
        RenderLabel();
        label.maxVisibleCharacters = 0;   // Nothing shows until the console has opened.

        panelGroup.alpha = 1f;
        panel.localScale = new Vector3(1f, 0f, 1f);

        LeanTween.scaleY(panel.gameObject, 1f, openDuration)
                 .setEase(LeanTweenType.easeOutQuad)
                 .setIgnoreTimeScale(true)
                 .setOnComplete(() =>
                 {
                     if (typewriter != null) typewriter.Play();
                     else label.maxVisibleCharacters = int.MaxValue;
                 });
    }

    private void Close(float duration, System.Action then)
    {
        closing = true;
        LeanTween.cancel(panelGroup.gameObject);

        LeanTween.alphaCanvas(panelGroup, 0f, Mathf.Max(duration, 0.01f))
                 .setIgnoreTimeScale(true)
                 .setOnComplete(() =>
                 {
                     current = null;
                     closing = false;
                     panel.localScale = new Vector3(1f, 0f, 1f);
                     then?.Invoke();
                 });
    }

    // ── Text ─────────────────────────────────────────────────────────────────────────────

    private string BuildLine(InputHint hint)
    {
        string keys = uppercase ? hint.keys.ToUpperInvariant() : hint.keys;
        string action = uppercase ? hint.action.ToUpperInvariant() : hint.action;

        string keyColor = theme != null ? ColorUtility.ToHtmlStringRGB(theme.Get(keyRole)) : "FFFFFF";
        string coloredKeys = $"<color=#{keyColor}>[{keys}]</color>";

        string line = string.Format(format, coloredKeys, action);

        // Uppercasing the format too, but not the tags inside it.
        return uppercase ? UppercaseOutsideTags(line) : line;
    }

    private static string UppercaseOutsideTags(string text)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(text.Length);
        bool inTag = false;
        foreach (char c in text)
        {
            if (c == '<') inTag = true;
            sb.Append(inTag ? c : char.ToUpperInvariant(c));
            if (c == '>') inTag = false;
        }
        return sb.ToString();
    }

    private void TickCursor()
    {
        if (string.IsNullOrEmpty(cursorCharacter)) return;

        cursorTimer += Time.unscaledDeltaTime;
        if (cursorTimer < cursorBlinkInterval) return;

        cursorTimer = 0f;
        cursorOn = !cursorOn;
        RenderLabel();
    }

    private void RenderLabel()
    {
        if (string.IsNullOrEmpty(cursorCharacter))
        {
            label.text = body;
            return;
        }

        // Hidden with an alpha tag rather than dropped, so the line does not shift as it blinks.
        label.text = cursorOn ? body + cursorCharacter : body + "<alpha=#00>" + cursorCharacter;
    }

    // ── Input ────────────────────────────────────────────────────────────────────────────

    private static bool PlayerDidIt(InputHint hint)
    {
        if (hint.dismissOnMove &&
            (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f))
            return true;

        if (hint.dismissKeys == null) return false;
        foreach (KeyCode key in hint.dismissKeys)
            if (Input.GetKeyDown(key)) return true;

        return false;
    }
}
