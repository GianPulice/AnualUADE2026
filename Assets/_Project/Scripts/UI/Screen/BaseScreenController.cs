using UnityEngine;
using Cysharp.Threading.Tasks;
public abstract class BaseScreenController<TView, TModel> : MonoBehaviour, IScreen
    where TView : BaseScreenView
    where TModel : BaseScreenModel
{
    [SerializeField] protected TView view;
    protected TModel model;

    public bool IsActive => view.gameObject.activeSelf;

    public virtual void InjectDependencies(TModel injectedModel)
    {
        model = injectedModel;
    }
    public virtual async UniTask Open()
    {
        OnBeforeOpen();
        await view.ShowAsync();
        OnAfterOpen();
    }
    public virtual async UniTask Close()
    {
        OnBeforeClose();
        await view.HideAsync();
        OnAfterClose();
    }
    protected virtual void OnBeforeOpen() { }
    protected virtual void OnAfterOpen() { }
    protected virtual void OnBeforeClose() { }
    protected virtual void OnAfterClose() { }
}
