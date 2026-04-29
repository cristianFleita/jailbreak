using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Animations.Rigging;

public class HeldItemInput : MonoBehaviour
{
    [Header("Input")]
    public KeyCode throwKey = KeyCode.Mouse0;
    public KeyCode storeKey = KeyCode.F;
    [Tooltip("Voluntarily drop the held route tool back into the world. Server-driven for route items.")]
    public KeyCode dropKey  = KeyCode.G;

    [Header("Hold Point")]
    public string holdPointName = "Hand_R";

    [Header("Animation Rigging")]
    public TwoBoneIKConstraint handConstraint;
    public Transform ikTarget;
    public float lerpSpeed = 10f;

    [Header("Throw Settings")]
    public Transform aimTransform;
    public float throwForce = 12f;

    [Header("Events")]
    public UnityEvent<PickableItem> onItemAttached;
    public UnityEvent<PickableItem> onItemThrown;
    public UnityEvent<PickableItem> onItemStored;

    private PickableItem heldItem;
    private ItemInventory inventory;
    private Transform cachedHoldPoint;
    private float targetWeight = 0f;

    public bool HasItem => heldItem != null;
    public KeyCode ThrowKey => throwKey;
    public KeyCode StoreKey => storeKey;
    public KeyCode DropKey  => dropKey;

    void Awake()
    {
        inventory = GetComponent<ItemInventory>();
        cachedHoldPoint = ResolveHoldPointInHierarchy();

        if (handConstraint != null)
        {
            handConstraint.weight = 0f;
        }
    }

    public void Attach(PickableItem item)
    {
        heldItem = item;
        targetWeight = 1f;
        onItemAttached.Invoke(item);
    }

    public void Detach()
    {
        heldItem = null;
        targetWeight = 0f;
    }

    public Transform ResolveCurrentHoldPoint()
    {
        return cachedHoldPoint != null ? cachedHoldPoint : transform;
    }

    void Update()
    {
        HandleIKWeight();

        // Route tools can only be dropped after being stored in a slot.
        if (InputSystemKey.WasPressedThisFrame(dropKey) && TryRouteDrop())
            return;

        if (heldItem == null) return;

        var routePickable = heldItem.GetComponent<NetworkRoutePickable>();
        if (routePickable != null)
        {
            if (InputSystemKey.WasPressedThisFrame(storeKey) && CanStoreInSelectedSlot())
                routePickable.RequestStore(SelectedSlotIndex());
            return;
        }

        if (InputSystemKey.WasPressedThisFrame(throwKey))
            Throw();
        else if (InputSystemKey.WasPressedThisFrame(storeKey))
            TryStore();
    }

    /// <summary>
    /// Drops the route tool in the selected inventory slot. Server is
    /// authoritative; the local client only sends the request and waits for
    /// item:state.
    /// </summary>
    private bool TryRouteDrop()
    {
        var heldRoute = heldItem != null ? heldItem.GetComponent<NetworkRoutePickable>() : null;
        if (heldRoute != null && heldRoute.CanDropFromHand)
        {
            heldRoute.RequestThrow();
            return true;
        }

        if (inventory != null)
        {
            var slot = inventory.GetAt(inventory.SelectedIndex);
            var route = slot != null ? slot.GetComponent<NetworkRoutePickable>() : null;
            if (route != null)
            {
                route.RequestThrow();
                return true;
            }
        }

        return false;
    }

    private void HandleIKWeight()
    {
        if (handConstraint == null) return;

        if (!Mathf.Approximately(handConstraint.weight, targetWeight))
        {
            handConstraint.weight = Mathf.Lerp(handConstraint.weight, targetWeight, Time.deltaTime * lerpSpeed);
        }
    }

    public void ForceStore()
    {
        if (heldItem == null) return;
        var routePickable = heldItem.GetComponent<NetworkRoutePickable>();
        if (routePickable != null)
        {
            if (!CanStoreInSelectedSlot()) return;
            routePickable.RequestStore(SelectedSlotIndex());
            return;
        }
        if (!CanStoreInSelectedSlot()) return;

        var item = heldItem;
        Detach();
        inventory.TryAddAt(item, inventory.SelectedIndex);
        item.OnStoredInInventory();
        onItemStored.Invoke(item);
    }

    public void Throw()
    {
        if (heldItem == null) return;
        if (heldItem.GetComponent<NetworkRoutePickable>() != null) return;

        var item = heldItem;
        var direction = ResolveThrowDirection();

        Detach();
        item.OnThrown(direction, throwForce);
        onItemThrown.Invoke(item);
    }
    

    private void TryStore()
    {
        if (heldItem != null && heldItem.GetComponent<NetworkRoutePickable>() != null)
        {
            if (!CanStoreInSelectedSlot()) return;
            heldItem.GetComponent<NetworkRoutePickable>().RequestStore(SelectedSlotIndex());
            return;
        }
        if (!CanStoreInSelectedSlot()) return;

        var item = heldItem;
        Detach();
        inventory.TryAddAt(item, inventory.SelectedIndex);
        item.OnStoredInInventory();
        onItemStored.Invoke(item);
    }

    private int SelectedSlotIndex()
    {
        return inventory != null ? inventory.SelectedIndex : -1;
    }

    private bool CanStoreInSelectedSlot()
    {
        return inventory != null
            && inventory.IsValidIndex(inventory.SelectedIndex)
            && !inventory.HasItemAt(inventory.SelectedIndex);
    }

    private Vector3 ResolveThrowDirection()
    {
        if (aimTransform != null)
            return aimTransform.forward;

        if (Camera.main != null)
            return Camera.main.transform.forward;

        return transform.forward;
    }

    private Transform ResolveHoldPointInHierarchy()
    {
        if (string.IsNullOrEmpty(holdPointName)) return transform;

        var all = GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
            if (t.name == holdPointName) return t;

        return transform;
    }
}
