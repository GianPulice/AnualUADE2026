using System;
using UnityEngine;
using UnityEngine.UI;

public abstract class BaseResultView : BaseScreenView
{
    [Header("Buttons (shared)")]
    [SerializeField] private Button _btnRetry;
    [SerializeField] private Button _btnMainMenu;
    [SerializeField] private Button _btnNextLevel;
    [SerializeField] private Button _btnExit;

    public event Action OnRetryClicked;
    public event Action OnMainMenuClicked;
    public event Action OnNextLevelClicked;
    public event Action OnExitClicked;

    protected virtual void Awake()
    {
        _btnRetry?.onClick.AddListener(() => OnRetryClicked?.Invoke());
        _btnMainMenu?.onClick.AddListener(() => OnMainMenuClicked?.Invoke());
        _btnNextLevel?.onClick.AddListener(() => OnNextLevelClicked?.Invoke());
        _btnExit?.onClick.AddListener(() => OnExitClicked?.Invoke());
    }

    protected virtual void OnDestroy()
    {
        _btnRetry?.onClick.RemoveAllListeners();
        _btnMainMenu?.onClick.RemoveAllListeners();
        _btnNextLevel?.onClick.RemoveAllListeners();
        _btnExit?.onClick.RemoveAllListeners();
    }

    public virtual void SetData(GameResultModel model) { }

    protected void HideRetryButton()    => _btnRetry?.gameObject.SetActive(false);
    protected void HideMainMenuButton() => _btnMainMenu?.gameObject.SetActive(false);
    protected void HideNextLevelButton() => _btnNextLevel?.gameObject.SetActive(false);
}
