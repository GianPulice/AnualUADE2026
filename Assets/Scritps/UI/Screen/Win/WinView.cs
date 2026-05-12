using UnityEngine;
using TMPro;
using Cysharp.Threading.Tasks;

public class WinView : BaseResultView
{
    [Header("Win-specific")]
    [SerializeField] private GameObject _starsContainer;
    [SerializeField] private TextMeshProUGUI _titleText;

    protected override void Awake()
    {
        base.Awake();
        if (_titleText != null) _titleText.text = "You win!";
        HideNextLevelButton();
        HideRetryButton();
    }

    public override void SetData(GameResultModel model)
    {
        base.SetData(model);
    }

    public override async UniTask ShowAsync()
    {
        await base.ShowAsync();
    }
}
