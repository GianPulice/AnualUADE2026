using UnityEngine;

[CreateAssetMenu(fileName = "SO_MovingPlatform", menuName = "Scriptable Objects/SO_MovingPlatform")]
public class SO_MovingPlatform : ScriptableObject
{
    [SerializeField, Min(0f)] private float speed = 2f;
    [SerializeField, Min(0f)] private float distance = 5f;
    [SerializeField, Min(0f)] private float startDelay = 1f;

    public float Speed { get => speed; set => speed = value; }
    public float Distance { get => distance; set => distance = value; }
    public float StartDelay { get => startDelay; set => startDelay = value; }
}
