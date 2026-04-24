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
        if (Input.GetKeyDown(nextKey))
        {
            inventory.SelectNext();
            TryEquipSelected();
        }
        else if (Input.GetKeyDown(previousKey))
        {
            inventory.SelectPrevious();
            TryEquipSelected();
        }
    }
    
    private void TryEquipSelected()
    {
        if (!inventory.HasItemAt(inventory.SelectedIndex)) 
        {
            if (heldInput.HasItem) {
                heldInput.ForceStore(); 
            }
            return;
        }

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