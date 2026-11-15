using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Yes / no box over the Save Slots screen, for what cannot be undone there: deleting a save, and
/// overwriting one with a new game. It lives inside CanvasSaveSlots, hidden, so it goes through the
/// same CRT tube as the cards, and its full-screen dim keeps them from being clicked while the
/// question is open.
///
/// One question at a time: <see cref="Ask"/> while open replaces the pending one. The cancel button,
/// <see cref="Cancel"/> (ESC, through the screen) and <see cref="HideImmediate"/> (the screen closing
/// or reopening) drop it without calling back.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class SaveSlotConfirmDialog : MonoBehaviour
{
    [Header("Labels")]
    [SerializeField] private TextMeshProUGUI _titleLabel;
    [SerializeField] private TextMeshProUGUI _bodyLabel;
    [SerializeField] private TextMeshProUGUI _confirmLabel;

    [Header("Buttons")]
    [Tooltip("Runs the action. Plays its own sound (UISounds.ItemDiscard), so it has no click sound.")]
    [SerializeField] private Button _confirmButton;
    [SerializeField] private Button _cancelButton;

    [Header("Timing")]
    [Tooltip("Fade of the dim and the box, in real seconds (the menu may run with timeScale 0).")]
    [SerializeField, Min(0f)] private float _fadeDuration = 0.12f;

    private CanvasGroup _group;
    private Action _onConfirm;
    private TextMeshProUGUI _cancelLabel;

    public bool IsOpen => gameObject.activeSelf && _group != null && _group.interactable;

    private void Awake()
    {
        _group = GetComponent<CanvasGroup>();

        if (_confirmButton != null) _confirmButton.onClick.AddListener(Confirm);
        if (_cancelButton != null) _cancelButton.onClick.AddListener(Cancel);
    }

    // The cancel button is a ButtonGeneric instance: its label is found, not wired.
    private TextMeshProUGUI CancelLabel =>
        _cancelLabel != null ? _cancelLabel
            : _cancelButton != null ? (_cancelLabel = _cancelButton.GetComponentInChildren<TextMeshProUGUI>(true)) : null;

    private void OnDestroy()
    {
        LeanTween.cancel(gameObject);
        if (_confirmButton != null) _confirmButton.onClick.RemoveListener(Confirm);
        if (_cancelButton != null) _cancelButton.onClick.RemoveListener(Cancel);
    }

    /// <summary>Shows the question. <paramref name="onConfirm"/> runs only if the player confirms.</summary>
    public void Ask(string title, string body, string confirmText, string cancelText, Action onConfirm)
    {
        _onConfirm = onConfirm;

        if (_titleLabel != null) _titleLabel.text = title;
        if (_bodyLabel != null) _bodyLabel.text = body;
        if (_confirmLabel != null && !string.IsNullOrEmpty(confirmText)) _confirmLabel.text = confirmText;
        if (CancelLabel != null && !string.IsNullOrEmpty(cancelText)) CancelLabel.text = cancelText;

        transform.SetAsLastSibling();
        gameObject.SetActive(true);
        if (_group == null) _group = GetComponent<CanvasGroup>();

        LeanTween.cancel(gameObject);
        _group.interactable = true;
        _group.blocksRaycasts = true;
        _group.alpha = 0f;
        LeanTween.alphaCanvas(_group, 1f, _fadeDuration).setIgnoreTimeScale(true);
    }

    /// <summary>Closes with no answer and no fade. Safe when already closed.</summary>
    public void HideImmediate()
    {
        _onConfirm = null;
        LeanTween.cancel(gameObject);
        if (_group != null)
        {
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;
        }
        gameObject.SetActive(false);
    }

    private void Confirm()
    {
        if (!IsOpen) return;

        Action action = _onConfirm;
        _onConfirm = null;
        FadeOut();

        UISounds.Play(UISounds.ItemDiscard);
        action?.Invoke();
    }

    /// <summary>No answer: closes without running the action (the cancel button, and ESC).</summary>
    public void Cancel()
    {
        if (!IsOpen) return;

        _onConfirm = null;
        FadeOut();
    }

    private void FadeOut()
    {
        // Inputs off at once, so a double click cannot answer twice during the fade.
        _group.interactable = false;
        _group.blocksRaycasts = false;

        LeanTween.cancel(gameObject);
        LeanTween.alphaCanvas(_group, 0f, _fadeDuration)
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                if (this != null && !_group.interactable) gameObject.SetActive(false);
            });
    }
}
