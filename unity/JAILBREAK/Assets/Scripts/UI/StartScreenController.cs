using System.Collections;
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
        private Coroutine _pendingTransition;

        private void OnEnable()
        {
            var root = uiDocument.rootVisualElement;

            _startBtn    = root.Q<Button>("start-btn");
            _nameField   = root.Q<TextField>("display-name-field");
            _statusLabel = root.Q<Label>("status-label");

            if (NetworkManager.Instance == null)
            {
                var go = new GameObject("NetworkManager");
                go.AddComponent<NetworkManager>();
            }

            var net = NetworkManager.Instance;

            // Subscribe to network events
            net.OnAuthRegisteredEvent += OnAuthenticated;
            net.OnNetworkErrorEvent   += OnNetworkError;
            net.OnGameReconnectEvent  += OnGameReconnect;
            _startBtn.clicked         += OnStartClicked;

            // FLOW STEP 1: Do I have a saved user ID?
            var savedId = net.GetSavedUserId();
            
            if (!string.IsNullOrEmpty(savedId))
            {
                // Yes -> Hide the UI and check the backend automatically
                Debug.Log("[StartScreen] Found saved ID. Auto-authenticating...");
                _nameField.style.display = DisplayStyle.None;
                _startBtn.style.display = DisplayStyle.None;
                _statusLabel.text = "Restoring session...";
                
                _connecting = true;
                net.Connect(); // NetworkManager will use the saved ID and Name automatically
            }
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

            NetworkManager.Instance.Connect(displayName);
        }

        private void OnAuthenticated(AuthRegisteredPayload payload)
        {
            Debug.Log($"[StartScreen] Authenticated as {payload.userId}");
            _statusLabel.text = "Authenticated! Checking game state...";
            
            // FLOW STEP 2 & 3: Wait a split second to see if the backend sends `game:reconnect`
            _pendingTransition = StartCoroutine(DeferredGoToLobby());
        }

        private IEnumerator DeferredGoToLobby()
        {
            // If the server doesn't interrupt us within 0.2s with a game:reconnect,
            // we assume there is no active game and go to the Lobby.
            yield return new WaitForSeconds(0.5f);
            
            Debug.Log("[StartScreen] No active game found. Going to Lobby.");
            GoToLobby();
        }

        private void OnGameReconnect(GameReconnectPayload payload)
        {
            // FLOW STEP 4: Active game found!
            Debug.Log("[StartScreen] Active game detected! Redirecting to GameScene");

            // Cancel the trip to the lobby
            if (_pendingTransition != null)
            {
                StopCoroutine(_pendingTransition);
                _pendingTransition = null;
            }

            GoToGame();
        }

        private void OnNetworkError(ErrorPayload error)
        {
            _statusLabel.text = $"Error: {error.message}";
            _connecting = false;
            
            // If auto-login failed, restore the UI so they can try manually
            _nameField.style.display = DisplayStyle.Flex;
            _startBtn.style.display = DisplayStyle.Flex;
            _startBtn.SetEnabled(true);
        }

        private void GoToLobby()
        {
            UnsubscribeAll();
            if (uiDocument != null) uiDocument.rootVisualElement.style.display = DisplayStyle.None;
            SceneManager.LoadScene(lobbySceneName);
        }

        private void GoToGame()
        {
            UnsubscribeAll();
            if (uiDocument != null) uiDocument.rootVisualElement.style.display = DisplayStyle.None;
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