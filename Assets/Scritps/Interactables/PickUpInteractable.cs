using UnityEngine;

public class PickupInteractable : BaseRangeInteractable
{
    [Header("Item")]
    [SerializeField] private SO_InventoryItem itemToPick;

    /// <summary>Item asignado a este pickup. Lo lee <see cref="ItemProximityHighlight"/>
    /// para resolver la categoría automáticamente sin duplicar el dropdown a mano.</summary>
    public SO_InventoryItem Item => itemToPick;


    public override string GetInteractText()
    {
        return itemToPick != null
            ? $"Presione la tecla 'E' para agarrar {itemToPick.ItemName}"
            : "Presione la tecla 'E' para agarrar";
    }

    protected override bool CanInteractInCloseRange()
    {
        return itemToPick != null;
    }

    protected override void OnInteract()
    {
        AudioManager.Instance.PlaySFX("PickUpInteractable");
        InventoryManager.Instance.AddItem(itemToPick);
        Destroy(gameObject);
    }

    public override bool IsRepeatable()
    {
        return false;
    }
}
