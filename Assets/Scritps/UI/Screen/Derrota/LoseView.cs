using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;

public class LoseView : BaseResultView
{
    [Header("Lose-specific")]
    [SerializeField] private TextMeshProUGUI _titleText;
    [SerializeField] private Button _btnOptions;

    public event Action OnOptionsClicked;

    protected override void Awake()
    {
        base.Awake();
        if (_titleText != null) _titleText.text = "You lose!";
        HideNextLevelButton();
        HideMainMenuButton();
        _btnOptions?.onClick.AddListener(() => OnOptionsClicked?.Invoke());
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        _btnOptions?.onClick.RemoveAllListeners();
    }
}
