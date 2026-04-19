using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Player
{
    /// <summary>
    /// Lives on remote player GameObjects. Listens to <c>player:action</c>
    /// broadcasts forwarded by the server and replays the corresponding interaction
    /// animation (sit/stand/etc.) on this avatar.
    ///
    /// Position is still driven by <see cref="RemotePlayerSync"/> (interpolation).
    ///
    /// To add support for a new interaction type:
    ///   1. Add a handler branch in <see cref="HandleAction"/>.
    ///   2. Implement an <c>ApplyRemote</c> method on the relevant interactable.
    /// </summary>
    public class RemoteInteractionHandler : MonoBehaviour
    {
        /// <summary>The server-assigned player id this avatar represents.</summary>
        public string PlayerId { get; set; }

        private SitInteraction sitInteraction;
        private CarryFoodInteraction carryFoodInteraction;

        void Awake()
        {
            sitInteraction       = GetComponent<SitInteraction>();
            carryFoodInteraction = GetComponent<CarryFoodInteraction>();
        }

        void OnEnable()
        {
            var net = NetworkManager.Instance;
            if (net != null) net.OnPlayerActionEvent += HandleAction;
        }

        void OnDisable()
        {
            var net = NetworkManager.Instance;
            if (net != null) net.OnPlayerActionEvent -= HandleAction;

            // Free any occupied seats if this remote player disconnects mid-sit.
            sitInteraction?.ForceReset();
            // Drop any held prop so the plate prefab doesn't leak when the avatar despawns.
            carryFoodInteraction?.ForceReset();
        }

        // ─── Broadcast handler ────────────────────────────────────────────────

        void HandleAction(PlayerActionBroadcast data)
        {
            if (data == null || string.IsNullOrEmpty(PlayerId)) return;
            if (data.playerId != PlayerId) return; // Not for this avatar

            var ni = NetworkInteractable.Find(data.objectId);
            if (ni == null)
            {
                Debug.LogWarning($"[RemoteInteract] Unknown objectId '{data.objectId}' " +
                                 $"for player '{PlayerId}' action '{data.action}'");
                return;
            }

            // ── Sit / Stand ──────────────────────────────────────────────────
            var sitPoint = ni.GetComponent<SitPointInteractable>();
            if (sitPoint != null && sitInteraction != null)
            {
                sitPoint.ApplyRemote(sitInteraction, data.action);
                Debug.Log($"[RemoteInteract] '{PlayerId}' → {data.action} on '{data.objectId}'");
                return;
            }

            // ── Food counter pickup ──────────────────────────────────────────
            var foodCounter = ni.GetComponent<FoodCounterInteractable>();
            if (foodCounter != null && carryFoodInteraction != null)
            {
                foodCounter.ApplyRemote(carryFoodInteraction, data.action);
                Debug.Log($"[RemoteInteract] '{PlayerId}' → {data.action} on '{data.objectId}'");
                return;
            }

            // ── Future interaction types: add branches here ──────────────────

            Debug.LogWarning($"[RemoteInteract] No handler for action '{data.action}' " +
                             $"on object '{data.objectId}' (player '{PlayerId}')");
        }
    }
}
