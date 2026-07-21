using UnityEngine;

[CreateAssetMenu(fileName = "SO_SkillCheckData", menuName = "Scriptable Objects/Puzzles/Skill Check Data")]
public class SO_SkillCheckData : ScriptableObject
{
    [Header("Aguja")]
    [Tooltip("Grados por segundo que rota la aguja.")]
    public float needleSpeed = 90f;

    [Header("Zona de éxito")]
    [Tooltip("Ángulo de inicio (0-360) de la zona de éxito en el círculo.")]
    public float successZoneStartAngle = 80f;
    [Tooltip("Ancho inicial de la zona en grados. Crece con cada check acertado.")]
    public float initialSuccessZoneWidth = 40f;
    [Tooltip("Grados que se suman a la zona por cada check superado (dificultad decreciente).")]
    public float successZoneWidthIncrement = 10f;

    [Header("Checks")]
    [Tooltip("Número total de checks para completar el puzzle.")]
    public int totalChecks = 4;

    [Header("Penalización")]
    [Tooltip("Segundos que se restan al timer del módulo activo al fallar un check.")]
    public float failTimePenalty = 5f;

    [Header("Feedback")]
    [Tooltip("Duración del flash de acierto/fallo en segundos.")]
    public float flashDuration = 0.1f;
}
