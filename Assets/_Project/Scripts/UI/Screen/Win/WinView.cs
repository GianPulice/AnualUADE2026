using TMPro;
using UnityEngine;

public class WinView : BaseResultView
{
    [Header("Win-specific")]
    [SerializeField] private TextMeshProUGUI _titleText;

    [Tooltip("Large title. Empty = keep whatever the text has in the prefab.")]
    [SerializeField] private string _title = "You win!";

    protected override void Awake()
    {
        base.Awake();
        if (_titleText != null && !string.IsNullOrEmpty(_title)) _titleText.text = _title;
        HideRetryButton();
        HideNextLevelButton();
    }

    public override void SetData(GameResultModel model) => base.SetData(model);
}
