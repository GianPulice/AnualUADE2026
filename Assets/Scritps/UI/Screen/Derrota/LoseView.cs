using UnityEngine;
using TMPro;
using Cysharp.Threading.Tasks;

public class LoseView : BaseResultView
{
    [Header("Lose-specific")]
    [SerializeField] private TextMeshProUGUI _titleText;

    protected override void Awake()
    {
        base.Awake();
        HideNextLevelButton(); // Lose screen has no "next level"
        if (_titleText != null) _titleText.text = "You lose!";
    }

    // Override for any lose-specific animations or effects before showing the screen.
    public override async UniTask ShowAsync()
    {
         // WIP
        await base.ShowAsync();
    }
}
