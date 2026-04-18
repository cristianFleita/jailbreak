using Jailbreak.Network;
using UnityEngine;

[RequireComponent(typeof(SitPoint))]
public class SitPointInteractable : MonoBehaviour, IInteractable
{
    public KeyCode InteractKey => KeyCode.E;
    public int Priority        => 10;
    public Transform Transform => transform;

    /// <summary>
    /// Null means "available in any state". The sit-prompt shows when free OR when
    /// the local player is the current occupant (stand-up). We do NOT restrict to a
    /// specific active-state tag so that sitting down from the default (no-tag) state
    /// is possible. The InteractionManager's IsAllowed() treats null/empty as always-allowed.
    /// </summary>
    public string[] AllowedInStates => null;

    public string ActionLabel => IsOccupiedBySelf() ? "Stand up" : "Sit";
    public bool CanInteract   => !sitPoint.isOccupied || IsOccupiedBySelf();

    public const string ActionSit   = "sit";
    public const string ActionStand = "stand";

    private SitPoint sitPoint;
    private SitInteraction occupant;
    private NetworkInteractable networkInteractable;

    void Awake()
    {
        sitPoint            = GetComponent<SitPoint>();
        networkInteractable = GetComponent<NetworkInteractable>();
    }

    /// <summary>
    /// Called by <see cref="InteractionManager"/> when the local player presses the
    /// interact key while standing next to this sit point.
    /// Executes the sit-down or stand-up locally, then broadcasts the action so
    /// remote clients can replay it on the matching avatar.
    /// </summary>
    public void OnInteract(Collider source)
    {
        SitInteraction sit = source.GetComponentInParent<SitInteraction>();
        if (sit == null) return;

        string action;
        if (!IsOccupiedBySelf())
        {
            occupant = sit;
            sit.TrySitDown(sitPoint);
            action = ActionSit;
        }
        else
        {
            sit.TryStandUp();
            action = ActionStand;
        }

        Broadcast(action);
    }

    /// <summary>
    /// Replays a remote player's sit or stand action on their avatar.
    /// Called by <see cref="Jailbreak.Player.RemoteInteractionHandler"/> when a
    /// <c>player:action</c> broadcast arrives from the server.
    /// </summary>
    public void ApplyRemote(SitInteraction sit, string action)
    {
        if (sit == null) return;

        if (action == ActionSit)
        {
            occupant = sit;
            sit.TrySitDown(sitPoint);
        }
        else if (action == ActionStand)
        {
            sit.TryStandUp();
            // Clear local occupancy reference so the seat is visually freed.
            if (occupant == sit) occupant = null;
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    void Broadcast(string action)
    {
        if (networkInteractable == null)
        {
            Debug.LogWarning($"[SitPointInteractable] No NetworkInteractable found on {gameObject.name}. Cannot broadcast '{action}'.");
            return;
        }

        var net = NetworkManager.Instance;
        if (net == null)
        {
            Debug.LogWarning($"[SitPointInteractable] No NetworkManager instance. Cannot broadcast '{action}' on '{networkInteractable.NetworkId}'.");
            return;
        }

        Debug.Log($"[SitPointInteractable] Broadcasting '{action}' on '{networkInteractable.NetworkId}'");
        net.SendPlayerAction(networkInteractable.NetworkId, action);
    }

    bool IsOccupiedBySelf() => occupant != null && occupant.IsSitting;
}
