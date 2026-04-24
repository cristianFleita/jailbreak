using System.Text;
using UnityEngine;
using TMPro;

public class InventoryHUD : MonoBehaviour
{
    [Header("References")]
    public ItemInventory   inventory;
    public HeldItemInput   heldInput;
    public InventoryInput  inventoryInput;

    [Header("Text Elements")]
    public TextMeshProUGUI inventoryText;
    public TextMeshProUGUI heldActionsText;

    void Awake()
    {
        inventory.onItemAdded.AddListener((i, item) => RefreshInventoryText());
        inventory.onItemRemoved.AddListener((i, item) => RefreshInventoryText());
        inventory.onSlotSelected.AddListener(i => RefreshInventoryText());

        heldInput.onItemAttached.AddListener(_ => RefreshHeldText());
        heldInput.onItemThrown.AddListener(_ => RefreshHeldText());
        heldInput.onItemStored.AddListener(_ => RefreshHeldText());

        inventory.onItemAdded.AddListener((i, item) => RefreshHeldText());
        inventory.onItemRemoved.AddListener((i, item) => RefreshHeldText());
    }

    void Start()
    {
        RefreshInventoryText();
        RefreshHeldText();
    }

    private void RefreshInventoryText()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"[{inventoryInput.previousKey}] / [{inventoryInput.nextKey}] Navigate");
        sb.AppendLine();

        for (int i = 0; i < inventory.SlotCount; i++)
        {
            bool   isSelected = i == inventory.SelectedIndex;
            var    item       = inventory.Slots[i];
            string itemName   = item != null ? item.name : "empty";
            string prefix     = isSelected ? "> " : "  ";

            sb.AppendLine($"{prefix}[{i + 1}] {itemName}");
        }

        inventoryText.text = sb.ToString();
    }

    private void RefreshHeldText()
    {
        if (!heldInput.HasItem)
        {
            heldActionsText.text = string.Empty;
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[{heldInput.ThrowKey}] Throw");
        sb.AppendLine(inventory.IsFull()
            ? "Inventory full"
            : $"[{heldInput.StoreKey}] Store");

        heldActionsText.text = sb.ToString();
    }
}