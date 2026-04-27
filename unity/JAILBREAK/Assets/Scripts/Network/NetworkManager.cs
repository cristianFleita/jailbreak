using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

#if !UNITY_WEBGL || UNITY_EDITOR
using SocketIOClient;
#endif

namespace Jailbreak.Network
{
    /// <summary>
    /// Central networking hub for JAILBREAK.
    ///
    /// WebGL build  → delegates all socket I/O to SocketBridge.jslib.
    ///               JS callbacks arrive via Unity SendMessage on this GameObject.
    /// Editor/Desktop → uses SocketIOUnity (C# WebSocket).
    ///
    /// Public API is identical in both paths.
    /// </summary>
    public class NetworkManager : MonoBehaviour
    {
        // ─── Singleton ──────────────────────────────────────────────────────
        public static NetworkManager Instance { get; private set; }

        // ─── Constants (mirror backend defaultGameConfig) ────────────────────
        public const float TickInterval            = 0.05f;
        public const float NpcSendInterval         = 0.2f;
        public const float InterpolationBuffer     = 0.1f;
        public const float ReconciliationThreshold = 5.0f;
        public const float ReconciliationLerpSpeed = 0.15f;
        public const float ReconnectTimeout        = 30f;

        // ─── State ───────────────────────────────────────────────────────────
        public ConnectionState State          { get; private set; } = ConnectionState.Disconnected;
        public string          LocalPlayerId  { get; private set; }
        public string          LocalUserId    { get; private set; }
        public string          LocalDisplayName { get; private set; }
        public string          CurrentRoomId  => _currentRoomId;
        public bool            IsHost         { get; private set; }
        public bool            IsAuthenticated { get; private set; }

        /// <summary>
        /// Cached payloads so late-loading scenes (GameScene) can
        /// read them even after the events have already fired during loading.
        /// </summary>
        public GameStartPayload CachedGameStart { get; private set; }
        public GameReconnectPayload CachedGameReconnect { get; private set; }
        public PhaseJailStartPayload CachedPhaseJailStart { get; private set; }

        // ─── Events: Auth & Room Lobby ───────────────────────────────────────
        public event Action<AuthRegisteredPayload>    OnAuthRegisteredEvent;
        public event Action<RoomCreatedPayload>       OnRoomCreatedEvent;
        public event Action<RoomStatePayload>         OnRoomStateEvent;
        public event Action<RoomListPayload>          OnRoomListEvent;
        public event Action<RoomPlayerJoinedPayload>  OnRoomPlayerJoinedEvent;
        public event Action<RoomPlayerLeftPayload>    OnRoomPlayerLeftEvent;
        public event Action<RoomKickedPayload>        OnRoomKickedEvent;
        public event Action<RoomDestroyedPayload>     OnRoomDestroyedEvent;
        public event Action<GameStartPayload>         OnGameStartEvent;

        // ─── Events: Connection & Gameplay ──────────────────────────────────
        public event Action                           OnConnectedEvent;
        public event Action                           OnDisconnectedEvent;
        public event Action<PlayerJoinedPayload>      OnPlayerJoinedEvent;
        public event Action<PlayerLeftPayload>        OnPlayerLeftEvent;
        public event Action<PlayerReconnectedPayload> OnPlayerReconnectedEvent;
        public event Action<GameReconnectPayload>     OnGameReconnectEvent;
        public event Action<PlayerStateUpdate>        OnPlayerStateEvent;
        public event Action<NPCPositionUpdate>        OnNPCPositionsEvent;
        public event Action<PhaseChangePayload>       OnPhaseChangeEvent;
        public event Action<GuardCatchPayload>        OnGuardCatchResultEvent;
        public event Action<ChaseStartPayload>        OnChaseStartEvent;
        public event Action<ChaseEndPayload>          OnChaseEndEvent;
        public event Action<ItemPickupPayload>        OnItemPickupEvent;
        public event Action<RiotAvailablePayload>     OnRiotAvailableEvent;
        public event Action<PlayerActionBroadcast>    OnPlayerActionEvent;
        public event Action<GameEndPayload>           OnGameEndEvent;
        public event Action<ErrorPayload>             OnNetworkErrorEvent;

        // ─── Events: Jail Routine / NPC Phase System ────────────────────────
        public event Action<PhaseJailStartPayload>    OnPhaseJailStartEvent;
        public event Action<PhaseWarningPayload>      OnPhaseWarningEvent;
        public event Action<NPCReassignPayload>       OnNPCReassignEvent;
        public event Action<PhaseZoneCheckPayload>    OnPhaseZoneCheckEvent;

        // ─── Events: NPC Personality & Emergent Behavior ─────────────────
        public event Action<NPCEmergentData>          OnNPCEmergentEvent;
        public event Action<NPCMoodShiftData>         OnNPCMoodShiftEvent;

        // ─── Events: Escape Route System (Ruta 1 / Phase A) ──────────────
        public event Action<EscapeRouteSelectedPayload> OnEscapeRouteSelectedEvent;
        public event Action<EscapeRoute1StatePayload>   OnEscapeRoute1StateEvent;
        public event Action<WorldCuePayload>            OnWorldCueEvent;
        public event Action<WorldStatePayload>          OnWorldStateEvent;
        public event Action<ItemStateBroadcastPayload>  OnItemStateEvent;

        // Latest values cached so late-loading scenes / UI can hydrate.
        public EscapeRouteSelectedPayload CachedEscapeRouteSelected { get; private set; }
        public EscapeRoute1StatePayload   CachedEscapeRoute1State   { get; private set; }
        public WorldStatePayload          CachedWorldState          { get; private set; }
        public Dictionary<string, ItemStateBroadcastPayload> CachedItemStates { get; } = new();

        // ─── Private ─────────────────────────────────────────────────────────
        private string _currentRoomId;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

#if !UNITY_WEBGL || UNITY_EDITOR
        // Editor / desktop only
        private SocketIOUnity _socket;
        private float _reconnectTimer;
#endif

        // ─── jslib imports (WebGL only) ──────────────────────────────────────
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void   SocketConnect(string goName, string displayName);
        [DllImport("__Internal")] private static extern void   SocketCreateRoom(string roomName);
        [DllImport("__Internal")] private static extern void   SocketJoinRoom(string roomId);
        [DllImport("__Internal")] private static extern void   SocketKickPlayer(string targetUserId);
        [DllImport("__Internal")] private static extern void   SocketStartGame();
        [DllImport("__Internal")] private static extern void   SocketListRooms();
        [DllImport("__Internal")] private static extern void   SocketGetRoomState();
        [DllImport("__Internal")] private static extern void   SocketLeaveRoom();
        [DllImport("__Internal")] private static extern void   SocketSendPlayerMove(string json);
        [DllImport("__Internal")] private static extern void   SocketSendGuardMark(string targetId);
        [DllImport("__Internal")] private static extern void   SocketSendGuardCatch(string targetId);
        [DllImport("__Internal")] private static extern void   SocketSendInteract(string objectId, string action);
        [DllImport("__Internal")] private static extern void   SocketSendPlayerAction(string objectId, string action);
        [DllImport("__Internal")] private static extern void   SocketSendRiotActivate();
        [DllImport("__Internal")] private static extern void   SocketSendNPCSyncState(string json);
        [DllImport("__Internal")] private static extern void   SocketSendRegisterSpawnAreas(string json);
        [DllImport("__Internal")] private static extern void   SocketDisconnect();
        [DllImport("__Internal")] private static extern string SocketGetSavedUserId();
        [DllImport("__Internal")] private static extern string SocketGetSavedDisplayName();
        [DllImport("__Internal")] private static extern int    SocketIsConnected();
        [DllImport("__Internal")] private static extern string GetBackendUrl();
#endif

        // ─── Unity Lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Ensure the GameObject has the exact name the jslib will use for SendMessage
            gameObject.name = "NetworkManager";
        }

        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action?.Invoke(); }
                catch (Exception ex) { Debug.LogError($"[NET] Queued action exception: {ex}"); }
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            if (State == ConnectionState.Reconnecting)
            {
                _reconnectTimer -= Time.deltaTime;
                if (_reconnectTimer <= 0)
                {
                    Debug.Log("[NET] Reconnect timeout");
                    SetState(ConnectionState.Disconnected);
                }
            }
#endif
        }

        private void OnDestroy()
        {
            // Only the real singleton should disconnect.
            // When a duplicate NM is Destroy'd in Awake (because Instance already
            // exists), this fires — calling SocketDisconnect here would kill the
            // live socket of the REAL instance, breaking WebGL connectivity.
            if (Instance != this) return;

#if UNITY_WEBGL && !UNITY_EDITOR
            SocketDisconnect();
#else
            if (_socket != null) _ = _socket.DisconnectAsync();
#endif
        }

        // ─── Public API: Auth & Room Lobby ───────────────────────────────────

        /// <summary>Connect to server and authenticate with saved/new userId.</summary>
        public void Connect(string displayName = null)
        {
            if (!string.IsNullOrEmpty(displayName))
                LocalDisplayName = displayName;

            if (string.IsNullOrEmpty(LocalDisplayName))
                LocalDisplayName = GetSavedDisplayName();

            if (string.IsNullOrEmpty(LocalDisplayName))
                LocalDisplayName = $"Player_{UnityEngine.Random.Range(1000, 9999)}";

            Debug.Log($"[NET] Connect() as \"{LocalDisplayName}\"");
            SetState(ConnectionState.Connecting);

#if UNITY_WEBGL && !UNITY_EDITOR
            SocketConnect(gameObject.name, LocalDisplayName);
#else
            ConnectSocketIO();
#endif
        }

        public void Disconnect()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SocketDisconnect();
#else
            if (_socket != null) _ = _socket.DisconnectAsync();
#endif
            SetState(ConnectionState.Disconnected);
            IsAuthenticated = false;
            _currentRoomId = null;
        }

        public void CreateRoom(string roomName)
        {
            if (!IsAuthenticated) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            SocketCreateRoom(roomName);
#else
            _socket?.Emit("room:create", new { roomName });
#endif
        }

        public void JoinRoomById(string roomId)
        {
            if (!IsAuthenticated) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            SocketJoinRoom(roomId);
#else
            _socket?.Emit("room:join", new { roomId });
#endif
        }

        public void RequestRoomList()
        {
            if (!IsAuthenticated) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            SocketListRooms();
#else
            _socket?.Emit("room:list");
#endif
        }

        public void KickPlayer(string targetUserId)
        {
            if (!IsHost) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            SocketKickPlayer(targetUserId);
#else
            _socket?.Emit("room:kick", new { targetUserId });
#endif
        }

        public void StartGame()
        {
            if (!IsHost) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            SocketStartGame();
#else
            _socket?.Emit("room:start");
#endif
        }

        public void LeaveRoom()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SocketLeaveRoom();
#else
            _socket?.Emit("room:leave");
#endif
            _currentRoomId = null;
            IsHost = false;
        }

        /// <summary>
        /// Asks the server to resend room:state for the current room.
        /// Called by RoomScreenController on enable, since the original
        /// room:state was consumed by LobbyScreen before this scene loaded.
        /// </summary>
        public void RequestRoomState()
        {
            if (string.IsNullOrEmpty(_currentRoomId)) return;
        #if UNITY_WEBGL && !UNITY_EDITOR
            SocketGetRoomState();
        #else
            _socket?.Emit("room:get-state");
        #endif
        }

        // ─── Public API: Gameplay ────────────────────────────────────────────

        public void SendPlayerMove(PlayerMovePayload payload)
        {
            if (!IsInGame()) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            SocketSendPlayerMove(JsonUtility.ToJson(payload));
#else
            try { _socket.EmitStringAsJSON("player:move", JsonUtility.ToJson(payload)); }
            catch (Exception ex) { Debug.LogError($"[NET] SendPlayerMove: {ex}"); }
#endif
        }

        public void SendGuardMark(string targetId)
        {
            if (!IsInGame()) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            SocketSendGuardMark(targetId);
#else
            _socket?.Emit("guard:mark", new { targetId });
#endif
        }

        public void SendGuardCatch(string targetId)
        {
            if (!IsInGame()) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            SocketSendGuardCatch(targetId);
#else
            _socket?.Emit("guard:catch", new { targetId });
#endif
        }

        public void SendPlayerInteract(string objectId, string action)
        {
            if (!IsInGame()) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            SocketSendInteract(objectId, action);
#else
            _socket?.Emit("player:interact", new { objectId, action });
#endif
        }

        /// <summary>
        /// Broadcast an animation/interaction action (sit, stand, etc.) so
        /// other players can replay it on their local view of this avatar.
        /// </summary>
        public void SendPlayerAction(string objectId, string action)
        {
            if (!IsInGame()) return;
            if (string.IsNullOrEmpty(objectId) || string.IsNullOrEmpty(action)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            SocketSendPlayerAction(objectId, action);
#else
            _socket?.Emit("player:action", new { objectId, action });
#endif
        }

        public void SendRiotActivate()
        {
            if (!IsInGame()) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            SocketSendRiotActivate();
#else
            _socket?.Emit("riot:activate");
#endif
        }

        public void SendNPCSyncState(NPCSyncStatePayload payload)
        {
            if (!IsInGame() || !IsHost) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            SocketSendNPCSyncState(JsonUtility.ToJson(payload));
#else
            try { _socket.EmitStringAsJSON("npc:sync_state", JsonUtility.ToJson(payload)); }
            catch (Exception ex) { Debug.LogError($"[NET] SendNPCSyncState: {ex}"); }
#endif
        }

        /// <summary>
        /// Host-only: registers scene-authored route spawn areas with the
        /// backend. The backend picks one area per critical item and emits
        /// item:state with authoritative positions (Phase C-02).
        /// </summary>
        public void SendRegisterSpawnAreas(RegisterSpawnAreasPayload payload)
        {
            if (!IsInGame() || !IsHost) return;
            if (payload == null || payload.areas == null || payload.areas.Length == 0)
            {
                Debug.LogWarning("[NET] SendRegisterSpawnAreas: empty payload, skipping");
                return;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            SocketSendRegisterSpawnAreas(JsonUtility.ToJson(payload));
#else
            try { _socket.EmitStringAsJSON("route:register_spawn_areas", JsonUtility.ToJson(payload)); }
            catch (Exception ex) { Debug.LogError($"[NET] SendRegisterSpawnAreas: {ex}"); }
#endif
        }

        public string GetSavedUserId()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return SocketGetSavedUserId();
#else
            return PlayerPrefs.GetString("jailbreak_user_id", "");
#endif
        }

        public string GetSavedDisplayName()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return SocketGetSavedDisplayName();
#else
            return PlayerPrefs.GetString("jailbreak_display_name", "");
#endif
        }

        // ─── SendMessage callbacks (WebGL — called by SocketBridge.jslib) ────
        // Unity's SendMessage delivers these on the main thread, so we can
        // invoke events directly; we still use the queue for consistency.

        public void OnSocketDisconnected(string reason)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                Debug.Log($"[NET] Disconnected: {reason}");
                if (State == ConnectionState.InGame)
                    SetState(ConnectionState.Reconnecting);
                else
                    SetState(ConnectionState.Disconnected);
                OnDisconnectedEvent?.Invoke();
            });
        }

        public void OnAuthRegistered(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<AuthRegisteredPayload>(json);
                if (data == null) return;
                LocalUserId = data.userId;
                LocalDisplayName = data.displayName;
                LocalPlayerId = data.userId; // use stable userId so it survives reconnects
                IsAuthenticated = true;
                SetState(ConnectionState.Connected);
                OnConnectedEvent?.Invoke();
                OnAuthRegisteredEvent?.Invoke(data);
            });
        }

        public void OnRoomCreated(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<RoomCreatedPayload>(json);
                if (data == null) return;
                _currentRoomId = data.roomId;
                IsHost = data.hostUserId == LocalUserId;
                OnRoomCreatedEvent?.Invoke(data);
            });
        }

        public void OnRoomState(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<RoomStatePayload>(json);
                if (data == null) return;
                _currentRoomId = data.roomId;
                IsHost = data.hostUserId == LocalUserId;
                OnRoomStateEvent?.Invoke(data);
            });
        }

        public void OnRoomList(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<RoomListPayload>(json);
                if (data != null) OnRoomListEvent?.Invoke(data);
            });
        }

        public void OnRoomPlayerJoined(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<RoomPlayerJoinedPayload>(json);
                if (data != null) OnRoomPlayerJoinedEvent?.Invoke(data);
            });
        }

        public void OnRoomPlayerLeft(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<RoomPlayerLeftPayload>(json);
                if (data != null) OnRoomPlayerLeftEvent?.Invoke(data);
            });
        }

        public void OnRoomKicked(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                _currentRoomId = null;
                IsHost = false;
                var data = JsonUtility.FromJson<RoomKickedPayload>(json);
                if (data != null) OnRoomKickedEvent?.Invoke(data);
            });
        }

        public void OnRoomDestroyed(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                _currentRoomId = null;
                IsHost = false;
                var data = JsonUtility.FromJson<RoomDestroyedPayload>(json);
                if (data != null) OnRoomDestroyedEvent?.Invoke(data);
            });
        }

        public void OnGameStart(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                SetState(ConnectionState.InGame);
                var data = JsonUtility.FromJson<GameStartPayload>(json);
                if (data != null)
                {
                    CachedGameStart = data;
                    OnGameStartEvent?.Invoke(data);
                }
            });
        }

        public void OnPlayerState(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<PlayerStateUpdate>(json);
                if (data != null) OnPlayerStateEvent?.Invoke(data);
            });
        }

        public void OnNPCPositions(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<NPCPositionUpdate>(json);
                if (data != null) OnNPCPositionsEvent?.Invoke(data);
            });
        }

        public void OnGameEnd(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                SetState(ConnectionState.PostGame);
                var data = JsonUtility.FromJson<GameEndPayload>(json);
                if (data != null) OnGameEndEvent?.Invoke(data);
            });
        }

        public void OnGameReconnect(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                SetState(ConnectionState.InGame);
                var data = JsonUtility.FromJson<GameReconnectPayload>(json);
                if (data != null)
                {
                    CachedGameReconnect = data;
                    OnGameReconnectEvent?.Invoke(data);
                }
            });
        }

        public void OnChaseStart(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<ChaseStartPayload>(json);
                if (data != null) OnChaseStartEvent?.Invoke(data);
            });
        }

        public void OnGuardCatchResult(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<GuardCatchPayload>(json);
                if (data != null) OnGuardCatchResultEvent?.Invoke(data);
            });
        }

        public void OnItemPickup(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<ItemPickupPayload>(json);
                if (data != null) OnItemPickupEvent?.Invoke(data);
            });
        }

        public void OnRiotAvailable(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<RiotAvailablePayload>(json);
                if (data != null) OnRiotAvailableEvent?.Invoke(data);
            });
        }

        public void OnPlayerAction(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<PlayerActionBroadcast>(json);
                if (data != null) OnPlayerActionEvent?.Invoke(data);
            });
        }

        // ─── Jail Routine callbacks (WebGL SendMessage) ──────────────────────

        public void OnPhaseJailStart(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<PhaseJailStartPayload>(json);
                if (data != null) OnPhaseJailStartEvent?.Invoke(data);
            });
        }

        public void OnPhaseWarning(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<PhaseWarningPayload>(json);
                if (data != null) OnPhaseWarningEvent?.Invoke(data);
            });
        }

        public void OnNPCReassign(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<NPCReassignPayload>(json);
                if (data != null) OnNPCReassignEvent?.Invoke(data);
            });
        }

        public void OnPhaseZoneCheck(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<PhaseZoneCheckPayload>(json);
                if (data != null) OnPhaseZoneCheckEvent?.Invoke(data);
            });
        }

        public void OnNPCEmergent(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<NPCEmergentData>(json);
                if (data != null) OnNPCEmergentEvent?.Invoke(data);
            });
        }

        public void OnNPCMoodShift(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<NPCMoodShiftData>(json);
                if (data != null) OnNPCMoodShiftEvent?.Invoke(data);
            });
        }

        // ─── Escape Route callbacks (WebGL SendMessage) ──────────────────

        public void OnEscapeRouteSelected(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<EscapeRouteSelectedPayload>(json);
                if (data == null) return;
                CachedEscapeRouteSelected = data;
                OnEscapeRouteSelectedEvent?.Invoke(data);
            });
        }

        public void OnEscapeRoute1State(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<EscapeRoute1StatePayload>(json);
                if (data == null) return;
                CachedEscapeRoute1State = data;
                OnEscapeRoute1StateEvent?.Invoke(data);
            });
        }

        public void OnWorldCue(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<WorldCuePayload>(json);
                if (data != null) OnWorldCueEvent?.Invoke(data);
            });
        }

        public void OnWorldState(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<WorldStatePayload>(json);
                if (data == null) return;
                CachedWorldState = data;
                OnWorldStateEvent?.Invoke(data);
            });
        }

        public void OnItemState(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<ItemStateBroadcastPayload>(json);
                if (data == null) return;
                CachedItemStates[data.itemId] = data;
                OnItemStateEvent?.Invoke(data);
            });
        }

        public void OnNetworkError(string json)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                var data = JsonUtility.FromJson<ErrorPayload>(json);
                if (data == null) return;
                Debug.LogError($"[NET] Server error: {data.message}");
                if (State == ConnectionState.Connecting)
                    SetState(ConnectionState.Disconnected);
                OnNetworkErrorEvent?.Invoke(data);
            });
        }

        // ─── Editor / Desktop path (SocketIOUnity) ──────────────────────────
#if !UNITY_WEBGL || UNITY_EDITOR
        private void ConnectSocketIO()
        {
            var url = "http://localhost:3001";
            Debug.Log($"[NET] ConnectSocketIO → {url}");

            try
            {
                _socket = new SocketIOUnity(url, new SocketIOOptions
                {
                    Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
                    Reconnection = false,
                });
                RegisterSocketIOListeners();
                _ = _socket.ConnectAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NET] ConnectSocketIO error: {ex}");
                SetState(ConnectionState.Disconnected);
            }
        }

        private void RegisterSocketIOListeners()
        {
            _socket.OnConnected += (_, _) =>
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    Debug.Log("[NET] SocketIO connected, authenticating...");
                    // Authenticate
                    var savedId = PlayerPrefs.GetString("jailbreak_user_id", null);
                    _socket.Emit("auth:register", new
                    {
                        userId = string.IsNullOrEmpty(savedId) ? null : savedId,
                        displayName = LocalDisplayName,
                    });
                });
            };

            _socket.OnDisconnected += (_, reason) =>
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    Debug.Log($"[NET] SocketIO disconnected: {reason}");
                    if (State == ConnectionState.InGame)
                    {
                        _reconnectTimer = ReconnectTimeout;
                        SetState(ConnectionState.Reconnecting);
                        StartCoroutine(ReconnectLoop());
                    }
                    else
                    {
                        SetState(ConnectionState.Disconnected);
                    }
                    OnDisconnectedEvent?.Invoke();
                });
            };

            SafeOn("auth:registered", r =>
            {
                var data = DeserializePayload<AuthRegisteredPayload>(r);
                if (data == null) return;
                _mainThreadQueue.Enqueue(() =>
                {
                    LocalUserId = data.userId;
                    LocalDisplayName = data.displayName;
                    LocalPlayerId = data.userId; // use stable userId so it survives reconnects
                    IsAuthenticated = true;
                    PlayerPrefs.SetString("jailbreak_user_id",    data.userId);
                    PlayerPrefs.SetString("jailbreak_display_name", data.displayName);
                    PlayerPrefs.Save();
                    SetState(ConnectionState.Connected);
                    OnConnectedEvent?.Invoke();
                    OnAuthRegisteredEvent?.Invoke(data);
                });
            });

            SafeOn("room:created", r =>
            {
                var data = DeserializePayload<RoomCreatedPayload>(r);
                if (data == null) return;
                _mainThreadQueue.Enqueue(() =>
                {
                    _currentRoomId = data.roomId;
                    IsHost = data.hostUserId == LocalUserId;
                    OnRoomCreatedEvent?.Invoke(data);
                });
            });

            SafeOn("room:state", r =>
            {
                var data = DeserializePayload<RoomStatePayload>(r);
                if (data == null) return;
                _mainThreadQueue.Enqueue(() =>
                {
                    _currentRoomId = data.roomId;
                    IsHost = data.hostUserId == LocalUserId;
                    OnRoomStateEvent?.Invoke(data);
                });
            });

            SafeOn("room:list:response", r =>
            {
                var data = DeserializePayload<RoomListPayload>(r);
                if (data != null)
                    _mainThreadQueue.Enqueue(() => OnRoomListEvent?.Invoke(data));
            });

            SafeOn("room:list:update", r =>
            {
                var data = DeserializePayload<RoomListPayload>(r);
                if (data != null)
                    _mainThreadQueue.Enqueue(() => OnRoomListEvent?.Invoke(data));
            });

            SafeOn("room:player-joined", r =>
            {
                var data = DeserializePayload<RoomPlayerJoinedPayload>(r);
                if (data != null)
                    _mainThreadQueue.Enqueue(() => OnRoomPlayerJoinedEvent?.Invoke(data));
            });

            SafeOn("room:player-left", r =>
            {
                var data = DeserializePayload<RoomPlayerLeftPayload>(r);
                if (data != null)
                    _mainThreadQueue.Enqueue(() => OnRoomPlayerLeftEvent?.Invoke(data));
            });

            SafeOn("room:kicked", r =>
            {
                var data = DeserializePayload<RoomKickedPayload>(r);
                _mainThreadQueue.Enqueue(() =>
                {
                    _currentRoomId = null; IsHost = false;
                    if (data != null) OnRoomKickedEvent?.Invoke(data);
                });
            });

            SafeOn("room:destroyed", r =>
            {
                var data = DeserializePayload<RoomDestroyedPayload>(r);
                _mainThreadQueue.Enqueue(() =>
                {
                    _currentRoomId = null; IsHost = false;
                    if (data != null) OnRoomDestroyedEvent?.Invoke(data);
                });
            });

            SafeOn("game:start", r =>
            {
                var data = DeserializePayload<GameStartPayload>(r);
                _mainThreadQueue.Enqueue(() =>
                {
                    SetState(ConnectionState.InGame);
                    if (data != null)
                    {
                        CachedGameStart = data;
                        OnGameStartEvent?.Invoke(data);
                    }
                });
            });

            SafeOn("player:state", r =>
            {
                var data = DeserializePayload<PlayerStateUpdate>(r);
                if (data != null)
                    _mainThreadQueue.Enqueue(() => OnPlayerStateEvent?.Invoke(data));
            });

            SafeOn("npc:positions", r =>
            {
                var data = DeserializePayload<NPCPositionUpdate>(r);
                if (data != null)
                    _mainThreadQueue.Enqueue(() => OnNPCPositionsEvent?.Invoke(data));
            });

            SafeOn("game:end", r =>
            {
                var data = DeserializePayload<GameEndPayload>(r);
                _mainThreadQueue.Enqueue(() =>
                {
                    SetState(ConnectionState.PostGame);
                    if (data != null) OnGameEndEvent?.Invoke(data);
                });
            });

            SafeOn("game:reconnect", r =>
            {
                var data = DeserializePayload<GameReconnectPayload>(r);
                _mainThreadQueue.Enqueue(() =>
                {
                    SetState(ConnectionState.InGame);
                    if (data != null)
                    {
                        CachedGameReconnect = data;
                        OnGameReconnectEvent?.Invoke(data);
                    }
                });
            });

            SafeOn("chase:start",     r => { var d = DeserializePayload<ChaseStartPayload>(r);   if (d != null) _mainThreadQueue.Enqueue(() => OnChaseStartEvent?.Invoke(d)); });
            SafeOn("guard:catch",     r => { var d = DeserializePayload<GuardCatchPayload>(r);   if (d != null) _mainThreadQueue.Enqueue(() => OnGuardCatchResultEvent?.Invoke(d)); });
            SafeOn("item:pickup",     r => { var d = DeserializePayload<ItemPickupPayload>(r);   if (d != null) _mainThreadQueue.Enqueue(() => OnItemPickupEvent?.Invoke(d)); });
            SafeOn("riot:available",  r => { var d = DeserializePayload<RiotAvailablePayload>(r); if (d != null) _mainThreadQueue.Enqueue(() => OnRiotAvailableEvent?.Invoke(d)); });
            SafeOn("player:action",   r => { var d = DeserializePayload<PlayerActionBroadcast>(r); if (d != null) _mainThreadQueue.Enqueue(() => OnPlayerActionEvent?.Invoke(d)); });

            // ── Jail Routine ────────────────────────────────────────────────
            SafeOn("phase:start",     r =>
            {
                var d = DeserializePayload<PhaseJailStartPayload>(r);
                if (d == null)
                {
                    Debug.LogError("[NET] phase:start deserialize returned null");
                    return;
                }
                _mainThreadQueue.Enqueue(() =>
                {
                    CachedPhaseJailStart = d;
                    int subs = OnPhaseJailStartEvent?.GetInvocationList()?.Length ?? 0;
                    Debug.Log($"[NET] phase:start received — Phase {d.phase} ({d.phaseName}) assignments={d.npcAssignments?.Length ?? 0} subscribers={subs}");
                    OnPhaseJailStartEvent?.Invoke(d);
                });
            });
            SafeOn("phase:warning",   r => { var d = DeserializePayload<PhaseWarningPayload>(r);   if (d != null) _mainThreadQueue.Enqueue(() => OnPhaseWarningEvent?.Invoke(d)); });
            SafeOn("npc:reassign",    r => { var d = DeserializePayload<NPCReassignPayload>(r);    if (d != null) _mainThreadQueue.Enqueue(() => OnNPCReassignEvent?.Invoke(d)); });
            SafeOn("phase:zone_check",r => { var d = DeserializePayload<PhaseZoneCheckPayload>(r); if (d != null) _mainThreadQueue.Enqueue(() => OnPhaseZoneCheckEvent?.Invoke(d)); });
            SafeOn("npc:emergent",    r => { var d = DeserializePayload<NPCEmergentData>(r);       if (d != null) _mainThreadQueue.Enqueue(() => OnNPCEmergentEvent?.Invoke(d)); });
            SafeOn("npc:mood_shift",  r => { var d = DeserializePayload<NPCMoodShiftData>(r);      if (d != null) _mainThreadQueue.Enqueue(() => OnNPCMoodShiftEvent?.Invoke(d)); });

            // ── Escape Route System (Ruta 1 / Phase A) ────────────────────────
            SafeOn("escape:route:selected", r =>
            {
                var d = DeserializePayload<EscapeRouteSelectedPayload>(r);
                if (d == null) return;
                _mainThreadQueue.Enqueue(() =>
                {
                    CachedEscapeRouteSelected = d;
                    OnEscapeRouteSelectedEvent?.Invoke(d);
                });
            });
            SafeOn("escape:route1:state", r =>
            {
                var d = DeserializePayload<EscapeRoute1StatePayload>(r);
                if (d == null) return;
                _mainThreadQueue.Enqueue(() =>
                {
                    CachedEscapeRoute1State = d;
                    OnEscapeRoute1StateEvent?.Invoke(d);
                });
            });
            SafeOn("world:cue",   r => { var d = DeserializePayload<WorldCuePayload>(r);   if (d != null) _mainThreadQueue.Enqueue(() => OnWorldCueEvent?.Invoke(d)); });
            SafeOn("world:state", r =>
            {
                var d = DeserializePayload<WorldStatePayload>(r);
                if (d == null) return;
                _mainThreadQueue.Enqueue(() =>
                {
                    CachedWorldState = d;
                    OnWorldStateEvent?.Invoke(d);
                });
            });
            SafeOn("item:state",  r =>
            {
                var d = DeserializePayload<ItemStateBroadcastPayload>(r);
                if (d != null)
                    _mainThreadQueue.Enqueue(() =>
                    {
                        CachedItemStates[d.itemId] = d;
                        OnItemStateEvent?.Invoke(d);
                    });
            });

            SafeOn("game:error", r =>
            {
                var data = DeserializePayload<ErrorPayload>(r);
                _mainThreadQueue.Enqueue(() =>
                {
                    Debug.LogError($"[NET] Server error: {data?.message}");
                    if (State == ConnectionState.Connecting) SetState(ConnectionState.Disconnected);
                    OnNetworkErrorEvent?.Invoke(data);
                });
            });
        }

        private void SafeOn(string eventName, Action<SocketIOResponse> callback)
        {
            try { _socket.On(eventName, callback); }
            catch (Exception ex) { Debug.LogError($"[NET] Error registering '{eventName}': {ex}"); }
        }

        private static T DeserializePayload<T>(SocketIOResponse response) where T : class
        {
            try
            {
                var raw = response.GetValue(0).GetRawText();
                return JsonUtility.FromJson<T>(raw);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NET] Failed to deserialize {typeof(T).Name}: {ex}");
                return null;
            }
        }

        private IEnumerator ReconnectLoop()
        {
            yield return new WaitForSeconds(2f);
            if (State != ConnectionState.Reconnecting) yield break;
            Connect(LocalDisplayName);
        }
#endif

        private void SetState(ConnectionState s)
        {
            State = s;
            Debug.Log($"[NET] State → {s}");
        }

        private bool IsInGame() =>
            State == ConnectionState.InGame || State == ConnectionState.Connected;
    }
}
