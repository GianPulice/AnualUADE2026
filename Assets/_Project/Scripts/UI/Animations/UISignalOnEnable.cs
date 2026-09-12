using UnityEngine;

/// <summary>
/// Plays this panel's <see cref="UISignalTransition"/> every time the panel is activated, so a screen
/// being shown or a Settings tab being selected reads as the tube locking on to a new channel.
///
/// Its only job is that trigger. Screens and tabs already show themselves with SetActive
/// (BaseScreenView.ShowAsync, SettingsTabSelector), so hooking activation covers both without either
/// knowing about the transition.
///
/// Runs after UISignalTransition: on activation Unity calls Awake and OnEnable script by script, and
/// Play() needs the transition's Awake to have found its CanvasGroup first.
/// </summary>
[DefaultExecutionOrder(10)]
[RequireComponent(typeof(UISignalTransition))]
[AddComponentMenu("WIRED/UI Animations/UI Signal On Enable")]
public class UISignalOnEnable : MonoBehaviour
{
    private UISignalTransition signal;

    private void Awake() => signal = GetComponent<UISignalTransition>();

    private void OnEnable() => signal.Play();
}
