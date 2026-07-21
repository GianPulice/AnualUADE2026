using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Vista del Skill-Check (Puzzle Central 2).
// Requiere en jerarquía:
//   - NeedleTransform  : Transform que rota (hijo del círculo)
//   - SuccessZoneImage : Image (Filled, Radial360) que marca la zona válida
//   - CounterText      : TextMeshProUGUI con "X / Y"
//   - FlashImage       : Image semitransparente de pantalla completa (alpha 0 por defecto)
public class SkillCheckView : BaseScreenView
{
    [Header("Aguja")]
    [SerializeField] private Transform needleTransform;

    [Header("Zona de éxito")]
    [SerializeField] private RectTransform successZoneRoot;
    [SerializeField] private Image successZoneImage;

    [Header("Contador")]
    [SerializeField] private TextMeshProUGUI counterText;

    [Header("Flash")]
    [SerializeField] private Image flashImage;
    [SerializeField] private Color successFlashColor = new Color(1f, 1f, 1f, 0.6f);
    [SerializeField] private Color failFlashColor    = new Color(0.8f, 0.1f, 0.1f, 0.6f);

    private float _needleAngle;
    private float _flashDuration;
    private CancellationTokenSource _flashCts;

    public float NeedleAngle => _needleAngle;

    public void Initialize(float zoneStartAngle, float zoneWidth, int totalChecks, float flashDuration)
    {
        _flashDuration = flashDuration;
        _needleAngle   = 0f;

        if (needleTransform != null)
            needleTransform.localRotation = Quaternion.identity;

        UpdateSuccessZone(zoneStartAngle, zoneWidth);
        UpdateCounter(0, totalChecks);

        if (flashImage != null)
        {
            flashImage.color = Color.clear;
            flashImage.gameObject.SetActive(false);
        }
    }

    // Llamado cada frame por el controller mientras el check corre.
    public void Tick(float speed)
    {
        _needleAngle = (_needleAngle + speed * Time.deltaTime) % 360f;
        if (needleTransform != null)
            needleTransform.localRotation = Quaternion.Euler(0f, 0f, -_needleAngle);
    }

    public void UpdateCounter(int current, int total)
    {
        if (counterText != null)
            counterText.text = $"{current} / {total}";
    }

    // zoneStartAngle: donde empieza la zona (rotación del root).
    // zoneWidth: arco en grados → fillAmount = width/360.
    public void UpdateSuccessZone(float zoneStartAngle, float zoneWidth)
    {
        if (successZoneRoot != null)
            successZoneRoot.localRotation = Quaternion.Euler(0f, 0f, -zoneStartAngle);
        if (successZoneImage != null)
            successZoneImage.fillAmount = Mathf.Clamp01(zoneWidth / 360f);
    }

    public void FlashSuccess() => ShowFlash(successFlashColor).Forget();
    public void FlashFail()    => ShowFlash(failFlashColor).Forget();

    private async UniTaskVoid ShowFlash(Color color)
    {
        _flashCts?.Cancel();
        _flashCts?.Dispose();
        _flashCts = new CancellationTokenSource();

        if (flashImage == null) return;

        flashImage.color = color;
        flashImage.gameObject.SetActive(true);

        await UniTask.Delay(
            System.TimeSpan.FromSeconds(_flashDuration),
            DelayType.UnscaledDeltaTime,
            cancellationToken: _flashCts.Token);

        if (!_flashCts.Token.IsCancellationRequested)
        {
            flashImage.gameObject.SetActive(false);
            flashImage.color = Color.clear;
        }
    }
}
