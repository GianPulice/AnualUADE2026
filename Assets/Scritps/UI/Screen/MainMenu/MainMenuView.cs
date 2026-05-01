using System;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuView : BaseScreenView
{
    [SerializeField] private Button newGameBtn;
    [SerializeField] private Button loadGameBtn;
    [SerializeField] private Button settingsBtn;
    [SerializeField] private Button exitBtn;

    public event Action OnNewGameClicked;
    public event Action OnLoadGameClicked;
    public event Action OnSettingsClicked;
    public event Action OnExitClicked;
    private void Awake()
    {
        newGameBtn.onClick.AddListener(() => OnNewGameClicked?.Invoke());
        loadGameBtn.onClick.AddListener(() => OnLoadGameClicked?.Invoke());
        settingsBtn.onClick.AddListener(() => OnSettingsClicked?.Invoke());
        exitBtn.onClick.AddListener(() => OnExitClicked?.Invoke());
    }
    private void OnDestroy()
    {
        newGameBtn.onClick.RemoveAllListeners();
        loadGameBtn.onClick.RemoveAllListeners();
        settingsBtn.onClick.RemoveAllListeners();
        exitBtn.onClick.RemoveAllListeners();
    }
}
