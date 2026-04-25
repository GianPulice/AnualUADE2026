using UnityEngine;

public class ModuleData : MonoBehaviour
{
    [Header("ID")]
    public string moduleID;
    public string moduleLabel;

    [Header("State")]
    public ModuleStatus status;
    public int failuresCount;
    public int MaxFailures = 3;
    [Header("Timer")]
    public float timerDuration; //Tiempo total
    public float timeRemaining;
    public bool isTimerRunning;

    public float TimerProgress => timerDuration > 0f ? timeRemaining / timerDuration : 0f;

    public string FormattedTime
    {
        get
        {
            int minutes = Mathf.FloorToInt(timeRemaining / 60f);
            int seconds = Mathf.FloorToInt(timeRemaining % 60f);
            return $"{minutes:00}:{seconds:00}";
        }
    }
    public bool hasFailures => failuresCount > 0;
    public bool isOperational => status == ModuleStatus.Active;
}
public enum ModuleStatus
{
    Active,
    Warning,
    Failure,
    Inactive
}