using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MainMenuView : BaseScreenView
{
    /// <summary>Alpha del label de "Load Game" cuando no hay partidas guardadas.</summary>
    private const float DISABLED_ALPHA = 0.35f;

    [SerializeField] private Button newGameBtn;
    [SerializeField] private Button loadGameBtn;
    [SerializeField] private Button settingsBtn;
    [SerializeField] private Button exitBtn;

    [Header("Estado deshabilitado")]
    [Tooltip("Opcional. CanvasGroup del botón Load Game para atenuarlo cuando no hay saves. " +
             "Si no se asigna, se agrega uno automáticamente en Awake.")]
    [SerializeField] private CanvasGroup loadGameCanvasGroup;

    public event Action OnNewGameClicked;
    public event Action OnLoadGameClicked;
    public event Action OnSettingsClicked;
    public event Action OnExitClicked;

    /// <summary>
    /// Habilita o deshabilita "Load Game". Sin partidas guardadas el botón no debe abrir
    /// el panel de slots: entrarías a una partida inventada.
    /// </summary>
    public void SetLoadGameInteractable(bool value)
    {
        if (loadGameBtn != null) loadGameBtn.interactable = value;

        if (loadGameCanvasGroup != null)
        {
            loadGameCanvasGroup.alpha = value ? 1f : DISABLED_ALPHA;
            // Sin esto, el botón deshabilitado sigue recibiendo hover/selección del
            // EventSystem y el ButtonHoverSweepEffect (IPointerEnter/ISelect) deja la
            // barra de sweep pegada como si estuviera seleccionado.
            loadGameCanvasGroup.blocksRaycasts = value;
        }

        // Si quedó seleccionado (foco de teclado/gamepad) al deshabilitarlo, forzar el
        // deselect para que el sweep effect vuelva a su estado oculto.
        if (!value && loadGameBtn != null && EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject == loadGameBtn.gameObject)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private void Awake()
    {
        // El fondo del botón es transparente en estado normal y el texto TMP no
        // reacciona al Color Tint del Button, así que interactable=false solo no
        // lo pone gris. Un CanvasGroup atenuando todo el botón (fondo + texto) sí.
        if (loadGameCanvasGroup == null && loadGameBtn != null)
        {
            loadGameCanvasGroup = loadGameBtn.GetComponent<CanvasGroup>();
            if (loadGameCanvasGroup == null)
                loadGameCanvasGroup = loadGameBtn.gameObject.AddComponent<CanvasGroup>();
        }

        // El ButtonGeneric trae un disabledColor rojizo (pensado para otro uso) que
        // Unity aplica solo con interactable=false, sin pasar por el CanvasGroup ni por
        // el hover: por eso el botón se veía "seleccionado" en rojo en vez de gris.
        // Lo neutralizamos para que el único efecto visual de deshabilitado sea el
        // dimming del CanvasGroup de arriba.
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
