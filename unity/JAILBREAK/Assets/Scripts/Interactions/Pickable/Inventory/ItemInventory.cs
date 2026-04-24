using UnityEngine;
using UnityEngine.Events;

public class ItemInventory : MonoBehaviour
{
    [Header("Config")]
    public int slotCount = 4;

    [Header("Events")]
    public UnityEvent<int, PickableItem> onItemAdded;
    public UnityEvent<int, PickableItem> onItemRemoved;
    public UnityEvent<int>               onSlotSelected;

    private PickableItem[] slots;
    private int            selectedIndex;

    public int            SlotCount     => slotCount;
    public int            SelectedIndex => selectedIndex;
    public PickableItem[] Slots         => slots;

    void Awake()
    {
        slots = new PickableItem[slotCount];
    }

    public bool TryAdd(PickableItem item)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null) continue;
            slots[i] = item;
            onItemAdded.Invoke(i, item);
            return true;
        }
        return false;
    }

    public PickableItem TakeAt(int index)
    {
        if (index < 0 || index >= slots.Length) return null;
        if (slots[index] == null) return null;

        var item     = slots[index];
        slots[index] = null;
        onItemRemoved.Invoke(index, item);
        return item;
    }

    public PickableItem TakeSelected() => TakeAt(selectedIndex);

    public bool HasItemAt(int index)
    {
        if (index < 0 || index >= slots.Length) return false;
        return slots[index] != null;
    }

    public bool IsFull()
    {
        foreach (var slot in slots)
            if (slot == null) return false;
        return true;
    }

    public void SelectNext()
    {
        selectedIndex = (selectedIndex + 1) % slots.Length;
        onSlotSelected.Invoke(selectedIndex);
    }

    public void SelectPrevious()
    {
        selectedIndex = (selectedIndex - 1 + slots.Length) % slots.Length;
        onSlotSelected.Invoke(selectedIndex);
    }
}