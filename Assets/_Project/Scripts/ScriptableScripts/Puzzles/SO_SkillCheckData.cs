using UnityEngine;

[CreateAssetMenu(fileName = "SO_SkillCheckData", menuName = "Scriptable Objects/Puzzles/Skill Check Data")]
public class SO_SkillCheckData : ScriptableObject
{
    [Header("Needle")]
    [Tooltip("Degrees per second the needle rotates.")]
    public float needleSpeed = 90f;

    [Header("Success zone")]
    [Tooltip("Start angle (0-360) of the success zone on the circle.")]
    public float successZoneStartAngle = 80f;
    [Tooltip("Initial width of the zone in degrees. Grows with every successful check.")]
    public float initialSuccessZoneWidth = 40f;
    [Tooltip("Degrees added to the zone for each check passed (decreasing difficulty).")]
    public float successZoneWidthIncrement = 10f;

    [Header("Checks")]
    [Tooltip("Total number of checks needed to complete the puzzle.")]
    public int totalChecks = 4;

    [Header("Penalty")]
    [Tooltip("Seconds subtracted from the active module's timer when a check is failed.")]
    public float failTimePenalty = 5f;

    [Header("Feedback")]
    [Tooltip("Duration of the success/fail flash in seconds.")]
    public float flashDuration = 0.1f;
}
