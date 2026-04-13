using Jailbreak.Network;
using Jailbreak.NPC;
using UnityEngine;

namespace Jailbreak.Player
{
    /// <summary>
    /// Spawns the LOCAL player prefab (prisoner or guard) when the game starts.
    /// Remote players are handled separately by GameStateManager.
    ///
    /// Setup in scene:
    ///   1. Attach this component to any persistent GameObject (e.g. GameManager).
    ///   2. Assign prisonerPrefab, guardPrefab, and waypointRegistry in the Inspector.
    ///
    /// Prisoner prefab needs:   CharacterController, PlayerInputController,
    ///                          PlayerNetworkSync, FPSCameraController
    /// Guard prefab needs:      Same as prisoner (same controls, different visuals)
    /// </summary>
    public class PlayerSpawnController : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private GameObject prisonerPrefab;
        [SerializeField] private GameObject guardPrefab;

        [Header("References")]
        [SerializeField] private WaypointRegistry waypointRegistry;

        [Header("Fallback spawn if waypoint not found")]
        [SerializeField] private Vector3 fallbackSpawnPosition = new Vector3(0f, 1f, 0f);

        private GameObject _localPlayerInstance;
        private bool       _spawned;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Start()
        {
            var net = NetworkManager.Instance;
            if (net == null)
            {
                Debug.LogError("[SPAWN] NetworkManager not found");
                return;
            }

            net.OnGameStartEvent     += HandleGameStart;
            net.OnGameReconnectEvent += HandleGameReconnect;

            // Game may have already started before this scene loaded (normal flow)
            if (net.State == ConnectionState.InGame && net.CachedGameStart != null)
                HandleGameStart(net.CachedGameStart);
            // F5 reload path: reconnect data is cached, game:start was never received this session
            else if (net.State == ConnectionState.InGame && net.CachedGameReconnect != null)
                HandleGameReconnect(net.CachedGameReconnect);
        }

        private void OnDestroy()
        {
            var net = NetworkManager.Instance;
            if (net != null)
            {
                net.OnGameStartEvent     -= HandleGameStart;
                net.OnGameReconnectEvent -= HandleGameReconnect;
            }
        }

        // ─── Game Start / Reconnect ───────────────────────────────────────────

        private void HandleGameReconnect(GameReconnectPayload data)
        {
            if (_spawned) return;
            if (data?.players == null) return;

            var localId = NetworkManager.Instance?.LocalPlayerId;
            if (string.IsNullOrEmpty(localId))
            {
                Debug.LogError("[SPAWN] LocalPlayerId not set — cannot spawn local player on reconnect");
                return;
            }

            PlayerStateData myData = null;
            foreach (var p in data.players)
                if (p.userId == localId) { myData = p; break; }

            if (myData == null)
            {
                Debug.LogWarning($"[SPAWN] Local player {localId} not found in game:reconnect payload");
                return;
            }

            SpawnLocalPlayer(myData);
        }

        private void HandleGameStart(GameStartPayload data)
        {
            if (_spawned) return;

            var localId = NetworkManager.Instance?.LocalPlayerId;
            if (string.IsNullOrEmpty(localId))
            {
                Debug.LogError("[SPAWN] LocalPlayerId not set — cannot spawn local player");
                return;
            }

            // Find this player's data in the payload
            PlayerStateData myData = null;
            if (data.players != null)
                foreach (var p in data.players)
                    if (p.userId == localId) { myData = p; break; }

            if (myData == null)
            {
                Debug.LogWarning($"[SPAWN] Local player {localId} not found in game:start payload");
                return;
            }

            SpawnLocalPlayer(myData);
        }

        // ─── Spawn ────────────────────────────────────────────────────────────

        private void SpawnLocalPlayer(PlayerStateData data)
        {
            var prefab = data.role == "guard" ? guardPrefab : prisonerPrefab;
            if (prefab == null)
            {
                Debug.LogError($"[SPAWN] Prefab for role '{data.role}' is not assigned!");
                return;
            }

            var spawnPos = ResolveSpawnPosition(data);

            // Instantiate high up so the CC doesn't clip geometry at the wrong height.
            // TeleportToSpawn will immediately move to the correct position.
            _localPlayerInstance = Instantiate(prefab, new Vector3(spawnPos.x, 500f, spawnPos.z), Quaternion.identity);
            _localPlayerInstance.name = $"LocalPlayer_{data.role}";
            _spawned = true;

            var cc = _localPlayerInstance.GetComponent<CharacterController>();
            if (cc != null)
            {
                // FIX: If the CharacterController's center is 0, it means the GameObject pivot is 
                // in the middle of the capsule. Humanoid models have their pivot at their feet.
                // We correct the CC to standard Unity humanoid specs to stop the visual model from floating.
                if (Mathf.Abs(cc.center.y) < 0.01f && cc.height > 0f)
                {
                    cc.center = new Vector3(cc.center.x, cc.height * 0.5f, cc.center.z);
                    Debug.Log($"[SPAWN] Corrected CharacterController center to {cc.center} to prevent floating.");
                }
            }

            // Initialize PlayerNetworkSync with the spawn position so it starts sending moves
            var pns = _localPlayerInstance.GetComponent<PlayerNetworkSync>();
            if (pns != null)
            {
                pns.TeleportToSpawn(spawnPos);
                
                // FAILSAFE: Ensure the CC is re-enabled immediately so gravity takes over
                if (cc != null) cc.enabled = true;
            }
            else
            {
                Debug.LogError("[SPAWN] PlayerNetworkSync not found on spawned prefab!");
            }

            Debug.Log($"[SPAWN] Spawned local {data.role.ToUpper()} at {pns?.transform.position ?? spawnPos} (wp={data.spawnWaypointId ?? "none"})");
        }

        private Vector3 ResolveSpawnPosition(PlayerStateData data)
        {
            // Always trust the server-assigned position (even (0,0,0) is valid for guard)
            var serverPos = data.position.ToUnity();
            
            Debug.Log($"[SPAWN] Using server position: {serverPos}");
            return serverPos;
        }

        // ─── Public API ───────────────────────────────────────────────────────

        public GameObject LocalPlayerInstance => _localPlayerInstance;
    }
}