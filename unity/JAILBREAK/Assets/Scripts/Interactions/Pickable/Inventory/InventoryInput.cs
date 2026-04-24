using UnityEngine;

[RequireComponent(typeof(ItemInventory))]
[RequireComponent(typeof(HeldItemInput))]
public class InventoryInput : MonoBehaviour
{
    [Header("Navigation")]
    public KeyCode nextKey     = KeyCode.RightArrow;
    public KeyCode previousKey = KeyCode.LeftArrow;

    private ItemInventory inventory;
    private HeldItemInput heldInput;

    void Awake()
    {
        inventory = GetComponent<ItemInventory>();
        heldInput = GetComponent<HeldItemInput>();
    }

    void Update()
    {
        if (InputSystemKey.WasPressedThisFrame(nextKey))
        {
            inventory.SelectNext();
            TryEquipSelected();
        }
        else if (InputSystemKey.WasPressedThisFrame(previousKey))
        {
            inventory.SelectPrevious();
            TryEquipSelected();
        }
    }
    
    private void TryEquipSelected()
    {
        var selected = inventory.GetAt(inventory.SelectedIndex);
        if (selected == null)
        {
            if (heldInput.HasItem) {
                heldInput.ForceStore(); 
            }
            return;
        }

        if (selected.GetComponent<NetworkRoutePickable>() != null)
            return;

        if (heldInput.HasItem)
        {
            if (inventory.IsFull())
            {
                heldInput.Throw(); 
            }
            else
            {
                heldInput.ForceStore();
            }

            if (heldInput.HasItem)
                return;
        }

        var newItem = inventory.TakeSelected();
        if (newItem != null)
        {
            newItem.gameObject.SetActive(true);
            var holdPoint = heldInput.ResolveCurrentHoldPoint();
        
            newItem.OnPickedUp(holdPoint); 
            heldInput.Attach(newItem);
        }
    }
}
