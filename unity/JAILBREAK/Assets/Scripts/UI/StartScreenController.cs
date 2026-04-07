using Jailbreak.Network;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Jailbreak.UI
{
    /// <summary>
    /// Controls the Start Screen.
    /// Shows "START GAME" for new players, "CONTINUE" for returning players.
    /// Connects + authenticates in one step, then loads LobbyScene.
    /// </summary>
    public class StartScreenController : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private string lobbySceneName = "LobbyScene";
        [SerializeField] private string gameSceneName  = "GameScene";

        private Button _startBtn;
        private TextField _nameField;
        private Label _statusLabel;
        private bool _connecting;

        private void OnEnable()
        {
            var root = uiDocument.rootVisualElement;

            _startBtn   = root.Q<Button>("start-btn");
            _nameField  = root.Q<TextField>("display-name-field");
            _statusLabel = root.Q<Label>("status-label");

            // Ensure NetworkManager exists
            if (NetworkManager.Instance == null)
            {
                var go = new GameObject("NetworkManager");
                go.AddComponent<NetworkManager>();
            }

            // Check for returning player using NetworkManager (works for WebGL + Editor)
            var net = NetworkManager.Instance;
            var savedName = net.GetSavedDisplayName();
            var savedId   = net.GetSavedUserId();

            bool isReturning = !string.IsNullOrEmpty(savedId);
            _startBtn.text = isReturning ? "CONTINUE" : "START GAME";

            if (!string.IsNullOrEmpty(savedName))
                _nameField.value = savedName;

            _startBtn.clicked += OnStartClicked;

            // Subscribe to reconnect event *before* connecting so we catch it
            // if auth:registered is immediately followed by game:reconnect
            net.OnGameReconnectEvent += OnGameReconnect;

            // If already authenticated (e.g. back from lobby), go straight through
            if (net.IsAuthenticated)
                GoToLobby();
        }

        private void OnDisable()
        {
            if (_startBtn != null)
                _startBtn.clicked -= OnStartClicked;

            var net = NetworkManager.Instance;
            if (net != null)
            {
                net.OnAuthRegisteredEvent -= OnAuthenticated;
                net.OnNetworkErrorEvent   -= OnNetworkError;
                net.OnGameReconnectEvent  -= OnGameReconnect;
            }
        }

        private void OnStartClicked()
        {
            if (_connecting) return;

            var displayName = _nameField.value?.Trim();
            if (string.IsNullOrEmpty(displayName))
            {
                _statusLabel.text = "Please enter your name";
                return;
            }

            _connecting = true;
            _startBtn.SetEnabled(false);
            _statusLabel.text = "Connecting...";

            var net = NetworkManager.Instance;
            net.OnAuthRegisteredEvent += OnAuthenticated;
            net.OnNetworkErrorEvent   += OnNetworkError;

            // Connect(displayName) handles connect + auth in one call
            net.Connect(displayName);
        }

        private void OnAuthenticated(AuthRegisteredPayload payload)
        {
            Debug.Log($"[StartScreen] Authenticated as {payload.userId}");
            // NOTE: if the server also fires game:reconnect immediately after
            // auth:registered, OnGameReconnect will fire and override this.
            GoToLobby();
        }

        private void OnGameReconnect(GameReconnectPayload payload)
        {
            Debug.Log("[StartScreen] Active game detected — redirecting to GameScene");
            GoToGame();
        }

        private void OnNetworkError(ErrorPayload error)
        {
            _statusLabel.text = $"Error: {error.message}";
            _startBtn.SetEnabled(true);
            _connecting = false;
        }

        private void GoToLobby()
        {
            UnsubscribeAll();
            SceneManager.LoadScene(lobbySceneName);
        }

        private void GoToGame()
        {
            UnsubscribeAll();
            SceneManager.LoadScene(gameSceneName);
        }

        private void UnsubscribeAll()
        {
            var net = NetworkManager.Instance;
            if (net != null)
            {
                net.OnAuthRegisteredEvent -= OnAuthenticated;
                net.OnNetworkErrorEvent   -= OnNetworkError;
                net.OnGameReconnectEvent  -= OnGameReconnect;
            }
        }
    }
}