using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace Jailbreak.Game
{
    /// <summary>
    /// Displays the win/lose result using UI Toolkit based on the static data stored in EndGameController.
    /// Attach this to the GameObject that has the UIDocument component for the EndGame scene.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class EndGameUI : MonoBehaviour
    {
        [Header("Settings")]
        public string lobbySceneName = "MenuScene"; // Adjust to your actual lobby/menu scene name

        private VisualElement _resultIcon;
        private Label _resultTitle;
        private Label _resultDescription;
        private Button _returnButton;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            var root = GetComponent<UIDocument>().rootVisualElement;

            _resultIcon = root.Q<VisualElement>("ResultIcon");
            _resultTitle = root.Q<Label>("ResultTitle");
            _resultDescription = root.Q<Label>("ResultDescription");
            _returnButton = root.Q<Button>("ReturnButton");

            if (_returnButton != null)
            {
                _returnButton.clicked += OnReturnToLobbyClicked;
            }

            string winner = EndGameController.LastWinner ?? "unknown";
            string role = EndGameController.PlayerRoleAtEnd ?? "unknown";

            bool isWin = (winner == role) || (winner == "prisoners" && role == "prisoner") || (winner == "guards" && role == "guard");

            // 1. Set Title Text & Color
            if (_resultTitle != null)
            {
                _resultTitle.text = isWin ? "VICTORY" : "DEFEAT";
                _resultTitle.style.color = isWin ? new StyleColor(new Color(0.4f, 0.8f, 0.4f)) : new StyleColor(new Color(0.8f, 0.3f, 0.3f));
            }

            // 2. Set Description Text & Swap Icons dynamically!
            if (_resultDescription != null && _resultIcon != null)
            {
                // Clear default icons
                _resultIcon.RemoveFromClassList("icon-prisoners");
                _resultIcon.RemoveFromClassList("icon-guards");

                if (winner == "prisoners")
                {
                    _resultIcon.AddToClassList("icon-prisoners"); // Shows the Gear/Wrench shield
                    _resultDescription.text = role == "prisoner" 
                        ? "You and the other prisoners successfully outsmarted the guard!" 
                        : "The prisoners managed to escape or riot. You lost control of the prison.";
                }
                else if (winner == "guards")
                {
                    _resultIcon.AddToClassList("icon-guards"); // Shows the Star/Eye shield
                    _resultDescription.text = role == "guard" 
                        ? "You successfully caught all the prisoners!" 
                        : "The guard caught all the prisoners. Your escape failed.";
                }
                else
                {
                    _resultDescription.text = "The game ended in a draw or an unknown error occurred.";
                }
            }
        }

        private void OnDestroy()
        {
            if (_returnButton != null)
            {
                _returnButton.clicked -= OnReturnToLobbyClicked;
            }
        }

        private void OnReturnToLobbyClicked()
        {
            SceneManager.LoadScene(lobbySceneName);
        }
    }
}