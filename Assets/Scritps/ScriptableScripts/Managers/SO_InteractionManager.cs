using UnityEngine;

[CreateAssetMenu(fileName = "SO_InteractionManager", menuName = "Scriptable Objects/SO_InteractionManager")]
public class SO_InteractionManager : ScriptableObject
{
    [Header("Raycast de interaccion")]
    [Tooltip("Distancia maxima desde la camara a la que el jugador puede interactuar con un item.")]
    [SerializeField, Min(0f)] private float interactionDistance = 3f;

    [Tooltip("Layers que el raycast considera como interactuables.")]
    [SerializeField] private LayerMask interactableLayers = ~0;

    [Tooltip("Layers que bloquean la vision (paredes, props solidos). Si el raycast pega antes contra una de estas, no se detecta el interactuable.")]
    [SerializeField] private LayerMask blockingLayers = ~0;

    public float InteractionDistance => interactionDistance;
    public LayerMask InteractableLayers => interactableLayers;
    public LayerMask BlockingLayers => blockingLayers;
}
