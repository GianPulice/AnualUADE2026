using System;

public class SkillCheckModel : BaseScreenModel
{
    public int   CurrentCheck      { get; private set; }
    public int   TotalChecks       { get; private set; }
    public float NeedleSpeed       { get; private set; }
    public float SuccessZoneStart  { get; private set; }
    public float SuccessZoneWidth  { get; private set; }
    public bool  IsComplete        { get; private set; }

    public event Action OnCheckSuccess;
    public event Action OnCheckFailed;
    public event Action OnAllChecksComplete;

    private float _widthIncrement;

    public void Configure(SO_SkillCheckData data)
    {
        NeedleSpeed      = data.needleSpeed;
        SuccessZoneStart = data.successZoneStartAngle;
        SuccessZoneWidth = data.initialSuccessZoneWidth;
        _widthIncrement  = data.successZoneWidthIncrement;
        TotalChecks      = data.totalChecks;
        CurrentCheck     = 0;
        IsComplete       = false;
        IsInitialized    = true;
    }

    public override void Initialize()
    {
        CurrentCheck = 0;
        IsComplete   = false;
        IsInitialized = true;
    }

    // Devuelve true si el ángulo de la aguja está dentro de la zona de éxito.
    public bool TryInput(float needleAngle)
    {
        if (IsComplete) return false;

        if (IsAngleInZone(needleAngle))
        {
            CurrentCheck++;
            SuccessZoneWidth += _widthIncrement;
            NotifyDataChanged();

            if (CurrentCheck >= TotalChecks)
            {
                IsComplete = true;
                OnAllChecksComplete?.Invoke();
            }
            else
            {
                OnCheckSuccess?.Invoke();
            }
            return true;
        }

        OnCheckFailed?.Invoke();
        return false;
    }

    private bool IsAngleInZone(float angle)
    {
        float start = SuccessZoneStart % 360f;
        float end   = (start + SuccessZoneWidth) % 360f;
        if (start <= end)
            return angle >= start && angle <= end;
        // zona cruza los 0°
        return angle >= start || angle <= end;
    }
}
