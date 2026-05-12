using UnityEngine;
using TMPro;
using Cysharp.Threading.Tasks;
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
        HideNextLevelButton(); // Lose screen has no "next level"
        if (_titleText != null) _titleText.text = "You lose!";
        if (_btnOptions != null) _btnOptions.onClick.AddListener(() => OnOptionsClicked?.Invoke());
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if(_btnOptions != null) _btnOptions.onClick.RemoveAllListeners();
    }

    private void HandleOptionsButtonClicked()
    {
        OnOptionsClicked?.Invoke();
    }

    // Override for any lose-specific animations or effects before showing the screen.
    public override async UniTask ShowAsync()
    {
         // WIP
        await base.ShowAsync();
    }
}
