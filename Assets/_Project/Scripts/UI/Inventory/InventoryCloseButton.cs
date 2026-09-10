using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The title-bar close button of the inventory.
///
/// A persistent onClick listener cannot do this job: InventoryManagerUI lives in LevelUI.unity, not
/// in the Inventory Canvas prefab, and a prefab cannot reference a scene object. So the button
/// reaches it through the singleton instead.
/// </summary>
[RequireComponent(typeof(Button))]
[AddComponentMenu("WIRED/UI/Inventory Close Button")]
public class InventoryCloseButton : MonoBehaviour
{
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(HandleClick);
    }

    private void HandleClick()
    {
        if (InventoryManagerUI.Exists) InventoryManagerUI.Instance.CloseInventory();
    }
}
