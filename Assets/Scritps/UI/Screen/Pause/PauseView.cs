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

    public void ResetButtonStates()
    {
        ResetButton(buttonContinue);
        ResetButton(buttonSettings);
        ResetButton(buttonExit);
    }
    private void ResetButton(Button btn)
    {
        if (btn == null) return;
        btn.OnPointerExit(new UnityEngine.EventSystems.PointerEventData(
            UnityEngine.EventSystems.EventSystem.current));
        btn.OnDeselect(null);
    }

    //-- Zona de efectos (WIP) ----------------------
}
