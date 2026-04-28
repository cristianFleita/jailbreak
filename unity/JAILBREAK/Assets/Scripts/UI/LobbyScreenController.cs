using Jailbreak.Network;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Jailbreak.UI
{
    /// <summary>
    /// Controls the Lobby Screen.
    /// Create a room (as host) or join an existing room by ID.
    /// </summary>
    public class LobbyScreenController : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private string roomSceneName = "RoomScene";
        [SerializeField] private string startSceneName = "StartScene";

        private Button _createRoomBtn;
        private Button _joinRoomBtn;
        private Button _browseRoomsBtn;
        private Button _refreshRoomsBtn;
        private TextField _roomNameField;
        private TextField _roomIdField;
        private Label _statusLabel;
        private Label _welcomeLabel;
        private VisualElement _roomBrowserSection;
        private ScrollView _roomList;
        private Label _roomListEmptyLabel;

        private bool _roomBrowserVisible;
        private RoomListEntryPayload[] _latestRooms = new RoomListEntryPayload[0];

        private void OnEnable()
        {
            // Menu scene → cursor MUST be free for clicks. Hold our own unlock
            // token so the manager doesn't relock when a previous gameplay
            // scene's last token was released during scene unload.
            CursorLockManager.RequestUnlock(this);

            var root = uiDocument.rootVisualElement;

            _createRoomBtn = root.Q<Button>("create-room-btn");
            _joinRoomBtn = root.Q<Button>("join-room-btn");
            _browseRoomsBtn = root.Q<Button>("browse-rooms-btn");
            _refreshRoomsBtn = root.Q<Button>("refresh-rooms-btn");
            _roomNameField = root.Q<TextField>("room-name-field");
            _roomIdField = root.Q<TextField>("room-id-field");
            _statusLabel = root.Q<Label>("status-label");
            _welcomeLabel = root.Q<Label>("welcome-label");
            _roomBrowserSection = root.Q("room-browser-section");
            _roomList = root.Q<ScrollView>("room-list");
            _roomListEmptyLabel = root.Q<Label>("room-list-empty-label");

            _createRoomBtn.clicked += OnCreateRoom;
            _joinRoomBtn.clicked += OnJoinRoom;
            _browseRoomsBtn.clicked += OnBrowseRooms;
            _refreshRoomsBtn.clicked += OnRefreshRooms;

            var net = NetworkManager.Instance;
            if (net == null)
            {
                // Not connected — go back to start
                SceneManager.LoadScene(startSceneName);
                return;
            }

            // Show welcome message
            _welcomeLabel.text = $"Welcome, {net.LocalDisplayName}";

            // Listen for room events
            net.OnRoomCreatedEvent += OnRoomCreated;
            net.OnRoomStateEvent += OnRoomState;
            net.OnRoomListEvent += OnRoomList;
            net.OnNetworkErrorEvent += OnNetworkError;
            net.OnDisconnectedEvent += OnDisconnected;

            net.RequestRoomList();
        }

        private void OnDisable()
        {
            CursorLockManager.ReleaseUnlock(this);

            if (_createRoomBtn != null) _createRoomBtn.clicked -= OnCreateRoom;
            if (_joinRoomBtn != null) _joinRoomBtn.clicked -= OnJoinRoom;
            if (_browseRoomsBtn != null) _browseRoomsBtn.clicked -= OnBrowseRooms;
            if (_refreshRoomsBtn != null) _refreshRoomsBtn.clicked -= OnRefreshRooms;

            var net = NetworkManager.Instance;
            if (net != null)
            {
                net.OnRoomCreatedEvent -= OnRoomCreated;
                net.OnRoomStateEvent -= OnRoomState;
                net.OnRoomListEvent -= OnRoomList;
                net.OnNetworkErrorEvent -= OnNetworkError;
                net.OnDisconnectedEvent -= OnDisconnected;
            }
        }

        private void OnCreateRoom()
        {
            var roomName = _roomNameField.value?.Trim();
            if (string.IsNullOrEmpty(roomName))
            {
                _statusLabel.text = "Enter a room name";
                return;
            }
            if (roomName.Length > 32)
            {
                _statusLabel.text = "Room name max 32 characters";
                return;
            }

            _statusLabel.text = "Creating room...";
            SetLobbyButtonsEnabled(false);
            NetworkManager.Instance.CreateRoom(roomName);
        }

        private void OnJoinRoom()
        {
            var roomId = _roomIdField.value?.Trim();
            if (string.IsNullOrEmpty(roomId))
            {
                _statusLabel.text = "Enter a room ID";
                return;
            }

            _statusLabel.text = "Joining room...";
            SetLobbyButtonsEnabled(false);
            NetworkManager.Instance.JoinRoomById(roomId);
        }

        private void OnBrowseRooms()
        {
            _roomBrowserVisible = !_roomBrowserVisible;
            _roomBrowserSection.style.display = _roomBrowserVisible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _browseRoomsBtn.text = _roomBrowserVisible ? "HIDE ROOMS" : "VIEW ROOMS";

            if (!_roomBrowserVisible) return;

            _statusLabel.text = "";
            RefreshRoomList(_latestRooms);
            OnRefreshRooms();
        }

        private void OnRefreshRooms()
        {
            var net = NetworkManager.Instance;
            if (net == null) return;

            _refreshRoomsBtn.SetEnabled(false);
            net.RequestRoomList();
        }

        private void OnJoinListedRoom(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) return;

            _roomIdField.value = roomId;
            _statusLabel.text = "Joining room...";
            SetLobbyButtonsEnabled(false);
            NetworkManager.Instance.JoinRoomById(roomId);
        }

        private void OnRoomCreated(RoomCreatedPayload payload)
        {
            Debug.Log($"[LobbyScreen] Room created: {payload.roomId}");
            // room:state will follow — that triggers scene transition
        }

        private void OnRoomState(RoomStatePayload payload)
        {
            Debug.Log($"[LobbyScreen] Room state received, going to RoomScene");
            GoToRoom();
        }

        private void OnRoomList(RoomListPayload payload)
        {
            _latestRooms = payload?.rooms ?? new RoomListEntryPayload[0];
            RefreshRoomList(_latestRooms);

            if (_refreshRoomsBtn != null)
                _refreshRoomsBtn.SetEnabled(true);
        }

        private void OnNetworkError(ErrorPayload error)
        {
            _statusLabel.text = error.message;
            SetLobbyButtonsEnabled(true);
        }

        private void OnDisconnected()
        {
            SceneManager.LoadScene(startSceneName);
        }

        private void GoToRoom()
        {
            var net = NetworkManager.Instance;
            if (net != null)
            {
                net.OnRoomCreatedEvent -= OnRoomCreated;
                net.OnRoomStateEvent -= OnRoomState;
                net.OnRoomListEvent -= OnRoomList;
                net.OnNetworkErrorEvent -= OnNetworkError;
                net.OnDisconnectedEvent -= OnDisconnected;
            }

            SceneManager.LoadScene(roomSceneName);
        }

        private void RefreshRoomList(RoomListEntryPayload[] rooms)
        {
            if (_roomList == null || _roomListEmptyLabel == null) return;

            _roomList.Clear();

            bool hasRooms = rooms != null && rooms.Length > 0;
            _roomListEmptyLabel.style.display = hasRooms
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            if (!hasRooms) return;

            foreach (var room in rooms)
            {
                if (room == null) continue;

                var roomId = room.roomId;

                var row = new VisualElement();
                row.AddToClassList("room-row");

                var main = new VisualElement();
                main.AddToClassList("room-row-main");

                var nameLabel = new Label(room.roomId);
                nameLabel.AddToClassList("room-name-text");
                main.Add(nameLabel);

                var hostName = string.IsNullOrEmpty(room.hostDisplayName)
                    ? "Unknown host"
                    : room.hostDisplayName;
                var metaLabel = new Label($"Host: {hostName}");
                metaLabel.AddToClassList("room-meta-text");
                main.Add(metaLabel);

                var countLabel = new Label($"{room.playerCount}/{room.maxPlayers}");
                countLabel.AddToClassList("room-player-count");

                var joinButton = new Button(() => OnJoinListedRoom(roomId));
                joinButton.text = "JOIN";
                joinButton.AddToClassList("room-join-btn");
                joinButton.SetEnabled(room.status == "lobby" && room.playerCount < room.maxPlayers);

                row.Add(main);
                row.Add(countLabel);
                row.Add(joinButton);

                _roomList.Add(row);
            }
        }

        private void SetLobbyButtonsEnabled(bool enabled)
        {
            if (_createRoomBtn != null) _createRoomBtn.SetEnabled(enabled);
            if (_joinRoomBtn != null) _joinRoomBtn.SetEnabled(enabled);
            if (_browseRoomsBtn != null) _browseRoomsBtn.SetEnabled(enabled);
            if (_refreshRoomsBtn != null) _refreshRoomsBtn.SetEnabled(enabled && _roomBrowserVisible);
        }
    }
}
