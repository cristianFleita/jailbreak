using UnityEngine;
using UnityEngine.InputSystem;
using Jailbreak.Network;
using Jailbreak.NPC;

namespace Jailbreak.Player
{
    /// <summary>
    /// Handles the "Focus Capture" mechanic for guards.
    /// Requirements:
    /// - Distance <= 2.0m
    /// - Continuous focus for 0.5s via Raycast from camera
    /// - Emit guard:catch when successful
    /// </summary>
    public class GuardCaptureSystem : MonoBehaviour
    {
        [Header("Capture Settings")]
        public float captureRange = 2.0f;
        public float captureFocusTime = 0.5f;
        public LayerMask targetLayer;

        [Header("References")]
        public Camera fpsCamera;

        public float CurrentFocusProgress => _focusTimer / captureFocusTime;
        public bool IsFocusing => _focusTimer > 0f;

        private float _focusTimer = 0f;
        private string _currentTargetId = null;

        private void Start()
        {
            if (fpsCamera == null)
            {
                fpsCamera = GetComponentInChildren<Camera>();
            }
        }

        private void Update()
        {
            // Only local player guards should run this logic
            if (NetworkManager.Instance == null || NetworkManager.Instance.State != ConnectionState.InGame)
                return;

            var gsm = GameStateManager.Instance;
            if (gsm == null || gsm.LocalRole != "guard")
                return;

            // Must use Left Mouse Button to focus
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.isPressed)
            {
                ResetFocus();
                return;
            }

            // Raycast to find target
            Ray ray = new Ray(fpsCamera.transform.position, fpsCamera.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, captureRange, targetLayer))
            {
                string hitTargetId = GetTargetIdFromCollider(hit.collider);

                if (!string.IsNullOrEmpty(hitTargetId))
                {
                    if (hitTargetId == _currentTargetId)
                    {
                        // Continue focusing
                        _focusTimer += Time.deltaTime;
                        if (_focusTimer >= captureFocusTime)
                        {
                            CompleteCapture(_currentTargetId);
                        }
                    }
                    else
                    {
                        // New target
                        _currentTargetId = hitTargetId;
                        _focusTimer = 0f;
                    }
                }
                else
                {
                    // Hit something else
                    ResetFocus();
                }
            }
            else
            {
                // Hit nothing
                ResetFocus();
            }
        }

        private void ResetFocus()
        {
            _focusTimer = 0f;
            _currentTargetId = null;
        }

        private void CompleteCapture(string targetId)
        {
            Debug.Log($"[GuardCapture] Capture focus complete on target: {targetId}");
            NetworkManager.Instance.SendGuardCatch(targetId);
            ResetFocus();
        }

        private string GetTargetIdFromCollider(Collider col)
        {
            // First check if it's a remote player
            var remotePlayer = col.GetComponentInParent<RemotePlayerSync>();
            if (remotePlayer != null)
            {
                return remotePlayer.PlayerId;
            }

            // Next check if it's an NPC
            var npcIdentity = col.GetComponentInParent<NPCIdentity>();
            if (npcIdentity != null)
            {
                return npcIdentity.NpcId;
            }

            return null;
        }

        // Simple MVP UI drawing
        private void OnGUI()
        {
            var gsm = GameStateManager.Instance;
            if (gsm == null || gsm.LocalRole != "guard")
                return;

            // Draw Crosshair
            GUI.color = Color.white;
            GUI.Label(new Rect(Screen.width / 2 - 10, Screen.height / 2 - 10, 20, 20), "+");

            // Draw Focus Progress
            if (IsFocusing)
            {
                float progress = CurrentFocusProgress;
                int barWidth = 100;
                int barHeight = 10;
                Rect bgRect = new Rect(Screen.width / 2 - barWidth / 2, Screen.height / 2 + 20, barWidth, barHeight);
                Rect fgRect = new Rect(bgRect.x, bgRect.y, barWidth * progress, barHeight);

                GUI.color = new Color(0, 0, 0, 0.5f);
                GUI.DrawTexture(bgRect, Texture2D.whiteTexture);
                
                GUI.color = Color.red;
                GUI.DrawTexture(fgRect, Texture2D.whiteTexture);
            }
        }
    }
}
