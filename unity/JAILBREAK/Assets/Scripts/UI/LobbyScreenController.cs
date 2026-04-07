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
        private TextField _roomNameField;
        private TextField _roomIdField;
        private Label _statusLabel;
        private Label _welcomeLabel;

        private void OnEnable()
        {
            var root = uiDocument.rootVisualElement;

            _createRoomBtn = root.Q<Button>("create-room-btn");
            _joinRoomBtn = root.Q<Button>("join-room-btn");
            _roomNameField = root.Q<TextField>("room-name-field");
            _roomIdField = root.Q<TextField>("room-id-field");
            _statusLabel = root.Q<Label>("status-label");
            _welcomeLabel = root.Q<Label>("welcome-label");

            _createRoomBtn.clicked += OnCreateRoom;
            _joinRoomBtn.clicked += OnJoinRoom;

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
            net.OnNetworkErrorEvent += OnNetworkError;
            net.OnDisconnectedEvent += OnDisconnected;
        }

        private void OnDisable()
        {
            if (_createRoomBtn != null) _createRoomBtn.clicked -= OnCreateRoom;
            if (_joinRoomBtn != null) _joinRoomBtn.clicked -= OnJoinRoom;

            var net = NetworkManager.Instance;
            if (net != null)
            {
                net.OnRoomCreatedEvent -= OnRoomCreated;
                net.OnRoomStateEvent -= OnRoomState;
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
            _createRoomBtn.SetEnabled(false);
            _joinRoomBtn.SetEnabled(false);
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
            _createRoomBtn.SetEnabled(false);
            _joinRoomBtn.SetEnabled(false);
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

        private void OnNetworkError(ErrorPayload error)
        {
            _statusLabel.text = error.message;
            _createRoomBtn.SetEnabled(true);
            _joinRoomBtn.SetEnabled(true);
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
                net.OnNetworkErrorEvent -= OnNetworkError;
                net.OnDisconnectedEvent -= OnDisconnected;
            }

            SceneManager.LoadScene(roomSceneName);
        }
    }
}
