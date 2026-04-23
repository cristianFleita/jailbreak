using UnityEngine;
using UnityEngine.SceneManagement;
using Jailbreak.Network;

namespace Jailbreak.Game
{
    /// <summary>
    /// Listens for the game end event, stores the outcome, and redirects to the EndGame scene.
    /// Attach this to any persistent or manager object in your main GameScene.
    /// </summary>
    public class EndGameController : MonoBehaviour
    {
        // Static data to pass between scenes
        public static string LastWinner { get; private set; }
        public static string PlayerRoleAtEnd { get; private set; }
        public static string EndReason { get; private set; }

        [Header("Settings")]
        [Tooltip("The name of the scene to load when the game ends.")]
        public string endGameSceneName = "EndGame";

        private void Start()
        {
            var gsm = GameStateManager.Instance;
            if (gsm != null)
            {
                gsm.onGameEnd.AddListener(HandleGameEnd);
            }
        }

        private void OnDestroy()
        {
            var gsm = GameStateManager.Instance;
            if (gsm != null)
            {
                gsm.onGameEnd.RemoveListener(HandleGameEnd);
            }
        }

        private void HandleGameEnd(string winner)
        {
            Debug.Log($"[EndGameController] Game ended! Winner: {winner}. Transitioning to {endGameSceneName} scene.");

            // Store the data so the EndGame scene can read it
            LastWinner = winner;
            PlayerRoleAtEnd = GameStateManager.Instance.LocalRole;
            
            // We no longer manually disconnect here. 
            // The backend automatically kicks the socket out of the room so we can safely join a new one.

            // Load the end game scene
            SceneManager.LoadScene(endGameSceneName);
        }
    }
}
