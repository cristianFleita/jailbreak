using Jailbreak.Network;
using UnityEngine;

/// <summary>
/// The cafeteria sink. When a prisoner carrying a food plate presses E next
/// to it, they drop the plate: the held prop is destroyed and the player
/// returns to non-carrying animations.
///
/// Mirrors <see cref="FoodCounterInteractable"/> — inverse gating: this
/// interactable is only available while the local player IS carrying.
///
/// Broadcasts via <see cref="NetworkInteractable"/> so remote clients
/// replay the put-down animation on this player's avatar.
/// </summary>
[RequireComponent(typeof(NetworkInteractable))]
public class SinkInteractable : MonoBehaviour, IInteractable
{
    public KeyCode InteractKey => KeyCode.E;
    public int Priority        => 10;
    public Transform Transform => transform;

    /// <summary>Null = universally available (not gated by state tags).</summary>
    public string[] AllowedInStates => null;

    public string ActionLabel => "Leave food";

    /// <summary>Only available when the local player is currently carrying a plate.</summary>
    public bool CanInteract
    {
        get
        {
            var carry = FindLocalCarryFood();
            if (carry == null) return false;
            return carry.IsCarrying;
        }
    }

    public const string ActionLeaveFood = "leaveFood";

    private NetworkInteractable networkInteractable;
    private CarryFoodInteraction cachedLocalCarry;

    void Awake()
    {
        networkInteractable = GetComponent<NetworkInteractable>();
    }

    // ─── Interaction entry points ────────────────────────────────────────────

    /// <summary>Called by InteractionManager on the local player.</summary>
    public void OnInteract(Collider source)
    {
        CarryFoodInteraction carry = source.GetComponentInParent<CarryFoodInteraction>();
        if (carry == null)
        {
            Debug.LogWarning("[Sink] Interactor has no CarryFoodInteraction — ignoring.");
            return;
        }
        if (!carry.IsCarrying) return; // guard against double-press races

        carry.TryDrop();
        Broadcast(ActionLeaveFood);
    }

    /// <summary>Replays the action on a remote avatar (called by RemoteInteractionHandler).</summary>
    public void ApplyRemote(CarryFoodInteraction carry, string action)
    {
        if (carry == null) return;

        if (action == ActionLeaveFood) carry.TryDrop();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Locate the local player's CarryFoodInteraction once. Cached so we don't
    /// FindObjectOfType per frame from CanInteract.
    /// </summary>
    CarryFoodInteraction FindLocalCarryFood()
    {
        if (cachedLocalCarry != null) return cachedLocalCarry;

#if UNITY_2023_1_OR_NEWER
        var all = Object.FindObjectsByType<CarryFoodInteraction>(FindObjectsSortMode.None);
#else
        var all = Object.FindObjectsOfType<CarryFoodInteraction>();
#endif
        foreach (var c in all)
        {
            // Local player is the one with an InteractionManager (remotes get it stripped).
            if (c.GetComponent<InteractionManager>() != null)
            {
                cachedLocalCarry = c;
                return c;
            }
        }
        return null;
    }

    void Broadcast(string action)
    {
        if (networkInteractable == null)
        {
            Debug.LogWarning($"[Sink] No NetworkInteractable on {gameObject.name}. Cannot broadcast '{action}'.");
            return;
        }

        var net = NetworkManager.Instance;
        if (net == null)
        {
            Debug.LogWarning($"[Sink] No NetworkManager instance. Cannot broadcast '{action}' on '{networkInteractable.NetworkId}'.");
            return;
        }

        Debug.Log($"[Sink] Broadcasting '{action}' on '{networkInteractable.NetworkId}'");
        net.SendPlayerAction(networkInteractable.NetworkId, action);
    }
}
