using UnityEngine;

[CreateAssetMenu(fileName = "SO_NemesisMovemetn", menuName = "Scriptable Objects/SO_NemesisMovemetn")]
public class SO_NemesisMovemetn : ScriptableObject
{
    [SerializeField] private float patrolSpeed;
    [SerializeField] private float investigationSpeed;
    [SerializeField] private float chaseSpeed;
    [SerializeField] private float searchSpeed;
    [SerializeField] private float angularSpeed;
    [SerializeField] private float acceleration;
    [SerializeField] private float stoppingDistance;

    public float PatrolSpeed { get => patrolSpeed; set => patrolSpeed = value; }
    public float InvestigationSpeed { get => investigationSpeed; set => investigationSpeed = value; }
    public float ChaseSpeed { get => chaseSpeed; set => chaseSpeed = value; }
    public float SearchSpeed { get => searchSpeed; set => searchSpeed = value; }
    public float AngularSpeed { get => angularSpeed; set => angularSpeed = value; }
    public float Acceleration { get => acceleration; set => acceleration = value; }
    public float StoppingDistance { get => stoppingDistance; set => stoppingDistance = value; }
}
