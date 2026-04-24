using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Animations.Rigging;

public class HeldItemInput : MonoBehaviour
{
    [Header("Input")]
    public KeyCode throwKey = KeyCode.Mouse0;
    public KeyCode storeKey = KeyCode.F;

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

        if (heldItem == null) return;

        if (Input.GetKeyDown(throwKey))
            Throw();
        else if (Input.GetKeyDown(storeKey))
            TryStore();
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
        if (inventory == null || inventory.IsFull()) return;

        var item = heldItem;
        Detach();
        inventory.TryAdd(item);
        item.OnStoredInInventory();
        onItemStored.Invoke(item);
    }

    public void Throw()
    {
        var item = heldItem;
        var direction = ResolveThrowDirection();

        Detach();
        item.OnThrown(direction, throwForce);
        onItemThrown.Invoke(item);
    }
    

    private void TryStore()
    {
        if (inventory == null || inventory.IsFull()) return;

        var item = heldItem;
        Detach();
        inventory.TryAdd(item);
        item.OnStoredInInventory();
        onItemStored.Invoke(item);
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