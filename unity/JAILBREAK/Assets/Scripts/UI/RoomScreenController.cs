using Jailbreak.Network;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Jailbreak.UI
{
    /// <summary>
    /// Controls the Room Screen.
    /// Shows room name, player list (live updated), host controls (start, kick),
    /// and handles room lifecycle events (destroyed, kicked, game start).
    /// </summary>
    public class RoomScreenController : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private string lobbySceneName = "LobbyScene";
        [SerializeField] private string gameSceneName = "GameScene";

        private Label _roomNameLabel;
        private Label _roomStatusLabel;
        private Label _statusLabel;
        private Label _waitingLabel;
        private ScrollView _playersList;
        private VisualElement _hostControls;
        private Button _startGameBtn;
        private Button _leaveBtn;

        private RoomPlayerInfo[] _currentPlayers;

        private void OnEnable()
        {
            var root = uiDocument.rootVisualElement;

            _roomNameLabel = root.Q<Label>("room-name-label");
            _roomStatusLabel = root.Q<Label>("room-status-label");
            _statusLabel = root.Q<Label>("status-label");
            _waitingLabel = root.Q<Label>("waiting-label");
            _playersList = root.Q<ScrollView>("players-list");
            _hostControls = root.Q("host-controls");
            _startGameBtn = root.Q<Button>("start-game-btn");
            _leaveBtn = root.Q<Button>("leave-btn");

            _startGameBtn.clicked += OnStartGame;
            _leaveBtn.clicked += OnLeaveRoom;

            var net = NetworkManager.Instance;
            if (net == null || string.IsNullOrEmpty(net.CurrentRoomId))
            {
                SceneManager.LoadScene(lobbySceneName);
                return;
            }

            // Show/hide host controls
            UpdateHostUI(net.IsHost);

            _roomNameLabel.text = $"Room: {net.CurrentRoomId}";

            // Subscribe to events
            net.OnRoomStateEvent += OnRoomState;
            net.OnRoomPlayerJoinedEvent += OnPlayerJoined;
            net.OnRoomPlayerLeftEvent += OnPlayerLeft;
            net.OnRoomKickedEvent += OnKicked;
            net.OnRoomDestroyedEvent += OnRoomDestroyed;
            net.OnGameStartEvent += OnGameStart;
            net.OnNetworkErrorEvent += OnNetworkError;
            net.OnDisconnectedEvent += OnDisconnected;

            // Request a fresh room:state from the server.
            net.RequestRoomState();
        }

        private void OnDisable()
        {
            if (_startGameBtn != null) _startGameBtn.clicked -= OnStartGame;
            if (_leaveBtn != null) _leaveBtn.clicked -= OnLeaveRoom;

            var net = NetworkManager.Instance;
            if (net != null)
            {
                net.OnRoomStateEvent -= OnRoomState;
                net.OnRoomPlayerJoinedEvent -= OnPlayerJoined;
                net.OnRoomPlayerLeftEvent -= OnPlayerLeft;
                net.OnRoomKickedEvent -= OnKicked;
                net.OnRoomDestroyedEvent -= OnRoomDestroyed;
                net.OnGameStartEvent -= OnGameStart;
                net.OnNetworkErrorEvent -= OnNetworkError;
                net.OnDisconnectedEvent -= OnDisconnected;
            }
        }

        // ─── Event Handlers ─────────────────────────────────────────────────

        private void OnRoomState(RoomStatePayload payload)
        {
            _roomNameLabel.text = $"Room: {payload.roomId}";
            UpdateHostUI(payload.hostUserId == NetworkManager.Instance.LocalUserId);
            RefreshPlayerList(payload.players);
        }

        private void OnPlayerJoined(RoomPlayerJoinedPayload payload)
        {
            _statusLabel.text = $"{payload.displayName} joined!";
            RefreshPlayerList(payload.players);
        }

        private void OnPlayerLeft(RoomPlayerLeftPayload payload)
        {
            _statusLabel.text = $"Player left ({payload.reason})";
            RefreshPlayerList(payload.players);
        }

        private void OnKicked(RoomKickedPayload payload)
        {
            Debug.Log($"[RoomScreen] Kicked from room: {payload.reason}");
            GoToLobby("You were kicked from the room");
        }

        private void OnRoomDestroyed(RoomDestroyedPayload payload)
        {
            Debug.Log($"[RoomScreen] Room destroyed: {payload.reason}");
            GoToLobby("Room was closed");
        }

        private void OnGameStart(GameStartPayload payload)
        {
            Debug.Log("[RoomScreen] Game starting!");

            var net = NetworkManager.Instance;
            if (net != null)
            {
                net.OnRoomStateEvent -= OnRoomState;
                net.OnRoomPlayerJoinedEvent -= OnPlayerJoined;
                net.OnRoomPlayerLeftEvent -= OnPlayerLeft;
                net.OnRoomKickedEvent -= OnKicked;
                net.OnRoomDestroyedEvent -= OnRoomDestroyed;
                net.OnGameStartEvent -= OnGameStart;
                net.OnNetworkErrorEvent -= OnNetworkError;
                net.OnDisconnectedEvent -= OnDisconnected;
            }

            SceneManager.LoadScene(gameSceneName);
        }

        private void OnNetworkError(ErrorPayload error)
        {
            _statusLabel.text = error.message;
            _startGameBtn.SetEnabled(true);
        }

        private void OnDisconnected()
        {
            SceneManager.LoadScene(lobbySceneName);
        }

        // ─── Actions ────────────────────────────────────────────────────────

        private void OnStartGame()
        {
            _startGameBtn.SetEnabled(false);
            _statusLabel.text = "Starting game...";
            NetworkManager.Instance.StartGame();
        }

        private void OnLeaveRoom()
        {
            NetworkManager.Instance.LeaveRoom();
            GoToLobby();
        }

        // ─── UI Helpers ─────────────────────────────────────────────────────

        private void UpdateHostUI(bool isHost)
        {
            _hostControls.style.display = isHost
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _waitingLabel.style.display = isHost
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        private void RefreshPlayerList(RoomPlayerInfo[] players)
        {
            _currentPlayers = players;
            _playersList.Clear();

            if (players == null) return;

            var net = NetworkManager.Instance;
            bool iAmHost = net != null && net.IsHost;

            _roomStatusLabel.text = $"PLAYERS ({players.Length}/8)";

            // Enable start button only if 2+ players
            if (_startGameBtn != null)
                _startGameBtn.SetEnabled(players.Length >= 2);

            for (int i = 0; i < players.Length; i++)
            {
                var p = players[i];
                var row = new VisualElement();
                row.AddToClassList("player-row"); // Updated class to match the USS

                var nameLabel = new Label($"{i + 1}. {p.displayName}");
                nameLabel.AddToClassList("player-name-text");
                row.Add(nameLabel);

                if (p.isHost)
                {
                    var hostBadge = new Label("HOST");
                    hostBadge.AddToClassList("player-host-badge"); // Fixed class name for styling
                    row.Add(hostBadge);
                }

                // Kick button (host only, can't kick self)
                if (iAmHost && !p.isHost)
                {
                    var kickBtn = new Button(() => OnKickPlayer(p.userId));
                    kickBtn.text = "KICK";
                    kickBtn.AddToClassList("kick-btn");
                    row.Add(kickBtn);
                }

                _playersList.Add(row);
            }
        }

        private void OnKickPlayer(string userId)
        {
            NetworkManager.Instance.KickPlayer(userId);
        }

        private void GoToLobby(string message = null)
        {
            if (!string.IsNullOrEmpty(message))
                Debug.Log($"[RoomScreen] Returning to lobby: {message}");

            SceneManager.LoadScene(lobbySceneName);
        }
    }
}