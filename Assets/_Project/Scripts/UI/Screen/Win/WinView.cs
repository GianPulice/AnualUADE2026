using TMPro;
using UnityEngine;

public class WinView : BaseResultView
{
    [Header("Win-specific")]
    [SerializeField] private TextMeshProUGUI _titleText;

    [Tooltip("Large title. Empty = keep whatever the text has in the prefab.")]
    [SerializeField] private string _title = "You win!";

    [Tooltip("Where the title comes from at the end of the escape. Empty = the Title field above.")]
    [SerializeField] private SO_EscapeEndingConfig _endingConfig;

    protected override void Awake()
    {
        base.Awake();
        string title = _endingConfig != null ? _endingConfig.Title : _title;
        if (_titleText != null && !string.IsNullOrEmpty(title)) _titleText.text = title;
        HideRetryButton();
        HideNextLevelButton();
    }

    public override void SetData(GameResultModel model) => base.SetData(model);
}
