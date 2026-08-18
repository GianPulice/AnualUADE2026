using UnityEngine;

[CreateAssetMenu(fileName = "SO_InteractionManager", menuName = "Scriptable Objects/SO_InteractionManager")]
public class SO_InteractionManager : ScriptableObject
{
    [Header("Interaction raycast")]
    [Tooltip("Maximum distance from the camera at which the player can interact with an item.")]
    [SerializeField, Min(0f)] private float interactionDistance = 3f;

    [Tooltip("Layers the raycast treats as interactable.")]
    [SerializeField] private LayerMask interactableLayers = ~0;

    [Tooltip("Layers that block line of sight (walls, solid props). If the raycast hits one of these first, the interactable is not detected.")]
    [SerializeField] private LayerMask blockingLayers = ~0;

    public float InteractionDistance => interactionDistance;
    public LayerMask InteractableLayers => interactableLayers;
    public LayerMask BlockingLayers => blockingLayers;
}
