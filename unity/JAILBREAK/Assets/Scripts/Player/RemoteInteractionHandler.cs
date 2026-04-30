using Jailbreak.Network;
using Jailbreak.Interactions.Route1;
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
        private CarryClothesInteraction carryClothesInteraction;
        private CarryFoldedClothesInteraction carryFoldedClothesInteraction;
        private FlashlightController flashlightController;

        void Awake()
        {
            sitInteraction                = GetComponent<SitInteraction>();
            carryFoodInteraction          = GetComponent<CarryFoodInteraction>();
            carryClothesInteraction       = GetComponent<CarryClothesInteraction>();
            carryFoldedClothesInteraction = GetComponent<CarryFoldedClothesInteraction>();
            flashlightController          = GetComponentInChildren<FlashlightController>();
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
            // Drop any held prop so the plate/clothes prefabs don't leak when the avatar despawns.
            carryFoodInteraction?.ForceReset();
            carryClothesInteraction?.ForceReset();
            carryFoldedClothesInteraction?.ForceReset();
        }

        // ─── Broadcast handler ────────────────────────────────────────────────

        void HandleAction(PlayerActionBroadcast data)
        {
            if (data == null || string.IsNullOrEmpty(PlayerId)) return;
            if (data.playerId != PlayerId) return; // Not for this avatar

            // ── Flashlight toggle (remote sync) ────────────────────────────
            if (data.objectId == "flashlight_system")
            {
                if (flashlightController != null)
                {
                    bool on = data.action == "flashlight_on";
                    flashlightController.SetFlashlightOn(on);
                    Debug.Log($"[RemoteInteract] '{PlayerId}' → flashlight {(on ? "ON" : "OFF")}");
                }
                return;
            }

            // ── Emote system (remote emote animations) ──────────────────────
            // EmoteSystem broadcasts with objectId="emote_system" — this is
            // NOT a scene NetworkInteractable, so handle before the NI lookup.
            if (data.objectId == "emote_system")
            {
                var animator = GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    if (data.action.StartsWith("emote_start:"))
                    {
                        string animState = data.action.Substring("emote_start:".Length);
                        animator.CrossFadeInFixedTime(animState, 0.15f, 0);
                        Debug.Log($"[RemoteInteract] '{PlayerId}' → emote '{animState}'");
                    }
                    else if (data.action == "emote_stop")
                    {
                        animator.CrossFadeInFixedTime("Idle", 0.25f, 0);
                        Debug.Log($"[RemoteInteract] '{PlayerId}' → emote stop");
                    }
                }
                return;
            }

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
                foodCounter.ApplyRemote(transform, carryFoodInteraction, data.action);
                Debug.Log($"[RemoteInteract] '{PlayerId}' → {data.action} on '{data.objectId}'");
                return;
            }

            // ── Sink drop ────────────────────────────────────────────────────
            var sink = ni.GetComponent<SinkInteractable>();
            if (sink != null && carryFoodInteraction != null)
            {
                sink.ApplyRemote(transform, carryFoodInteraction, data.action);
                Debug.Log($"[RemoteInteract] '{PlayerId}' → {data.action} on '{data.objectId}'");
                return;
            }

            // ── Worktable start/stop ─────────────────────────────────────────
            var worktable = ni.GetComponent<WorkTableInteractable>();
            if (worktable != null)
            {
                worktable.ApplyRemote(transform, data.action);
                Debug.Log($"[RemoteInteract] '{PlayerId}' → {data.action} on '{data.objectId}'");
                return;
            }

            // ── Route 1 mission interactions ────────────────────────────────
            var route1Handlers = ni.GetComponents<Route1ProgressInteractable>();
            foreach (var route1 in route1Handlers)
            {
                if (route1 == null || !route1.HandlesRemoteAction(data.action)) continue;
                route1.ApplyRemote(transform, data.action);
                return;
            }

            // ── Cabinet inspect start/stop ───────────────────────────────────
            var inspectCabinets = ni.GetComponent<WorkInspectCabinetsInteractable>();
            if (inspectCabinets != null)
            {
                inspectCabinets.ApplyRemote(transform, data.action);
                Debug.Log($"[RemoteInteract] '{PlayerId}' → {data.action} on '{data.objectId}'");
                return;
            }

            // ── Sleep start/stop ─────────────────────────────────────────────
            var sleep = ni.GetComponent<SleepInteractable>();
            if (sleep != null)
            {
                sleep.ApplyRemote(transform, data.action);
                Debug.Log($"[RemoteInteract] '{PlayerId}' → {data.action} on '{data.objectId}'");
                return;
            }

            // ── Hide start/stop ──────────────────────────────────────────────
            var hide = ni.GetComponent<HideInteractable>();
            if (hide != null)
            {
                hide.ApplyRemote(transform, data.action);
                Debug.Log($"[RemoteInteract] '{PlayerId}' → {data.action} on '{data.objectId}'");
                return;
            }

            // ── Laundry grab-clothes start/stop/grab ─────────────────────────
            var laundryGrab = ni.GetComponent<LaundryGrabClothesInteractable>();
            if (laundryGrab != null)
            {
                laundryGrab.ApplyRemote(transform, carryClothesInteraction, data.action);
                Debug.Log($"[RemoteInteract] '{PlayerId}' → {data.action} on '{data.objectId}'");
                return;
            }

            // ── Laundry load-washer start/stop/load ──────────────────────────
            var laundryLoad = ni.GetComponent<LaundryLoadWasherInteractable>();
            if (laundryLoad != null)
            {
                laundryLoad.ApplyRemote(transform, carryClothesInteraction, carryFoldedClothesInteraction, data.action);
                Debug.Log($"[RemoteInteract] '{PlayerId}' → {data.action} on '{data.objectId}'");
                return;
            }

            // ── Laundry store-clothes start/stop/store ──────────────────────
            var laundryStore = ni.GetComponent<LaundryStoreClothesInteractable>();
            if (laundryStore != null)
            {
                laundryStore.ApplyRemote(transform, carryFoldedClothesInteraction, data.action);
                Debug.Log($"[RemoteInteract] '{PlayerId}' → {data.action} on '{data.objectId}'");
                return;
            }

            // ── Future interaction types: add branches here ──────────────────

            Debug.LogWarning($"[RemoteInteract] No handler for action '{data.action}' " +
                             $"on object '{data.objectId}' (player '{PlayerId}')");
        }
    }
}
