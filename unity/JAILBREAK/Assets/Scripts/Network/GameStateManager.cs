using System.Collections.Generic;
using Jailbreak.Player;
using UnityEngine;
using UnityEngine.Events;

namespace Jailbreak.Network
{
    /// <summary>
    /// Central game state tracker.
    /// Subscribes to NetworkManager events and maintains:
    /// - Player roster
    /// - Current game phase
    /// - Game status
    ///
    /// Also manages spawning/despawning of remote player GameObjects.
    /// </summary>
    public class GameStateManager : MonoBehaviour
    {
        [Header("Remote Player Prefab")]
        [SerializeField] private GameObject remotePlayerPrefab;

        // ─── State ──────────────────────────────────────────────────────────
        public Dictionary<string, PlayerStateData> Players { get; } = new();
        public Dictionary<string, GameObject> RemotePlayerGameObjects { get; } = new();
        public List<NPCStateData> NPCs { get; } = new();

        public string LocalPlayerId => NetworkManager.Instance?.LocalPlayerId;
        public string LocalRole { get; private set; }
        public string CurrentPhase { get; private set; } = "setup";
        public bool GameActive { get; private set; }

        // ─── Unity Events (hookable in Inspector by other systems) ────────
        [Header("Game Events")]
        public UnityEvent<string> onPhaseChanged;          // phase name string
        public UnityEvent<string> onGameEnd;               // winner string
        public UnityEvent onGameStarted;
        public UnityEvent<string> onPlayerCaught;          // targetId

        // ─── Singleton accessor ───────────────────────────────────────────
        public static GameStateManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start()
        {
            var net = NetworkManager.Instance;
            if (net == null)
            {
                Debug.LogError("[GSM] NetworkManager not found");
                return;
            }

            net.OnGameStartEvent         += HandleGameStart;
            net.OnPlayerJoinedEvent      += HandlePlayerJoined;
            net.OnPlayerLeftEvent        += HandlePlayerLeft;
            net.OnPlayerReconnectedEvent += HandlePlayerReconnected;
            net.OnGameReconnectEvent     += HandleGameReconnect;
            net.OnPlayerStateEvent       += HandlePlayerState;
            net.OnNPCPositionsEvent      += HandleNPCPositions;
            net.OnPhaseChangeEvent       += HandlePhaseChange;
            net.OnGuardCatchResultEvent  += HandleGuardCatch;
            net.OnGameEndEvent           += HandleGameEnd;

            // If game:start already fired before this scene loaded (normal flow),
            // process the cached payload now.
            if (net.State == ConnectionState.InGame && net.CachedGameStart != null)
            {
                Debug.Log("[GSM] Processing cached game:start payload");
                HandleGameStart(net.CachedGameStart);
            }
        }

        private void OnDestroy()
        {
            var net = NetworkManager.Instance;
            if (net == null) return;

            net.OnGameStartEvent        -= HandleGameStart;
            net.OnPlayerJoinedEvent     -= HandlePlayerJoined;
            net.OnPlayerLeftEvent       -= HandlePlayerLeft;
            net.OnPlayerReconnectedEvent -= HandlePlayerReconnected;
            net.OnGameReconnectEvent    -= HandleGameReconnect;
            net.OnPlayerStateEvent      -= HandlePlayerState;
            net.OnNPCPositionsEvent     -= HandleNPCPositions;
            net.OnPhaseChangeEvent      -= HandlePhaseChange;
            net.OnGuardCatchResultEvent -= HandleGuardCatch;
            net.OnGameEndEvent          -= HandleGameEnd;
        }

        // ─── Handlers ────────────────────────────────────────────────────────

        // Handles the initial game:start event which carries the full player+NPC list.
        // This fires before the scene fully loads in WebGL, so we also handle
        // player-joined for redundancy — whichever arrives with players wins.
        private void HandleGameStart(GameStartPayload data)
        {
            if (data.players == null) return;

            // Find local player's role and log full roster
            var localId = LocalPlayerId;
            Debug.Log($"[GSM] game:start — {data.players.Length} players, localId={localId}");
            foreach (var p in data.players)
            {
                var tag = p.id == localId ? " ← YOU" : "";
                Debug.Log($"  → {p.id}: {p.role.ToUpper()}{tag}");
                if (p.id == localId)
                    LocalRole = p.role;
            }
            Debug.Log($"[GSM] Local role = {LocalRole}");

            SyncPlayerList(data.players);
            SpawnRemotePlayers();
            GameActive = true;
            onGameStarted?.Invoke();
        }

        private void HandlePlayerJoined(PlayerJoinedPayload data)
        {
            Debug.Log($"[GSM] HandlePlayerJoined: playerId={data?.playerId}, role={data?.role}, players.Length={data?.players?.Length ?? 0}");

            // Assign local role when we join
            if (data.playerId == LocalPlayerId)
            {
                LocalRole = data.role;
                Debug.Log($"[GSM] Assigned local role: {LocalRole}");
            }

            // Update full player list
            SyncPlayerList(data.players);
            SpawnRemotePlayers();

            GameActive = true;
            onGameStarted?.Invoke();
        }

        private void HandlePlayerLeft(PlayerLeftPayload data)
        {
            Players.Remove(data.playerId);
            DespawnRemotePlayer(data.playerId);
            SyncPlayerList(data.players);
        }

        private void HandlePlayerReconnected(PlayerReconnectedPayload data)
        {
            SyncPlayerList(data.players);
            SpawnRemotePlayers();
        }

        private void HandleGameReconnect(GameReconnectPayload data)
        {
            SyncPlayerList(data.players);
            SpawnRemotePlayers();

            NPCs.Clear();
            if (data.npcs != null) NPCs.AddRange(data.npcs);

            CurrentPhase = data.phase?.current ?? "active";
            GameActive = true;
            Debug.Log($"[GSM] Reconnected — tick {data.tick}, phase {CurrentPhase}");
        }

        private void HandlePlayerState(PlayerStateUpdate data)
        {
            if (data.players == null) return;

            foreach (var p in data.players)
            {
                Players[p.id] = p;

                // Discover our own role from the tick data if not yet known
                if (p.id == LocalPlayerId)
                {
                    if (string.IsNullOrEmpty(LocalRole) && !string.IsNullOrEmpty(p.role))
                    {
                        LocalRole = p.role;
                        Debug.Log($"[GSM] Role discovered from player:state → {LocalRole.ToUpper()}");
                    }
                    continue;
                }

                // Auto-spawn remote player if we haven't seen them before
                if (!RemotePlayerGameObjects.ContainsKey(p.id))
                {
                    SpawnRemotePlayer(p.id, p);
                }
                else if (RemotePlayerGameObjects.TryGetValue(p.id, out var go))
                {
                    var sync = go.GetComponent<RemotePlayerSync>();
                    if (sync != null) sync.PushState(p);
                }
            }

            // Mark game as active on first tick received
            if (!GameActive)
            {
                GameActive = true;
                onGameStarted?.Invoke();
            }
        }

        private void HandleNPCPositions(NPCPositionUpdate data)
        {
            if (data.npcs == null) return;
            // Update only the NPCs in the delta array
            foreach (var npc in data.npcs)
            {
                var idx = NPCs.FindIndex(n => n.id == npc.id);
                if (idx >= 0)
                    NPCs[idx] = npc;
                else
                    NPCs.Add(npc);
            }
        }

        private void HandlePhaseChange(PhaseChangePayload data)
        {
            CurrentPhase = data.phase;
            Debug.Log($"[GSM] Phase → {data.phaseName}");
            onPhaseChanged?.Invoke(data.phaseName);
        }

        private void HandleGuardCatch(GuardCatchPayload data)
        {
            if (data.success && data.isPlayer)
            {
                Debug.Log($"[GSM] Player caught: {data.targetId}");
                onPlayerCaught?.Invoke(data.targetId);

                // Mark player as dead
                if (Players.TryGetValue(data.targetId, out var p))
                {
                    p.isAlive = false;
                    Players[data.targetId] = p;
                }

                DespawnRemotePlayer(data.targetId);
            }
        }

        private void HandleGameEnd(GameEndPayload data)
        {
            GameActive = false;
            Debug.Log($"[GSM] Game ended — winner: {data.winner}, reason: {data.reason}");
            onGameEnd?.Invoke(data.winner);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private void SyncPlayerList(PlayerStateData[] incoming)
        {
            if (incoming == null) return;
            Players.Clear();
            foreach (var p in incoming)
                Players[p.id] = p;
        }

        private void SpawnRemotePlayers()
        {
            foreach (var (id, player) in Players)
            {
                if (id == LocalPlayerId) continue;  // Don't spawn self
                if (RemotePlayerGameObjects.ContainsKey(id)) continue;  // Already spawned

                SpawnRemotePlayer(id, player);
            }
        }

        private void SpawnRemotePlayer(string playerId, PlayerStateData player)
        {
            GameObject go;

            if (remotePlayerPrefab != null)
            {
                go = Instantiate(remotePlayerPrefab, player.position.ToUnity(), player.rotation.ToUnity());
            }
            else
            {
                // Default placeholder: capsule with different color
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.transform.position = player.position.ToUnity();
                go.transform.localScale = new Vector3(0.6f, 1.2f, 0.6f);

                // Color by role using MaterialPropertyBlock — no material instancing,
                // avoids URP shader breakage in WebGL (pink capsules).
                var color = player.role == "guard"
                    ? new Color(0.9f, 0.15f, 0.15f, 1f)  // red  = guard
                    : new Color(0.25f, 0.55f, 0.85f, 1f); // blue = prisoner
                var rend = go.GetComponent<Renderer>();
                if (rend != null)
                {
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetColor("_BaseColor", color); // URP Lit
                    mpb.SetColor("_Color",     color); // Built-in RP fallback
                    rend.SetPropertyBlock(mpb);
                }

                // Remove collider (optional)
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }

            go.name = $"Player_{playerId}_{player.role}";

            // Add RemotePlayerSync component
            var sync = go.AddComponent<RemotePlayerSync>();
            sync.PlayerId = playerId;
            sync.Role = player.role;

            RemotePlayerGameObjects[playerId] = go;
            Debug.Log($"[GSM] Spawned remote player {playerId} ({player.role})");
        }

        private void DespawnRemotePlayer(string playerId)
        {
            if (RemotePlayerGameObjects.TryGetValue(playerId, out var go))
            {
                Destroy(go);
                RemotePlayerGameObjects.Remove(playerId);
                Debug.Log($"[GSM] Despawned remote player {playerId}");
            }
        }
    }
}
