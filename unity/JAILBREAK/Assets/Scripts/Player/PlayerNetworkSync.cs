using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Player
{
    /// <summary>
    /// Attach to the LOCAL player GameObject.
    /// - Sends player:move ONLY when the player is actively moving (WASD input).
    /// - Does NOT apply server corrections: the local player owns its position.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerNetworkSync : MonoBehaviour
    {
        [Header("Tuning")]
        [SerializeField] private float positionThreshold = 0.05f;
        [SerializeField] private float minSendInterval   = 0.05f; // 20 Hz cap

        private CharacterController _cc;
        private PlayerInputController _input;

        // Send-on-change tracking
        private Vector3 _lastSentPosition;
        private string  _lastSentState = "idle";
        private float   _lastSentTime;
        private bool    _hasSpawned;
        private bool    _wasMovingLastFrame;
        private int     _sendCount;
        private Vector3 _spawnPosition;
        private bool    _needsActivation;

        private void Awake()
        {
            _cc    = GetComponent<CharacterController>();
            _input = GetComponent<PlayerInputController>();
            if (_cc == null) Debug.LogError("[PNS] CharacterController not found");
            if (_input == null) Debug.LogError("[PNS] PlayerInputController not found");
        }

        public void TeleportToSpawn(Vector3 spawnPos)
        {
            _cc.enabled = false;

            float feetOffset = _cc.height * 0.5f - _cc.center.y;
            spawnPos.y += feetOffset;

            transform.position = spawnPos;

            _lastSentPosition = spawnPos;
            _spawnPosition = spawnPos;
            _hasSpawned = true;
            _needsActivation = true;

            Debug.Log($"[PNS] Teleported to spawn: {spawnPos} (feet offset +{feetOffset:F2})");
        }

        private void Update()
        {
            var net = NetworkManager.Instance;
            if (net == null || net.State != ConnectionState.InGame) return;
            if (!_hasSpawned) return;

            if (_needsActivation)
            {
                _needsActivation = false;
                transform.position = _spawnPosition;
                _cc.enabled = true;
                if (_input != null) _input.InputEnabled = true;
                return;
            }

            var currentState = GetMovementState();
            var currentPos   = transform.position;
            var isMoving     = currentState != "idle";
            var now          = Time.time;

            // Rate-limit while moving
            if (isMoving && (now - _lastSentTime < minSendInterval)) return;

            if (isMoving)
            {
                // Player is pressing WASD — send position if it changed enough
                if (Vector3.Distance(currentPos, _lastSentPosition) >= positionThreshold)
                {
                    SendMove(net, currentPos, currentState);
                    _wasMovingLastFrame = true;
                }
            }
            else if (_wasMovingLastFrame)
            {
                // Player just released keys — send one final "stopped" position
                SendMove(net, currentPos, currentState);
                _wasMovingLastFrame = false;
            }
        }

        private void SendMove(NetworkManager net, Vector3 pos, string state)
        {
            var payload = new PlayerMovePayload
            {
                playerId      = net.LocalPlayerId,
                position      = SVector3.FromUnity(pos),
                // Rotation is still included so remote players see where you are looking
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