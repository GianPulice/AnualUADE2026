using TMPro;
using UnityEngine;

/// <summary>
/// VIEW of the group label in the list.
/// 8px text, dark grey, letter-spaced. Not clickable.
/// </summary>
public class GroupLabelView : MonoBehaviour
{
    [SerializeField] private SO_ItemCategoryConfig categoryConfig;
    [SerializeField] private TextMeshProUGUI labelText;

    public ItemCategory Category { get; private set; }

    public void Setup(ItemCategory category)
    {
        if (labelText != null)
            labelText.text = categoryConfig.Get(category).GroupLabel;
            Category = category;
    }
}
