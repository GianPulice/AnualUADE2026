using System;
using UnityEngine;
using UnityEngine.UI;

public abstract class BaseResultView : BaseScreenView
{
    //-- Shared UI -----------------------
    [Header("Buttons (shared)")]
    [SerializeField] private Button _btnRetry;
    [SerializeField] private Button _btnMainMenu;
    [SerializeField] private Button _btnNextLevel;

    // -- Button Events ----------------------
    public event Action OnRetryClicked;
    public event Action OnMainMenuClicked;
    public event Action OnNextLevelClicked;

    // -- Unity Methods ----------------------

    protected virtual void Awake()
    {
        _btnRetry.onClick.AddListener(() => OnRetryClicked?.Invoke());
        _btnMainMenu.onClick.AddListener(() => OnMainMenuClicked?.Invoke());
        _btnNextLevel.onClick.AddListener(() => OnNextLevelClicked?.Invoke());
    }
    protected virtual void OnDestroy()
    {
        _btnRetry?.onClick.RemoveAllListeners();
        _btnMainMenu?.onClick.RemoveAllListeners();
        _btnNextLevel?.onClick.RemoveAllListeners();
    }


    // -- Data Binding ----------------------
    public virtual void SetData(GameResultModel model)
    {
        // Base implementation can be empty or set shared UI elements if needed
    }
    protected void HideNextLevelButton()
    {
        if(_btnNextLevel != null)
            _btnNextLevel.gameObject.SetActive(false);
    }
    protected void HideRetryButton()
    {
        if(_btnRetry != null)
            _btnRetry.gameObject.SetActive(false);
    }
}
