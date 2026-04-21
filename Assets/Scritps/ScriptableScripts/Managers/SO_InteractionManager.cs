using UnityEngine;

[CreateAssetMenu(fileName = "SO_InteractionManager", menuName = "Scriptable Objects/SO_InteractionManager")]
public class SO_InteractionManager : ScriptableObject
{
    [SerializeField] private float interactionDistance;

    public float InteractionDistance { get => interactionDistance; }
}
