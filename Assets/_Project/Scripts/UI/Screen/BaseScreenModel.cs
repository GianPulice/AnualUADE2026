using System;

public abstract class BaseScreenModel
{
    public event Action OnDataChanged;
    public bool IsInitialized { get; protected set; }
    public abstract void Initialize();
    protected void NotifyDataChanged()
    {
        OnDataChanged?.Invoke();
    }
}
