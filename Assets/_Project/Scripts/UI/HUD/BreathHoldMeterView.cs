using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The breath meter: shown while the player is inside a hiding spot, one pip per slice of the air
/// left in their lungs (<see cref="PlayerStateManager.BreathAir"/>), and a status line that tells
/// them the key and what the lungs are doing.
///
/// Read-only over the player: the hidden state owns the breath and the noise it makes, this only
/// draws it. Same polling approach as <see cref="ModuleTimerHUDView"/> through
/// <see cref="PlayerRegistry"/> — there is no per-frame event for the air draining.
///
/// The root carries a <see cref="ModalVisibilityGate"/> on its own CanvasGroup; this fades the
/// window's, so the two never fight over one alpha. Lives in HUDCanvas.prefab.
/// </summary>
[DisallowMultipleComponent]
public class BreathHoldMeterView : MonoBehaviour
{
    [SerializeField] private SO_UIThemeConfig theme;
    [SerializeField] private CanvasGroup windowGroup;

    [Tooltip("Left to right. Lit from the left while there is air.")]
    [SerializeField] private Graphic[] pips = new Graphic[0];

    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Text")]
    [Tooltip("{0} = the hold-breath key.")]
    [SerializeField] private string readyFormat = "[{0}] HOLD BREATH";
    [SerializeField] private string holdingText = "HOLDING...";
    [SerializeField] private string recoveringText = "RECOVERING";

    [Header("Behaviour")]
    [Tooltip("Below this much air the lit pips switch to the accent colour.")]
    [SerializeField, Range(0f, 1f)] private float lowAir = 0.3f;

    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.25f;

    private float alpha;
    private string readyText;

    private void Awake()
    {
        if (windowGroup != null)
        {
            windowGroup.alpha = 0f;
            windowGroup.interactable = false;
            windowGroup.blocksRaycasts = false;
        }
    }

    private void OnEnable()
    {
        // The binding, not a hard-coded F: the key can be rebound.
        string key = GameInput.HoldBreath != null ? GameInput.HoldBreath.GetBindingDisplayString(0) : string.Empty;
        readyText = string.Format(readyFormat, string.IsNullOrEmpty(key) ? "F" : key.ToUpperInvariant());
    }

    private void Update()
    {
        PlayerStateManager player = PlayerRegistry.Current;
        bool visible = player != null && player.CurrentHidingSpot != null && !player.IsDisabled;

        float target = visible ? 1f : 0f;
        alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime / fadeSeconds);
        if (windowGroup != null) windowGroup.alpha = alpha;

        if (!visible) return;

        float air = player.BreathAir;
        bool holding = player.IsHoldingBreath;
        DrawPips(air, holding);

        if (statusText == null) return;
        if (holding) SetStatus(holdingText, Primary);
        else if (air < 0.999f) SetStatus(recoveringText, Muted);
        else SetStatus(readyText, Secondary);
    }

    private void DrawPips(float air, bool holding)
    {
        int count = pips.Length;
        if (count == 0) return;

        int lit = Mathf.CeilToInt(Mathf.Clamp01(air) * count - 0.001f);
        Color on = air < lowAir ? Accent : holding ? Primary : Secondary;

        for (int i = 0; i < count; i++)
        {
            if (pips[i] == null) continue;
            pips[i].color = i < lit ? on : Disabled;
        }
    }

    private void SetStatus(string text, Color color)
    {
        if (statusText.text != text) statusText.text = text;
        statusText.color = color;
    }

    private Color Primary   => theme != null ? theme.TextPrimary : Color.white;
    private Color Secondary => theme != null ? theme.TextSecondary : new Color(0.8f, 0.8f, 0.8f);
    private Color Muted     => theme != null ? theme.TextMuted : Color.gray;
    private Color Disabled  => theme != null ? theme.TextDisabled : new Color(0.3f, 0.3f, 0.3f);
    private Color Accent    => theme != null ? theme.Accent : Color.white;
}
