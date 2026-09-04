using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class LoadingView : BaseScreenView
{
    [SerializeField] private Slider progressSlider;
    [SerializeField] private TextMeshProUGUI progressText;

    public void UpdateProgress(float progress)
    {
        // progress comes in from 0.0 to 1.0. We show it in the UI.
        progressSlider.value = progress;
        progressText.text = $"Loading... {Mathf.RoundToInt(progress * 100)}%";
    }
}
