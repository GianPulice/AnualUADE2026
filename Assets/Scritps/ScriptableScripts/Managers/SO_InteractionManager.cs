using UnityEngine;

[CreateAssetMenu(fileName = "SO_InteractionManager", menuName = "Scriptable Objects/SO_InteractionManager")]
public class SO_InteractionManager : ScriptableObject
{
    [Header("Interaction raycast")]
    [Tooltip("Reach in metres, measured FROM THE PLAYER — not from the camera. The camera is a " +
             "third person rig sitting ~3.4 m behind and above the character, so a distance " +
             "measured from it spends most of its budget on empty air before it even reaches the " +
             "player's hands. 2 to 3 is arm's reach.")]
    [SerializeField, Min(0f)] private float interactionDistance = 2.5f;

    [Tooltip("Layers the raycast treats as interactable. Triggers on these layers ARE detected, " +
             "so an interaction volume can be a trigger and stop walling off the doorway it " +
             "belongs to.")]
    [SerializeField] private LayerMask interactableLayers = ~0;

    [Tooltip("Layers that block line of sight (walls, solid props). If one of these sits between " +
             "the PLAYER and the interactable, the interactable is not detected. Triggers on " +
             "these layers are ignored — only solid geometry occludes.")]
    [SerializeField] private LayerMask blockingLayers = ~0;

    [Header("Aiming")]
    [Tooltip("Where the crosshair sits, in viewport coordinates (0,0 = bottom-left, 1,1 = " +
             "top-right). The cast is fired through this exact point, so it always agrees with " +
             "what the player sees. Keep it in step with the Crosshair RectTransform in LevelUI: " +
             "an anchor of 0.5/0.5 with anchoredPosition 0,0 is (0.5, 0.5) here.")]
    [SerializeField] private Vector2 crosshairViewportPoint = new Vector2(0.5f, 0.5f);

    [Tooltip("Radius of the SphereCast. A 'thick' ray makes aiming at small items (pickups on the " +
             "floor, valves) forgiving without losing directionality. 0 makes it a plain ray.")]
    [SerializeField, Min(0f)] private float castRadius = 0.1f;

    [Tooltip("Fallback height above the player's pivot used as the reach origin when the player " +
             "has no CapsuleCollider to read. With one, the capsule's own centre is used, so the " +
             "origin follows the crouch automatically.")]
    [SerializeField, Min(0f)] private float fallbackOriginHeight = 1.2f;

    public float InteractionDistance => interactionDistance;
    public LayerMask InteractableLayers => interactableLayers;
    public LayerMask BlockingLayers => blockingLayers;
    public Vector2 CrosshairViewportPoint => crosshairViewportPoint;
    public float CastRadius => castRadius;
    public float FallbackOriginHeight => fallbackOriginHeight;
}
