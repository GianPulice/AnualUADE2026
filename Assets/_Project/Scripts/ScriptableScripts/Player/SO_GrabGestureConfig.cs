using UnityEngine;

/// <summary>
/// The arm reach the player plays when they take something or use a door (<see cref="PlayerGrabGesture"/>).
/// The clip runs on its own Animator layer, masked to the left arm, so walking carries on under it;
/// the layer's weight is faded in and out here, which is what keeps it from snapping on and off.
/// </summary>
[CreateAssetMenu(fileName = "SO_GrabGestureConfig", menuName = "Scriptable Objects/Player/Grab Gesture Config")]
public class SO_GrabGestureConfig : ScriptableObject
{
    [Tooltip("Animator layer of PlayerController that holds the gesture (masked to the left arm).")]
    [SerializeField] private string layerName = "Grab Arm";

    [Tooltip("State on that layer that plays the reach.")]
    [SerializeField] private string stateName = "Grabbing";

    [Tooltip("Seconds the layer takes to come in. Longer = a softer start.")]
    [SerializeField, Min(0f)] private float fadeInSeconds = 0.18f;

    [Tooltip("Seconds the layer takes to hand the arm back, ending with the clip.")]
    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.15f;

    [Tooltip("Playback speed of the state. Above 1 shortens the whole gesture.")]
    [SerializeField, Min(0.1f)] private float speed = 1f;

    public string LayerName => layerName;
    public string StateName => stateName;
    public float FadeInSeconds => fadeInSeconds;
    public float FadeOutSeconds => fadeOutSeconds;
    public float Speed => speed;
}
