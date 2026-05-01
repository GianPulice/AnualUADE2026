using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class LoadingView : BaseScreenView
{
    [SerializeField] private Slider progressSlider;
    [SerializeField] private TextMeshProUGUI progressText;

    public void UpdateProgress(float progress)
    {
        // progress viene de 0.0 a 1.0. Lo mostramos en la UI.
        progressSlider.value = progress;
        progressText.text = $"Cargando... {Mathf.RoundToInt(progress * 100)}%";
    }
}
