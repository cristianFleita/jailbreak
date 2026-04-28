using Jailbreak.Network;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Jailbreak.UI
{
    /// <summary>
    /// Pause/leave-match overlay for the GameScene. Sits on the same UIDocument
    /// as <see cref="GameHudController"/>; queries the pause panel + buttons
    /// out of GameGUI.uxml.
    ///
    /// Trigger surfaces:
    ///   - ESC (new + legacy input)
    ///   - The persistent MenuBtn in the HUD top-right (acts as a reminder of the
    ///     ESC keybind, but also clickable when the cursor is free, e.g. after
    ///     window focus loss).
    ///
    /// While the overlay is open the cursor is unlocked via
    /// <see cref="CursorLockManager"/> so the buttons are actually clickable —
    /// the FPS camera otherwise re-locks the cursor every LateUpdate.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class PauseMenuController : MonoBehaviour
    {
        [Header("Scene Routing")]
        [Tooltip("Scene to load after pressing 'Leave Match'. Should match the lobby browser scene name.")]
        [SerializeField] private string lobbySceneName = "LobbyScene";

        private VisualElement _pausePanel;
        private Button _pauseResumeBtn;
        private Button _pauseLeaveBtn;
        private Button _menuButton;

        private bool _isOpen;
        private bool _hasUnlockToken;
        private bool _navigatingAway;

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            if (doc == null) return;
            var root = doc.rootVisualElement;
            if (root == null) return;

            _pausePanel = root.Q<VisualElement>("PausePanel");
            _pauseResumeBtn = root.Q<Button>("PauseResumeBtn");
            _pauseLeaveBtn = root.Q<Button>("PauseLeaveBtn");
            _menuButton = root.Q<Button>("PauseMenuBtn");

            if (_pauseResumeBtn != null) _pauseResumeBtn.clicked += Close;
            if (_pauseLeaveBtn != null) _pauseLeaveBtn.clicked += OnLeavePressed;
            if (_menuButton != null) _menuButton.clicked += Open;

            ApplyVisibility();
        }

        private void OnDisable()
        {
            if (_pauseResumeBtn != null) _pauseResumeBtn.clicked -= Close;
            if (_pauseLeaveBtn != null) _pauseLeaveBtn.clicked -= OnLeavePressed;
            if (_menuButton != null) _menuButton.clicked -= Open;

            // Make sure we don't leak an unlock token if the scene unloads
            // while paused.
            if (_hasUnlockToken)
            {
                CursorLockManager.ReleaseUnlock(this);
                _hasUnlockToken = false;
            }
        }

        private void Update()
        {
            if (_navigatingAway) return;
            if (WasEscapePressedThisFrame())
            {
                if (_isOpen) Close();
                else Open();
            }
        }

        private static bool WasEscapePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null) return keyboard.escapeKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }

        public void Open()
        {
            if (_isOpen || _navigatingAway) return;
            _isOpen = true;
            CursorLockManager.RequestUnlock(this);
            _hasUnlockToken = true;
            ApplyVisibility();
        }

        public void Close()
        {
            if (!_isOpen) return;
            _isOpen = false;
            if (_hasUnlockToken)
            {
                CursorLockManager.ReleaseUnlock(this);
                _hasUnlockToken = false;
            }
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            if (_pausePanel != null)
                _pausePanel.style.display = _isOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnLeavePressed()
        {
            if (_navigatingAway) return;
            _navigatingAway = true;

            // Tell the backend we're out. The server decides whether this leave
            // ends the match (guard left / last prisoner left); either way the
            // local client is already navigating away from the GameScene.
            var net = NetworkManager.Instance;
            if (net != null) net.LeaveRoom();

            // Keep our unlock token held across the scene transition. Releasing
            // it here would briefly drop the refcount to 0 → CursorLockManager
            // would re-lock the cursor → on WebGL the browser keeps pointer
            // lock (it ignores `Cursor.lockState = None` mid-transition) and
            // the lobby starts trapped. OnDisable releases the token cleanly
            // once the destination scene's controller has its own token.
            //
            // Belt-and-suspenders: also force the browser to drop pointer lock
            // right now so the cursor is already free when the lobby loads.
            CursorLockManager.ForceBrowserPointerUnlock();

            SceneManager.LoadScene(lobbySceneName);
        }
    }
}
