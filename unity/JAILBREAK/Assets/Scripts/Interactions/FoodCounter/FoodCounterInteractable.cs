using Jailbreak.Network;
using UnityEngine;

/// <summary>
/// The cafeteria counter. When a prisoner (local player) presses E next to it,
/// they pick up a food plate: a prop is parented to their hand and they switch
/// to Carrying* animations. Drop-on-sink is handled by a separate interactable.
///
/// Guards cannot interact. Prisoners who are already carrying cannot pick up
/// another plate (CanInteract returns false).
///
/// Broadcasts via <see cref="NetworkInteractable"/> so remote clients replay
/// the plate-attach + animation on this player's avatar.
/// </summary>
[RequireComponent(typeof(NetworkInteractable))]
public class FoodCounterInteractable : MonoBehaviour, IInteractable
{
    public KeyCode InteractKey => KeyCode.E;
    public int Priority        => 10;
    public Transform Transform => transform;

    /// <summary>Null = universally available (not gated by state tags).</summary>
    public string[] AllowedInStates => null;

    public string ActionLabel => "Take food";

    /// <summary>
    /// Disabled for the local player when they are already carrying something,
    /// or when they are not a prisoner (guards don't use the counter).
    /// </summary>
    public bool CanInteract
    {
        get
        {
            var carry = FindLocalCarryFood();
            if (carry == null) return true; // unknown — let InteractionManager attempt
            return !carry.IsCarrying;
        }
    }

    public const string ActionTakeFood = "takeFood";

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
            Debug.LogWarning("[FoodCounter] Interactor has no CarryFoodInteraction — ignoring.");
            return;
        }
        if (carry.IsCarrying) return; // guard against double-press races

        carry.TryPickUp();
        Broadcast(ActionTakeFood);
    }

    /// <summary>Replays the action on a remote avatar (called by RemoteInteractionHandler).</summary>
    public void ApplyRemote(CarryFoodInteraction carry, string action)
    {
        if (carry == null) return;

        if (action == ActionTakeFood) carry.TryPickUp();
        // Future: leaveFood / drop actions handled on the sink interactable.
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Locate the local player's CarryFoodInteraction once. Used by CanInteract
    /// to hide the prompt after pickup. Cached so we don't FindObjectOfType per frame.
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
            Debug.LogWarning($"[FoodCounter] No NetworkInteractable on {gameObject.name}. Cannot broadcast '{action}'.");
            return;
        }

        var net = NetworkManager.Instance;
        if (net == null)
        {
            Debug.LogWarning($"[FoodCounter] No NetworkManager instance. Cannot broadcast '{action}' on '{networkInteractable.NetworkId}'.");
            return;
        }

        Debug.Log($"[FoodCounter] Broadcasting '{action}' on '{networkInteractable.NetworkId}'");
        net.SendPlayerAction(networkInteractable.NetworkId, action);
    }
}
