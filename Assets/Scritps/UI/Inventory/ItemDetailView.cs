using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VIEW del panel derecho de detalles del ítem seleccionado.
/// 
/// Responsabilidades:
///   - Mostrar nombre, tipo, descripción y parámetros del ítem seleccionado
///   - Exponer botones de Usar y Descartar
///   - Notificar al Controller las acciones del jugador
///   - NO contiene lógica de negocio
/// </summary>
public class ItemDetailView : MonoBehaviour
{
    // ── Header ───────────────────────────────────────────────────────────────

    [Header("Header del ítem")]
    [SerializeField] private Image itemColorBox;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI itemTypeText;
    [SerializeField] private Image itemTypeBackground;  // El fondo verde del tipo en tu UI

    [Header("Descripción")]
    [SerializeField] private TextMeshProUGUI descriptionText;

    [Header("Parámetros del ítem")]
    [SerializeField] private ItemParameterRow[] parameterRows;  // Array de 3 filas de parámetros

    [Header("Acciones")]
  //  [SerializeField] private Button useButton;
    [SerializeField] private Button discardButton;
    [SerializeField] private TextMeshProUGUI discardButtonLabel;

    [Header("Estado vacío")]
   // [SerializeField] private GameObject noSelectionPanel;   // Panel "selecciona un ítem"
   // [SerializeField] private GameObject detailContentPanel; // Panel con el contenido real

    [Header("Item Colors")]
    [SerializeField] private Color keysColor;
    [SerializeField] private Color componentColor;
    [SerializeField] private Color notesColor;
    [SerializeField] private Color esentialColor;

    // ── Unity ────────────────────────────────────────────────────────────────

    void Awake()
    {
 //       useButton?.onClick.AddListener(OnUseClicked);
        discardButton?.onClick.AddListener(OnDiscardClicked);
    }

    void OnDestroy()
    {
    //    useButton?.onClick.RemoveListener(OnUseClicked);
        discardButton?.onClick.RemoveListener(OnDiscardClicked);
    }

    // ── API pública ──────────────────────────────────────────────────────────

    /// <summary>Muestra los detalles del ítem seleccionado.</summary>
    public void ShowDetail(SO_InventoryItem item)
    {
        if (item == null)
        {
            ClearDetail();
            return;
        }

      //  noSelectionPanel?.SetActive(false);
      //  detailContentPanel?.SetActive(true);

        // Nombre y tipo
        if (itemNameText != null)    itemNameText.text = item.ItemName;
        if (itemTypeText != null)    itemTypeText.text = item.ItemType.ToString();

        // Color de tipo
        Color typeColor = GetColorForItemType(item.ItemType);
        if (itemColorBox != null) itemColorBox.color = typeColor;
        if (itemTypeBackground != null) itemTypeBackground.color = typeColor;

        //// Descripción
        if (descriptionText != null)
            descriptionText.text = item.Description;

        // Parámetros (rellenar las filas disponibles)
        if (parameterRows != null && item.Parameters != null)
        {
            for (int i = 0; i < parameterRows.Length; i++)
            {
                if (i < item.Parameters.Length)
                {
                    parameterRows[i].SetParameter(item.Parameters[i].ParameterName,
                                                  item.Parameters[i].ParameterValue);
                    parameterRows[i].gameObject.SetActive(true);
                }
                else
                {
                    parameterRows[i].gameObject.SetActive(false);
                }
            }
        }

        // Label del botón de descarte
        if (discardButtonLabel != null)
            discardButtonLabel.text = $"DISCARD {item.ItemName.ToUpper()}";
    }

    /// <summary>Limpia el panel de detalle y muestra el estado vacío.</summary>
    public void ClearDetail()
    {
      //  noSelectionPanel?.SetActive(true);
      //  detailContentPanel?.SetActive(false);
    }

    // ── Privados ─────────────────────────────────────────────────────────────

    private void OnUseClicked()
    {
        InventoryManagerUI.Instance.UseSelectedItem();
    }

    private void OnDiscardClicked()
    {
        InventoryManagerUI.Instance.DiscardSelectedItem();
    }

    private Color GetColorForItemType(ItemType type)
    {
        return type switch
        {
            ItemType.Keys => keysColor, //Rojo
            ItemType.Components => componentColor, // Verde
            ItemType.Notes => notesColor,  // Azul
            ItemType.Essentials => esentialColor,  //Amarillo     
            _ => Color.white
        };
    }
}
