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
        slots = new PickableItem[Mathf.Max(0, slotCount)];
    }

    public bool TryAdd(PickableItem item)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (TryAddAt(item, i)) return true;
        }
        return false;
    }

    public bool TryAddAt(PickableItem item, int index)
    {
        if (item == null) return false;
        if (!IsValidIndex(index)) return false;
        if (slots[index] != null) return false;

        slots[index] = item;
        onItemAdded.Invoke(index, item);
        return true;
    }

    public bool SetAt(PickableItem item, int index)
    {
        if (item == null) return false;
        if (!IsValidIndex(index)) return false;

        for (int i = 0; i < slots.Length; i++)
        {
            if (i == index || slots[i] != item) continue;
            slots[i] = null;
            onItemRemoved.Invoke(i, item);
        }

        if (slots[index] == item) return true;

        var previous = slots[index];
        if (previous != null)
            onItemRemoved.Invoke(index, previous);

        slots[index] = item;
        onItemAdded.Invoke(index, item);
        return true;
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

    public PickableItem GetAt(int index)
    {
        if (index < 0 || index >= slots.Length) return null;
        return slots[index];
    }

    public bool Contains(PickableItem item)
    {
        if (item == null) return false;
        foreach (var slot in slots)
            if (slot == item) return true;
        return false;
    }

    public bool TryRemove(PickableItem item)
    {
        if (item == null) return false;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != item) continue;
            slots[i] = null;
            onItemRemoved.Invoke(i, item);
            return true;
        }

        return false;
    }

    public bool HasItemAt(int index)
    {
        if (!IsValidIndex(index)) return false;
        return slots[index] != null;
    }

    public bool IsValidIndex(int index)
    {
        return slots != null && index >= 0 && index < slots.Length;
    }

    public bool IsFull()
    {
        foreach (var slot in slots)
            if (slot == null) return false;
        return true;
    }

    public void SelectNext()
    {
        if (slots.Length == 0) return;
        selectedIndex = (selectedIndex + 1) % slots.Length;
        onSlotSelected.Invoke(selectedIndex);
    }

    public void SelectPrevious()
    {
        if (slots.Length == 0) return;
        selectedIndex = (selectedIndex - 1 + slots.Length) % slots.Length;
        onSlotSelected.Invoke(selectedIndex);
    }
}
