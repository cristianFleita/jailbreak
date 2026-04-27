using UnityEngine;

[RequireComponent(typeof(ItemInventory))]
public class InventoryInput : MonoBehaviour
{
    [Header("Navigation")]
    public KeyCode nextKey     = KeyCode.L;
    public KeyCode previousKey = KeyCode.K;

    private ItemInventory inventory;

    void Awake()
    {
        inventory = GetComponent<ItemInventory>();
    }

    void Update()
    {
        if (InputSystemKey.WasPressedThisFrame(nextKey))
        {
            inventory.SelectNext();
        }
        else if (InputSystemKey.WasPressedThisFrame(previousKey))
        {
            inventory.SelectPrevious();
        }
    }
}
