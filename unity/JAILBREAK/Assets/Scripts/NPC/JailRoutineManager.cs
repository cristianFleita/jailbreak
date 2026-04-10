using System.Collections.Generic;
using UnityEngine;
using Jailbreak.Network;

namespace Jailbreak.NPC
{
    /// <summary>
    /// Receives jail routine events from the server and drives all NPC
    /// NPCBehaviorControllers accordingly.
    ///
    /// Setup:
    ///   1. Attach to a "JailRoutineManager" GameObject in the GameScene.
    ///   2. Assign waypointRegistry (drag the WaypointRegistry scene GameObject).
    ///   3. This component listens to NetworkManager events automatically.
    ///
    /// How it works:
    ///   - On phase:start  → apply all NPC assignments at once
    ///   - On npc:reassign → update only the mentioned NPCs
    ///   - On phase:warning → show HUD warning (pluggable via event)
    ///   - On phase:zone_check → show zone violation UI (pluggable via event)
    ///
    /// NPCBehaviorControllers are fetched from NPCNetworkSync's NPC pool,
    /// or created on demand if a controller is missing.
    /// </summary>
    public class JailRoutineManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private WaypointRegistry waypointRegistry;
        [SerializeField] private NPCNetworkSync    npcNetworkSync;   // optional — for NPC pool

        [Header("NPC Prefab (if not using NPCNetworkSync)")]
        [SerializeField] private GameObject npcPrefab;

        // ─── Runtime State ────────────────────────────────────────────────────
        private readonly Dictionary<string, NPCBehaviorController> _controllers = new();

        public int   CurrentJailPhase { get; private set; }
        public string CurrentZone      { get; private set; }
        public float  PhaseDuration    { get; private set; }
        public float  PhaseElapsed     { get; private set; }
        public float  PhaseRemaining   => Mathf.Max(0f, PhaseDuration - PhaseElapsed);

        // ─── Public Events (for HUD integration) ─────────────────────────────
        public System.Action<PhaseJailStartPayload> OnPhaseChanged;
        public System.Action<PhaseWarningPayload>   OnPhaseWarning;
        public System.Action<PhaseZoneCheckPayload> OnZoneViolation;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Start()
        {
            var net = NetworkManager.Instance;
            if (net == null)
            {
                Debug.LogError("[JAIL] NetworkManager not found");
                return;
            }

            net.OnPhaseJailStartEvent += HandlePhaseStart;
            net.OnPhaseWarningEvent   += HandlePhaseWarning;
            net.OnNPCReassignEvent    += HandleNPCReassign;
            net.OnPhaseZoneCheckEvent += HandleZoneCheck;
            net.OnGameReconnectEvent  += HandleReconnect;

            Debug.Log("[JAIL] JailRoutineManager initialized");
        }

        private void OnDestroy()
        {
            var net = NetworkManager.Instance;
            if (net == null) return;

            net.OnPhaseJailStartEvent -= HandlePhaseStart;
            net.OnPhaseWarningEvent   -= HandlePhaseWarning;
            net.OnNPCReassignEvent    -= HandleNPCReassign;
            net.OnPhaseZoneCheckEvent -= HandleZoneCheck;
            net.OnGameReconnectEvent  -= HandleReconnect;
        }

        private void Update()
        {
            if (PhaseDuration > 0f)
                PhaseElapsed += Time.deltaTime;
        }

        // ─── Event Handlers ───────────────────────────────────────────────────

        private void HandlePhaseStart(PhaseJailStartPayload data)
        {
            CurrentJailPhase = data.phase;
            CurrentZone      = data.zone;
            PhaseDuration    = data.duration;
            PhaseElapsed     = 0f;

            // Reset waypoint occupancy for the new phase
            waypointRegistry?.ResetOccupants();

            if (data.npcAssignments != null)
            {
                foreach (var assignment in data.npcAssignments)
                    ApplyAssignment(assignment);
            }

            OnPhaseChanged?.Invoke(data);
            Debug.Log($"[JAIL] Phase {data.phase} ({data.phaseName}) — {data.npcAssignments?.Length} NPCs assigned");
        }

        private void HandlePhaseWarning(PhaseWarningPayload data)
        {
            OnPhaseWarning?.Invoke(data);
            Debug.Log($"[JAIL] Phase warning: Phase {data.nextPhase} ({data.nextPhaseName}) in {data.warningInSeconds}s");
        }

        private void HandleNPCReassign(NPCReassignPayload data)
        {
            if (data.assignments == null) return;
            foreach (var assignment in data.assignments)
                ApplyAssignment(assignment);

            Debug.Log($"[JAIL] Reassigned {data.assignments.Length} NPCs (libre albedrío)");
        }

        private void HandleZoneCheck(PhaseZoneCheckPayload data)
        {
            OnZoneViolation?.Invoke(data);
            Debug.LogWarning($"[JAIL] Zone check: player in '{data.currentZone}', expected '{data.expectedZone}' — {data.graceSeconds}s grace");
        }

        private void HandleReconnect(GameReconnectPayload data)
        {
            // GameReconnectPayload doesn't carry jailPhase directly in the
            // current NetworkTypes, but the server includes it in the raw JSON.
            // For now, we wait for the next phase:start to resync.
            Debug.Log("[JAIL] Reconnected — waiting for next phase:start to resync NPC assignments");
        }

        // ─── Private: Apply Single Assignment ────────────────────────────────

        private void ApplyAssignment(NPCAssignmentData assignment)
        {
            if (string.IsNullOrEmpty(assignment.npcId)) return;

            var controller = GetOrCreateController(assignment.npcId);
            if (controller == null) return;

            controller.AssignAction(assignment, waypointRegistry);
        }

        // ─── Private: Controller Pool ─────────────────────────────────────────

        /// <summary>
        /// Returns the NPCBehaviorController for the given NPC id,
        /// creating one if it doesn't exist yet.
        /// </summary>
        private NPCBehaviorController GetOrCreateController(string npcId)
        {
            if (_controllers.TryGetValue(npcId, out var existing) && existing != null)
                return existing;

            // Try to find the NPC's existing GameObject (spawned by NPCNetworkSync)
            var npcGo = FindNPCGameObject(npcId);
            if (npcGo == null)
            {
                Debug.LogWarning($"[JAIL] NPC GameObject '{npcId}' not found — skipping assignment");
                return null;
            }

            // Add NPCBehaviorController if missing
            var ctrl = npcGo.GetComponent<NPCBehaviorController>();
            if (ctrl == null)
                ctrl = npcGo.AddComponent<NPCBehaviorController>();

            _controllers[npcId] = ctrl;
            return ctrl;
        }

        private GameObject FindNPCGameObject(string npcId)
        {
            // Search in NPCNetworkSync's child transforms
            if (npcNetworkSync != null)
            {
                for (int i = 0; i < npcNetworkSync.transform.childCount; i++)
                {
                    var child = npcNetworkSync.transform.GetChild(i);
                    // NPCNetworkSync names them "NPC_{id}_{type}"
                    if (child.name.Contains(npcId))
                        return child.gameObject;
                }
            }

            // Fallback: find by name in scene
            var found = GameObject.Find($"NPC_{npcId}");
            return found;
        }
    }
}
