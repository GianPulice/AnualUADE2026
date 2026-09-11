using TMPro;
using UnityEngine;

/// <summary>
/// Types a label out instead of showing it all at once. Play() when the text changes, before or after
/// it is set: it counts against the text as it stands on each frame, so which lands first does not
/// matter.
///
/// Long texts speed up rather than keep typing: nothing takes longer than <see cref="maxDuration"/>,
/// because a description the player has to wait for is worse than one that just appears.
///
/// Runs on unscaled time, like the rest of the inventory's animation.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
[AddComponentMenu("WIRED/UI Animations/TMP Typewriter Reveal")]
public class TMPTypewriterReveal : MonoBehaviour
{
    [SerializeField] private float charactersPerSecond = 80f;
    [SerializeField] private float maxDuration = 0.45f;
    [Tooltip("Seconds before the first character, so a panel transition gets a head start.")]
    [SerializeField] private float delay = 0.05f;

    private const int AllCharacters = 99999;

    private TMP_Text label;
    private float elapsed;
    private bool playing;

    private void Awake() => label = GetComponent<TMP_Text>();

    private void OnDisable() => Finish();

    public void Play()
    {
        if (!isActiveAndEnabled) return;

        playing = true;
        elapsed = -delay;
        label.maxVisibleCharacters = 0;
    }

    private void Update()
    {
        if (!playing) return;

        elapsed += Time.unscaledDeltaTime;

        // Counted on the raw string, tags included, so a tagged text merely finishes a hair late.
        int total = label.text != null ? label.text.Length : 0;
        float rate = Mathf.Max(charactersPerSecond, total / Mathf.Max(maxDuration, 0.01f));
        int visible = Mathf.Max(0, Mathf.FloorToInt(elapsed * rate));

        if (visible >= total) Finish();
        else label.maxVisibleCharacters = visible;
    }

    private void Finish()
    {
        playing = false;
        if (label != null) label.maxVisibleCharacters = AllCharacters;
    }
}
