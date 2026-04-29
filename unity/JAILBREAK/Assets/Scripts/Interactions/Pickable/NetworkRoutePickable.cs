using Jailbreak.Network;
using UnityEngine;

[RequireComponent(typeof(PickableItem))]
[RequireComponent(typeof(NetworkInteractable))]
public class NetworkRoutePickable : MonoBehaviour, IInteractable
{
    [Header("Route Item")]
    public string itemId = "route1_wrench";
    public string itemType = "route_tool";

    [Header("Interactable")]
    public string actionLabel = "Pick Up";
    public int priority = 20;
    public KeyCode interactKey = KeyCode.E;
    public string[] allowedInStates = null;

    [Header("Hold Point")]
    public string holdPointName = "mixamorig:RightHandIndex1";

    [Header("Drop Rules")]
    [Tooltip("Allow this item to be dropped directly from the player's hand with the drop key. Route tools keep this off; prank props such as soap can turn it on.")]
    public bool allowDropFromHand = false;

    [Header("Debug")]
    public bool debugLogs = false;

    public KeyCode InteractKey => interactKey;
    public string ActionLabel => actionLabel;
    public int Priority => priority;
    public Transform Transform => transform;
    public bool CanInteract => worldAvailable && !pendingPickup && !pickable.IsHeld;
    public string[] AllowedInStates => allowedInStates;
    public bool CanDropFromHand => allowDropFromHand;
    public string DebugState =>
        $"itemId={itemId} worldAvailable={worldAvailable} pendingPickup={pendingPickup} pendingStore={pendingStore} pendingThrow={pendingThrow} pendingStoreSlot={pendingStoreSlotIndex} isHeld={(pickable != null && pickable.IsHeld)} active={isActiveAndEnabled}";

    private PickableItem pickable;
    private NetworkInteractable networkInteractable;
    private HeldItemInput localHeldInput;
    private ItemInventory localInventory;
    private bool worldAvailable = true;
    private bool pendingPickup;
    private bool pendingStore;
    private bool pendingThrow;
    private int pendingStoreSlotIndex = -1;
    private bool subscribedNetwork;
    private bool subscribedGameState;
    private ItemStateBroadcastPayload latestState;

    private void Awake()
    {
        pickable = GetComponent<PickableItem>();
        networkInteractable = GetComponent<NetworkInteractable>();

        if (networkInteractable != null && string.IsNullOrEmpty(networkInteractable.networkId))
            networkInteractable.networkId = itemId;

        DebugRoute($"Awake layer={LayerMask.LayerToName(gameObject.layer)} colliders={GetComponentsInChildren<Collider>(true).Length} networkId={networkInteractable?.networkId ?? "null"}");
    }

    private void OnEnable()
    {
        SubscribeNetworkIfNeeded();
        SubscribeGameStateIfNeeded();
    }

    private void Start()
    {
        SubscribeNetworkIfNeeded();
        SubscribeGameStateIfNeeded();

        var net = NetworkManager.Instance;
        if (net != null && net.CachedItemStates.TryGetValue(itemId, out var cached))
            HandleItemState(cached);

        HandleLocalInventoryChanged();
    }

    private void Update()
    {
        SubscribeNetworkIfNeeded();
        SubscribeGameStateIfNeeded();
    }

    private void OnDisable()
    {
        var net = NetworkManager.Instance;
        if (net != null && subscribedNetwork)
        {
            net.OnItemStateEvent -= HandleItemState;
            net.OnNetworkErrorEvent -= HandleNetworkError;
        }
        subscribedNetwork = false;

        var gsm = GameStateManager.Instance;
        if (gsm != null && subscribedGameState)
            gsm.onLocalInventoryChanged.RemoveListener(HandleLocalInventoryChanged);
        subscribedGameState = false;
    }

    private void SubscribeNetworkIfNeeded()
    {
        if (subscribedNetwork) return;

        var net = NetworkManager.Instance;
        if (net == null) return;

        net.OnItemStateEvent += HandleItemState;
        net.OnNetworkErrorEvent += HandleNetworkError;
        subscribedNetwork = true;
    }

    private void SubscribeGameStateIfNeeded()
    {
        if (subscribedGameState) return;

        var gsm = GameStateManager.Instance;
        if (gsm == null) return;

        gsm.onLocalInventoryChanged.AddListener(HandleLocalInventoryChanged);
        subscribedGameState = true;
        HandleLocalInventoryChanged();
    }

    public void OnInteract(Collider source)
    {
        DebugRoute($"OnInteract source={source?.name ?? "null"} {DebugState}");
        if (!CanInteract || pendingPickup)
        {
            DebugRoute($"Pickup blocked canInteract={CanInteract} pendingPickup={pendingPickup}");
            return;
        }

        if (source == null)
        {
            DebugRoute("Pickup blocked: source collider missing");
            return;
        }

        CacheLocalPlayerRefs(source.transform.root);
        if (localHeldInput == null)
        {
            DebugRoute("Pickup blocked: local HeldItemInput not found on player root");
            return;
        }

        if (localHeldInput.HasItem)
        {
            DebugRoute("Pickup blocked: local hand already has an item");
            return;
        }

        pendingPickup = true;
        DebugRoute($"Sending item.pickup {itemId}");
        NetworkManager.Instance?.SendPlayerInteract(itemId, "item.pickup");
    }

    public void RequestStore(int slotIndex = -1)
    {
        DebugRoute($"RequestStore slot={slotIndex} {DebugState}");
        if (pendingStore) return;

        EnsureLocalPlayerRefs();
        if (localHeldInput == null || !localHeldInput.HasItem)
        {
            DebugRoute("Store blocked: local hand empty or HeldItemInput missing");
            return;
        }

        pendingStore = true;
        pendingStoreSlotIndex = slotIndex;
        DebugRoute($"Sending item.store {itemId} slot={slotIndex}");
        if (slotIndex >= 0)
            NetworkManager.Instance?.SendPlayerInteract(itemId, "item.store", slotIndex);
        else
            NetworkManager.Instance?.SendPlayerInteract(itemId, "item.store");
    }

    /// <summary>
    /// Voluntarily drop this route item back into the world. Server validates
    /// ownership, picks the drop position, and broadcasts <c>item:state</c>.
    /// The local client only flips a pending flag so we don't double-send while
    /// we wait for the authoritative response.
    /// </summary>
    public void RequestThrow()
    {
        DebugRoute($"RequestThrow {DebugState}");
        if (pendingThrow) return;

        var net = NetworkManager.Instance;
        if (net == null)
        {
            DebugRoute("Throw blocked: NetworkManager unavailable");
            return;
        }

        EnsureLocalPlayerRefs();

        var gsm = GameStateManager.Instance;
        var holdsStored = gsm != null && HasLocalInventorySlot(gsm.LocalInventorySlots);
        var holdsInHand = allowDropFromHand
            && ((gsm != null && gsm.LocalHeldItemId == itemId) || (localHeldInput != null && localHeldInput.HasItem && pickable.IsHeld));

        if (!holdsStored && !holdsInHand)
        {
            DebugRoute("Throw blocked: item must be stored in a slot before dropping");
            return;
        }

        pendingThrow = true;
        DebugRoute($"Sending item.throw {itemId}");
        net.SendPlayerInteract(itemId, "item.throw");
    }

    private void HandleItemState(ItemStateBroadcastPayload payload)
    {
        if (payload == null || payload.itemId != itemId) return;

        latestState = payload;
        pendingPickup = false;
        pendingStore = false;
        pendingThrow = false;

        DebugRoute($"item:state state={payload.state} holder={payload.holderUserId ?? "null"} spawnArea={payload.spawnAreaId ?? "null"} pos={payload.position}");
        ApplyNetworkState(payload);
        pendingStoreSlotIndex = -1;
    }

    private void HandleNetworkError(ErrorPayload _)
    {
        pendingPickup = false;
        pendingStore = false;
        pendingThrow = false;
        pendingStoreSlotIndex = -1;
        DebugRoute($"Network error cleared pending flags: {_?.message ?? "unknown"}");
    }

    private void HandleLocalInventoryChanged()
    {
        var gsm = GameStateManager.Instance;
        var net = NetworkManager.Instance;
        if (gsm == null || net == null) return;

        if (gsm.LocalHeldItemId == itemId)
        {
            ApplyNetworkState(BuildLocalState("held", net.LocalUserId));
            return;
        }

        if (HasLocalInventorySlot(gsm.LocalInventorySlots))
        {
            ApplyNetworkState(BuildLocalState("stored", net.LocalUserId));
            return;
        }

        if (latestState != null)
            ApplyNetworkState(latestState);
    }

    private ItemStateBroadcastPayload BuildLocalState(string state, string holderUserId)
    {
        var payload = new ItemStateBroadcastPayload
        {
            itemId = itemId,
            itemType = latestState != null && !string.IsNullOrEmpty(latestState.itemType) ? latestState.itemType : itemType,
            spawnAreaId = latestState != null ? latestState.spawnAreaId : null,
            position = latestState != null ? latestState.position : SVector3.FromUnity(transform.position),
        };

        payload.state = state;
        payload.holderUserId = holderUserId;
        return payload;
    }

    private bool HasLocalInventorySlot(InventorySlotData[] slots)
    {
        if (slots == null) return false;
        foreach (var slot in slots)
            if (slot != null && slot.itemId == itemId) return true;
        return false;
    }

    private void ApplyNetworkState(ItemStateBroadcastPayload payload)
    {
        var localUserId = NetworkManager.Instance != null ? NetworkManager.Instance.LocalUserId : null;
        var isLocalHolder = !string.IsNullOrEmpty(localUserId) && payload.holderUserId == localUserId;

        switch (payload.state)
        {
            case "spawned":
            case "dropped":
                ApplyWorldState(payload);
                break;

            case "held":
                if (isLocalHolder) ApplyLocalHeld();
                else HideWorldVisual();
                break;

            case "stored":
                if (isLocalHolder) ApplyLocalStored();
                else HideWorldVisual();
                break;

            case "respawning":
                HideWorldVisual();
                break;
        }
    }

    private void ApplyWorldState(ItemStateBroadcastPayload payload)
    {
        worldAvailable = true;
        EnsureLocalPlayerRefs();
        if (localHeldInput != null && pickable.IsHeld)
            localHeldInput.Detach();
        if (localInventory != null)
            localInventory.TryRemove(pickable);

        transform.SetParent(null);
        if (ShouldApplyPayloadPosition(payload))
            transform.position = payload.position.ToUnity();

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        pickable.SetWorldVisible(true);
    }

    private void ApplyLocalHeld()
    {
        worldAvailable = false;
        EnsureLocalPlayerRefs();

        if (localHeldInput == null)
        {
            HideWorldVisual();
            return;
        }

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        if (localHeldInput.HasItem && !pickable.IsHeld)
            localHeldInput.Detach();

        if (!pickable.IsHeld)
        {
            if (localInventory != null)
                localInventory.TryRemove(pickable);

            var holdPoint = ResolveHoldPoint(localHeldInput.transform.root);
            pickable.OnPickedUp(holdPoint);
            localHeldInput.Attach(pickable);
        }
        else
        {
            pickable.SetHeldVisible();
        }
    }

    private void ApplyLocalStored()
    {
        worldAvailable = false;
        EnsureLocalPlayerRefs();

        if (localHeldInput != null && pickable.IsHeld)
        {
            localHeldInput.Detach();
            localHeldInput.onItemStored.Invoke(pickable);
        }

        if (localInventory != null)
        {
            var slotIndex = ResolveLocalInventorySlotIndex();
            if (slotIndex >= 0)
                localInventory.SetAt(pickable, slotIndex);
            else if (!localInventory.Contains(pickable))
                localInventory.TryAdd(pickable);
        }

        pickable.OnStoredInInventory(false);
    }

    private int ResolveLocalInventorySlotIndex()
    {
        var gsm = GameStateManager.Instance;
        var slots = gsm != null ? gsm.LocalInventorySlots : null;
        if (slots != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null && slots[i].itemId == itemId)
                    return i;
            }
        }

        return pendingStoreSlotIndex >= 0 ? pendingStoreSlotIndex : -1;
    }

    private void HideWorldVisual()
    {
        worldAvailable = false;
        pickable.SetWorldVisible(false);
    }

    private void DebugRoute(string message)
    {
        if (!debugLogs) return;
        Debug.Log($"[NetworkRoutePickable] {name}: {message}", this);
    }

    private bool ShouldApplyPayloadPosition(ItemStateBroadcastPayload payload)
    {
        if (payload.state == "dropped") return true;
        if (!string.IsNullOrEmpty(payload.spawnAreaId)) return true;

        var p = payload.position;
        return Mathf.Abs(p.x) + Mathf.Abs(p.y) + Mathf.Abs(p.z) > 0.001f;
    }

    private void EnsureLocalPlayerRefs()
    {
        if (localHeldInput != null && localInventory != null) return;

        var root = FindLocalPlayerRoot();
        if (root != null)
            CacheLocalPlayerRefs(root);
    }

    private void CacheLocalPlayerRefs(Transform root)
    {
        if (root == null) return;
        localHeldInput = root.GetComponent<HeldItemInput>();
        localInventory = root.GetComponent<ItemInventory>();
    }

    private Transform FindLocalPlayerRoot()
    {
#if UNITY_2023_1_OR_NEWER
        var managers = Object.FindObjectsByType<InteractionManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
        var managers = Object.FindObjectsOfType<InteractionManager>();
#endif
        foreach (var manager in managers)
            if (manager != null && manager.enabled)
                return manager.transform.root;

        return null;
    }

    private Transform ResolveHoldPoint(Transform root)
    {
        if (root == null) return transform;
        if (string.IsNullOrEmpty(holdPointName)) return root;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == holdPointName) return t;

        return root;
    }
}
