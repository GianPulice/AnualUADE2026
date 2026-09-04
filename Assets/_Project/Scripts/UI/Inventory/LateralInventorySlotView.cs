using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Individual slot of the <see cref="LateralInventoryView"/>. Minimal design:
/// icon and name only, no expanded detail. Meant for quick use during
/// puzzle interaction Variant B.
///
/// SKELETON — the visual styling (per-category colors, hover, selection) will be
/// completed when Variant B is implemented.
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
