using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// View of the Skill-Check (Central Puzzle 2).
// Requires in the hierarchy:
//   - NeedleTransform  : Transform that rotates (child of the circle)
//   - SuccessZoneImage : Image (Filled, Radial360) marking the valid zone
//   - CounterText      : TextMeshProUGUI with "X / Y"
//   - FlashImage       : semi-transparent fullscreen Image (alpha 0 by default)
public class SkillCheckView : BaseScreenView
{
    [Header("Needle")]
    [SerializeField] private Transform needleTransform;

    [Header("Success zone")]
    [SerializeField] private RectTransform successZoneRoot;
    [SerializeField] private Image successZoneImage;

    [Header("Counter")]
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

    // Called every frame by the controller while the check is running.
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

    // zoneStartAngle: where the zone starts (rotation of the root).
    // zoneWidth: arc in degrees -> fillAmount = width/360.
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
