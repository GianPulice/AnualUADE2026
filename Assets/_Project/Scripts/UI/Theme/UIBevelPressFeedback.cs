using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Sinks a button's <see cref="UIBevelFrame"/> while it is held down and raises it again on release —
/// the Win95 press. Only the frame changes; the fill and the label are left to
/// ButtonHoverSweepEffect and ButtonHoverColorSwap, so the three stack on one button without
/// fighting over the same property.
/// </summary>
[RequireComponent(typeof(Button))]
[AddComponentMenu("WIRED/UI/UI Bevel Press Feedback")]
public class UIBevelPressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Tooltip("The frame to sink. Usually the BevelFrame child of this button.")]
    [SerializeField] private UIBevelFrame frame;

    private Button button;
    private UIBevelFrame.BevelStyle restingStyle;

    private void Awake()
    {
        button = GetComponent<Button>();
        if (frame != null) restingStyle = frame.Style;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (frame == null || !button.IsInteractable()) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;

        frame.Style = UIBevelFrame.BevelStyle.Sunken;
    }

    public void OnPointerUp(PointerEventData eventData) => Release();
    public void OnPointerExit(PointerEventData eventData) => Release();

    // The inventory closes on the click itself, so the button is disabled mid-press; without this
    // it would reopen still sunken.
    private void OnDisable() => Release();

    private void Release()
    {
        if (frame != null) frame.Style = restingStyle;
    }
}
