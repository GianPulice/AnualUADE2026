using System;
using UnityEngine;
using UnityEngine.UI;

public class PauseView : BaseScreenView
{
    [Header("Button")]
    [SerializeField] private Button buttonContinue;
    [SerializeField] private Button buttonSettings;
    [SerializeField] private Button buttonExit;

    //[Header("Effects")]
    //[SerializeField] private PauseEffectHandler effectHandler;

    public event Action OnContinueClicked;
    public event Action OnSettingsClicked;
    public event Action OnExitClicked;

    private void Awake()
    {
        buttonContinue?.onClick.AddListener(() => OnContinueClicked?.Invoke());
        buttonSettings?.onClick.AddListener(() => OnSettingsClicked?.Invoke());
        buttonExit?.onClick.AddListener(() => OnExitClicked?.Invoke());
    }
    private void OnDestroy()
    {
        buttonContinue?.onClick.RemoveAllListeners();
        buttonSettings?.onClick.RemoveAllListeners();
        buttonExit?.onClick.RemoveAllListeners();
    }

    //-- Zona de efectos (WIP) ----------------------
}
