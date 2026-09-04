using TMPro;
using UnityEngine;

public class WinView : BaseResultView
{
    [Header("Win-specific")]
    [SerializeField] private TextMeshProUGUI _titleText;

    protected override void Awake()
    {
        base.Awake();
        if (_titleText != null) _titleText.text = "You win!";
        HideRetryButton();
        HideNextLevelButton();
    }

    public override void SetData(GameResultModel model) => base.SetData(model);
}
