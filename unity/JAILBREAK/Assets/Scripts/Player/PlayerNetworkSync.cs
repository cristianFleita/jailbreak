using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Player
{
    /// <summary>
    /// Attach to the LOCAL player GameObject.
    /// - Sends player:move ONLY when the player is actively moving (WASD input).
    /// - Sends a heartbeat every 2s when idle so the server doesn't time us out.
    /// - Does NOT apply server corrections: the local player owns its position.
    ///   Remote players are corrected by RemotePlayerSync instead.
    ///
    /// Requires: CharacterController + PlayerInputController on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerNetworkSync : MonoBehaviour
    {
        [Header("Tuning")]
        [SerializeField] private float positionThreshold = 0.05f;
        [SerializeField] private float minSendInterval   = 0.05f; // 20 Hz cap while moving

        private CharacterController _cc;
        private PlayerInputController _input;

        // Send-on-change tracking
        private Vector3 _lastSentPosition;
        private string  _lastSentState = "idle";
        private float   _lastSentTime;
        private bool    _hasSpawned;
        private bool    _wasMovingLastFrame;
        private int     _sendCount;

        private void Awake()
        {
            _cc    = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInputController>();
            if (_cc == null) Debug.LogError("[PNS] CharacterController not found");
            if (_input == null) Debug.LogError("[PNS] PlayerInputController not found");
        }

        /// <summary>
        /// Called by PlayerSpawnController after Instantiate with the server-assigned position.
        /// Teleports the CharacterController and enables network sending.
        /// </summary>
        public void TeleportToSpawn(Vector3 spawnPos)
        {
            _cc.enabled = false;

            // Server sends floor-level positions (y=0).
            // CC with center=(0,0,0) and height=2 has its feet at localY = center.y - height/2 = -1.
            // Offset so feet sit exactly on the floor.
            float feetOffset = _cc.height * 0.5f - _cc.center.y;
            spawnPos.y += feetOffset;

            transform.position = spawnPos;
            // DON'T re-enable CC here — enabling it can trigger collision resolution
            // that pushes the character. We'll enable it on the first Update frame.

            _lastSentPosition = spawnPos;
            _spawnPosition = spawnPos;
            _hasSpawned = true;
            _needsActivation = true;

            Debug.Log($"[PNS] Teleported to spawn: {spawnPos} (feet offset +{feetOffset:F2})");
        }

        private Vector3 _spawnPosition;
        private bool _needsActivation;

        private int _diagCounter;

        private void Update()
        {
            var net = NetworkManager.Instance;
            if (net == null || net.State != ConnectionState.InGame) return;
            if (!_hasSpawned) return;

            // Deferred activation: enable CC + input on the first Update frame.
            // This avoids collision resolution pushing the character during the
            // frame gap between TeleportToSpawn and the first Update.
            if (_needsActivation)
            {
                _needsActivation = false;

                // Re-set position (in case anything moved us between Instantiate and now)
                transform.position = _spawnPosition;
                _cc.enabled = true;

                // Enable input + camera AFTER CC is properly placed
                if (_input != null) _input.InputEnabled = true;

                Debug.Log($"[PNS] Activated at {transform.position}");
                return; // skip this frame — let CC settle
            }

            // Diagnostic: log first 5 frames after activation
            _diagCounter++;
            if (_diagCounter <= 100 && _diagCounter % 20 == 1)
            {
                var inputState = _input != null ? _input.CurrentState.ToString() : "null";
                Debug.Log($"[PNS-DIAG] frame={_diagCounter} inputState={inputState} pos={transform.position}");
            }

            var currentState = GetMovementState();
            var isMoving     = currentState != "idle";
            var now          = Time.time;

            // Rate-limit while moving
            if (isMoving && (now - _lastSentTime < minSendInterval)) return;

            if (isMoving)
            {
                // Player is pressing WASD — send position if it changed enough
                var currentPos = transform.position;
                if (Vector3.Distance(currentPos, _lastSentPosition) >= positionThreshold)
                {
                    SendMove(net, currentPos, currentState);
                    _wasMovingLastFrame = true;
                }
            }
            else if (_wasMovingLastFrame)
            {
                // Player just released keys — send one final "stopped" position
                SendMove(net, transform.position, currentState);
                _wasMovingLastFrame = false;
            }
            // When idle and was already idle: send nothing. No heartbeat.
            // Server tick loop broadcasts player:state regardless.
            // Socket.io ping/pong keeps connection alive.
        }

        private void SendMove(NetworkManager net, Vector3 pos, string state)
        {
            var payload = new PlayerMovePayload
            {
                playerId      = net.LocalPlayerId,
                position      = SVector3.FromUnity(pos),
                rotation      = SQuaternion.FromUnity(transform.rotation),
                velocity      = SVector3.FromUnity(_cc.velocity),
                movementState = state,
            };

            _lastSentPosition = pos;
            _lastSentState    = state;
            _lastSentTime     = Time.time;

            _sendCount++;
            if (_sendCount % 20 == 1)
                Debug.Log($"[PNS] SEND #{_sendCount} pos={payload.position} state={payload.movementState}");

            net.SendPlayerMove(payload);
        }

        private string GetMovementState()
        {
            if (_input == null) return "idle";

            switch (_input.CurrentState)
            {
                case PlayerInputController.MovementState.Walk:
                case PlayerInputController.MovementState.CrouchWalk:
                    return "walking";
                case PlayerInputController.MovementState.Sprint:
                    return "sprinting";
                default:
                    return "idle";
            }
        }
    }
}
