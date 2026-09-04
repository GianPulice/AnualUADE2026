using UnityEngine;
using System;
public enum PauseState { Unpaused, Paused }
public class PauseModel : BaseScreenModel
{
    public event Action<PauseState> OnStateChanged;
    private PauseState state = PauseState.Unpaused;
    public PauseState State
    {
        get => state;
        private set
        {
            if (state == value) return;
            state = value;
            OnStateChanged?.Invoke(state);
            NotifyDataChanged();         }
    }
    public bool IsPaused => state == PauseState.Paused;
    public override void Initialize()
    {
        state = PauseState.Unpaused;
        IsInitialized = true;
    }
    public void Pause() => State = PauseState.Paused;
    public void Unpause() => State = PauseState.Unpaused;
    public void Toggle() => State = IsPaused ? PauseState.Unpaused : PauseState.Paused;
}
