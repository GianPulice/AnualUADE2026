using UnityEngine;

[CreateAssetMenu(fileName = "SO_InteractionManager", menuName = "Scriptable Objects/SO_InteractionManager")]
public class SO_InteractionManager : ScriptableObject
{
    [Header("Radios globales para todos los BaseRangeInteractable")]
    [Tooltip("Radio del collider lejano (cuando aparece el ImageFar).")]
    [SerializeField, Min(0f)] private float farRadius = 3f;

    [Tooltip("Radio del collider cercano (cuando aparece el ImageClose y se puede interactuar).")]
    [SerializeField, Min(0f)] private float closeRadius = 1.5f;

    public float FarRadius => farRadius;
    public float CloseRadius => closeRadius;
}
