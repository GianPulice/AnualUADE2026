using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Hover and click sounds of one Selectable (Button, Toggle, Slider, Dropdown). Silent while the
/// Selectable is not interactable, so a greyed-out "Load Game" makes no sound.
///
/// Click plays on pointer click and on Submit (keyboard / gamepad), never on a Slider; a Slider
/// ticks with the hover sound while its value changes instead. Hover
/// plays on pointer enter only: keyboard navigation already gets the click on Submit, and a sound
/// on every OnSelect would also fire on each mouse click.
///
/// Usually added at runtime by <see cref="UICanvasSounds"/>; add it by hand, or call
/// <see cref="Configure"/>, for a node that needs different sounds.
/// </summary>
[RequireComponent(typeof(Selectable))]
public class UISelectableSound : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler, ISubmitHandler
{
    [Tooltip("Empty = no hover sound.")]
    [SerializeField] private string hoverId = UISounds.Hover;

    [Tooltip("Empty = no click sound (e.g. when the action it triggers already plays its own).")]
    [SerializeField] private string clickId = UISounds.Click;

    [Tooltip("Sliders only: sound while the value is dragged or stepped. Empty = none.")]
    [SerializeField] private string slideId = UISounds.Hover;

    [Tooltip("Sliders only: minimum seconds between two slide sounds, so a drag ticks instead of buzzing.")]
    [SerializeField, Min(0f)] private float slideInterval = 0.08f;

    private Selectable selectable;
    private float lastSlideTime = float.NegativeInfinity;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();

        // Settings panels fill sliders with SetValueWithoutNotify, so this only hears the player.
        if (selectable is Slider slider) slider.onValueChanged.AddListener(HandleSlide);
    }

    private void OnDestroy()
    {
        if (selectable is Slider slider) slider.onValueChanged.RemoveListener(HandleSlide);
    }

    private void HandleSlide(float _)
    {
        if (!IsLive || Time.unscaledTime - lastSlideTime < slideInterval) return;
        lastSlideTime = Time.unscaledTime;
        UISounds.Play(slideId);
    }

    public void Configure(string hover, string click)
    {
        hoverId = hover;
        clickId = click;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!IsLive || string.IsNullOrEmpty(hoverId)) return;
        UISounds.Play(hoverId);
        UIHoverStatic.Play();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) PlayClick();
    }

    public void OnSubmit(BaseEventData eventData) => PlayClick();

    private void PlayClick()
    {
        if (IsLive && !(selectable is Slider) && !(selectable is Scrollbar)) UISounds.Play(clickId);
    }

    private bool IsLive => selectable != null && selectable.IsInteractable() && isActiveAndEnabled;
}
