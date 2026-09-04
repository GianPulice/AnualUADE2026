using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MainMenuView : BaseScreenView
{
    /// <summary>Alpha of the "Load Game" label when there are no saved games.</summary>
    private const float DISABLED_ALPHA = 0.35f;

    [SerializeField] private Button newGameBtn;
    [SerializeField] private Button loadGameBtn;
    [SerializeField] private Button settingsBtn;
    [SerializeField] private Button exitBtn;

    [Header("Disabled state")]
    [Tooltip("Optional. CanvasGroup of the Load Game button used to dim it when there are no saves. " +
             "If not assigned, one is added automatically in Awake.")]
    [SerializeField] private CanvasGroup loadGameCanvasGroup;

    public event Action OnNewGameClicked;
    public event Action OnLoadGameClicked;
    public event Action OnSettingsClicked;
    public event Action OnExitClicked;

    /// <summary>
    /// Enables or disables "Load Game". With no saved games the button must not open the
    /// slots panel: you would enter a made-up run.
    /// </summary>
    public void SetLoadGameInteractable(bool value)
    {
        if (loadGameBtn != null) loadGameBtn.interactable = value;

        if (loadGameCanvasGroup != null)
        {
            loadGameCanvasGroup.alpha = value ? 1f : DISABLED_ALPHA;
            // Without this, the disabled button still receives hover/selection from the
            // EventSystem and ButtonHoverSweepEffect (IPointerEnter/ISelect) leaves the
            // sweep bar stuck as if it were selected.
            loadGameCanvasGroup.blocksRaycasts = value;
        }

        // If it was left selected (keyboard/gamepad focus) when disabled, force the
        // deselect so the sweep effect returns to its hidden state.
        if (!value && loadGameBtn != null && EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject == loadGameBtn.gameObject)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private void Awake()
    {
        // The button's background is transparent in the normal state and TMP text does not
        // react to the Button's Color Tint, so interactable=false alone does not grey it
        // out. A CanvasGroup dimming the whole button (background + text) does.
        if (loadGameCanvasGroup == null && loadGameBtn != null)
        {
            loadGameCanvasGroup = loadGameBtn.GetComponent<CanvasGroup>();
            if (loadGameCanvasGroup == null)
                loadGameCanvasGroup = loadGameBtn.gameObject.AddComponent<CanvasGroup>();
        }

        // ButtonGeneric ships with a reddish disabledColor (meant for another use) that
        // Unity applies only with interactable=false, bypassing the CanvasGroup and the
        // hover: that is why the button looked "selected" in red instead of grey.
        // We neutralize it so the only visual disabled effect is the CanvasGroup dimming above.
        if (loadGameBtn != null)
        {
            ColorBlock colors = loadGameBtn.colors;
            Color normal = colors.normalColor;
            colors.disabledColor = new Color(normal.r, normal.g, normal.b, 0f);
            loadGameBtn.colors = colors;
        }

        newGameBtn.onClick.AddListener(() => OnNewGameClicked?.Invoke());
        loadGameBtn.onClick.AddListener(() => OnLoadGameClicked?.Invoke());
        settingsBtn.onClick.AddListener(() => OnSettingsClicked?.Invoke());
        exitBtn.onClick.AddListener(() => OnExitClicked?.Invoke());
    }
    private void OnDestroy()
    {
        newGameBtn.onClick.RemoveAllListeners();
        loadGameBtn.onClick.RemoveAllListeners();
        settingsBtn.onClick.RemoveAllListeners();
        exitBtn.onClick.RemoveAllListeners();
    }
}
