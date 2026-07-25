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

    // Visibilidad por boton. Los Set* permiten alternar en runtime (una misma pantalla
    // que cambia de cara segun el resultado); los Hide* son el atajo de las pantallas
    // que deciden su layout una sola vez en Awake.
    protected void SetRetryVisible(bool visible)     => _btnRetry?.gameObject.SetActive(visible);
    protected void SetMainMenuVisible(bool visible)  => _btnMainMenu?.gameObject.SetActive(visible);
    protected void SetNextLevelVisible(bool visible) => _btnNextLevel?.gameObject.SetActive(visible);
    protected void SetExitVisible(bool visible)      => _btnExit?.gameObject.SetActive(visible);

    protected void HideRetryButton()     => SetRetryVisible(false);
    protected void HideMainMenuButton()  => SetMainMenuVisible(false);
    protected void HideNextLevelButton() => SetNextLevelVisible(false);
}
