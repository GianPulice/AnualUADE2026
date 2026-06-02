using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Slot individual del <see cref="LateralInventoryView"/>. Diseño minimalista:
/// solo icono y nombre, sin detalle expandido. Pensado para uso rápido durante
/// la Variante B de interacción con puzzles.
///
/// ESQUELETO — el styling visual (colores por categoría, hover, selección) se
/// completará cuando se implemente la Variante B.
/// </summary>
public class LateralInventorySlotView : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private Button button;

    public event Action<SO_InventoryItem> OnClicked;

    private SO_InventoryItem _item;

    public SO_InventoryItem Item => _item;

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(HandleClick);
    }

    public void Bind(SO_InventoryItem item)
    {
        _item = item;
        if (item == null) return;

        if (iconImage != null) iconImage.sprite = item.ItemIcon;
        if (nameLabel != null) nameLabel.text = item.ItemName;
    }

    private void HandleClick() => OnClicked?.Invoke(_item);
}
