using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One look of the hiding overlay: the full-screen graphic the player sees from inside ONE kind of
/// hiding spot. A child of <see cref="HidingOverlayView"/>, which finds its layers by this component
/// and fades in the one whose <see cref="Type"/> matches the spot the player climbed into.
///
/// The look itself is the material on this object's Graphic — UI_HidingLocker, UI_HidingUnderTable,
/// UI_HidingContainer, with their shaders in Art/Materials/UI. Every shape, proportion and colour is
/// tuned there, live, with <see cref="HidingOverlayView"/>'s edit-mode preview on. This component
/// only says which spot type the layer answers to and how it follows the camera.
///
/// A new spot type is a new child with this component and a material of its own: no code. If
/// <see cref="EHidingSpotType"/> grows and no layer carries the new value, the view warns once the
/// player climbs into one and shows nothing.
///
/// SETUP: a full-screen (stretched) child of HUDCanvas/HidingOverlay with a RawImage — no texture,
/// the look's material, Raycast Target off — and this component.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Graphic))]
[AddComponentMenu("WIRED/UI/Hiding Overlay Layer")]
public class HidingOverlayLayer : MonoBehaviour
{
    [Tooltip("The kind of hiding spot this look belongs to. One layer per type.")]
    [SerializeField] private EHidingSpotType type = EHidingSpotType.Locker;

    [Tooltip("How firmly the look stays put while the player looks around inside the spot.\n\n" +
             "0 = glued to the screen, like a sticker on the lens.\n" +
             "1 = fixed in the world, like the real door: turn the camera and the look slides the " +
             "other way exactly as far as the room behind it does.\n\n" +
             "In between it follows only part of the way, which keeps more of the opening in " +
             "front of the player. Above 1 it overshoots, as if the head leaned as well as turned.")]
    [SerializeField, Range(0f, 1.5f)] private float lookParallax = 0.6f;

    private Graphic graphic;

    public EHidingSpotType Type => type;
    public float LookParallax => lookParallax;

    /// <summary>
    /// The Graphic that draws the look. Found on this object rather than assigned: a reference a
    /// designer has to remember to drag is one more thing that fails silently.
    /// </summary>
    public Graphic Graphic
    {
        get
        {
            if (graphic == null) graphic = GetComponent<Graphic>();
            return graphic;
        }
    }
}
