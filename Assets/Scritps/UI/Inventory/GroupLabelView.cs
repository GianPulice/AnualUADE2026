using TMPro;
using UnityEngine;

/// <summary>
/// VIEW de la etiqueta de grupo en la lista
/// Texto 8px, gris oscuro, letra espaciada. No es clickeable.
/// </summary>
public class GroupLabelView : MonoBehaviour
{
    [SerializeField] private SO_ItemCategoryConfig categoryConfig;
    [SerializeField] private TextMeshProUGUI labelText;

    public void Setup(ItemCategory category)
    {
        if (labelText != null)
            labelText.text = categoryConfig.Get(category).GroupLabel;
    }
}
