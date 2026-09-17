using TMPro;
using UnityEngine;

/// <summary>
/// The "[Press F to skip]" text in the corner of the HUD, with no panel behind it. Shows for the whole wake-up cinematic
/// (<see cref="WakeUpCinematicEvents.IsCameraLocked"/>) when <see cref="SO_WakeUpCinematicConfig.Skippable"/>
/// is on, and goes the moment the cinematic ends or is skipped. The skip itself is handled by
/// <see cref="WakeUpCinematicView"/>; this only tells the player it exists.
///
/// Hidden while the game is paused or a menu is open, when the key does nothing.
///
/// Must sit in HUDCanvas ABOVE the cinematic's black, or it is covered.
///
/// SETUP: Tools ▸ Architect ▸ Setup Wake-Up Cinematic.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class WakeUpSkipPromptView : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    [Tooltip("Seconds to fade in when the cinematic starts. It always hides at once.")]
    [SerializeField, Min(0f)] private float fadeInDuration = 0.4f;

    private CanvasGroup group;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    private void Update()
    {
        ArchitectVoiceController voice = ArchitectVoiceController.Instance;
        SO_WakeUpCinematicConfig config = voice != null ? voice.WakeUpConfig : null;

        bool show = WakeUpCinematicEvents.IsCameraLocked
                    && (config == null || config.Skippable)
                    && !PauseManager.IsGameplayInputBlocked;

        if (!show)
        {
            group.alpha = 0f;
            return;
        }

        if (label != null)
        {
            string text = config != null ? config.SkipPromptText : "[Press F to skip]";
            if (label.text != text) label.text = text;
        }

        // Unscaled: the cinematic runs on the voice line's unscaled clock.
        group.alpha = fadeInDuration > 0f
            ? Mathf.MoveTowards(group.alpha, 1f, Time.unscaledDeltaTime / fadeInDuration)
            : 1f;
    }
}
