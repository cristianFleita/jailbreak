using System.Collections;
using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Player
{
    /// <summary>
    /// Attach to the LOCAL player GameObject.
    /// - Sends player:move every 50ms
    /// - Receives server corrections and applies rubber-band reconciliation
    ///
    /// Requires: CharacterController on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerNetworkSync : MonoBehaviour
    {
        [Header("Tuning")]
        [SerializeField] private float sendInterval = NetworkManager.TickInterval;

        private CharacterController _cc;
        private Coroutine _sendLoop;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (_cc == null) Debug.LogError("[PNS] CharacterController not found");
        }

        private void Start()
        {
            var net = NetworkManager.Instance;
            if (net == null)
            {
                Debug.LogError("[PNS] NetworkManager not found");
                return;
            }

            net.OnPlayerStateEvent += HandlePlayerState;
            net.OnConnectedEvent += OnConnected;
            net.OnDisconnectedEvent += OnDisconnected;
        }

        private void OnDestroy()
        {
            var net = NetworkManager.Instance;
            if (net == null) return;
            net.OnPlayerStateEvent -= HandlePlayerState;
            net.OnConnectedEvent -= OnConnected;
            net.OnDisconnectedEvent -= OnDisconnected;
        }

        private void OnConnected()
        {
            if (_sendLoop != null) StopCoroutine(_sendLoop);
            _sendLoop = StartCoroutine(SendMovementLoop());
            Debug.Log("[PNS] Connected — movement send loop started");
        }

        private void OnDisconnected()
        {
            if (_sendLoop != null) { StopCoroutine(_sendLoop); _sendLoop = null; }
            Debug.Log("[PNS] Disconnected — movement send loop stopped");
        }

        // ─── Send Loop (every 50ms) ──────────────────────────────────────────

        private IEnumerator SendMovementLoop()
        {
            var wait = new WaitForSeconds(sendInterval);
            while (true)
            {
                yield return wait;
                SendMove();
            }
        }

        private void SendMove()
        {
            var net = NetworkManager.Instance;
            if (net == null || net.State != ConnectionState.InGame) return;

            var payload = new PlayerMovePayload
            {
                playerId = net.LocalPlayerId,
                position = SVector3.FromUnity(transform.position),
                rotation = SQuaternion.FromUnity(transform.rotation),
                velocity = SVector3.FromUnity(_cc.velocity),
                movementState = GetMovementState()
            };

            net.SendPlayerMove(payload);
        }

        private string GetMovementState()
        {
            if (!_cc.isGrounded) return "idle";

            var horzVel = new Vector3(_cc.velocity.x, 0, _cc.velocity.z);
            var speed = horzVel.magnitude;

            if (speed < 0.1f) return "idle";
            if (speed < 4f) return "walking";
            return "sprinting";
        }

        // ─── Server Reconciliation (Rubber-Band) ─────────────────────────────

        private void HandlePlayerState(PlayerStateUpdate data)
        {
            var net = NetworkManager.Instance;
            if (net == null) return;

            if (data.players == null) return;

            foreach (var p in data.players)
            {
                if (p.id != net.LocalPlayerId) continue;

                ApplyServerCorrection(p.position.ToUnity(), p.rotation.ToUnity());
                break;
            }
        }

        private void ApplyServerCorrection(Vector3 serverPos, Quaternion serverRot)
        {
            float diff = Vector3.Distance(transform.position, serverPos);

            if (diff >= NetworkManager.ReconciliationThreshold)
            {
                // Large divergence (>1m) — teleport immediately
                if (_cc.enabled)
                {
                    _cc.enabled = false;
                    transform.position = serverPos;
                    _cc.enabled = true;
                }
                else
                {
                    transform.position = serverPos;
                }

                Debug.Log($"[PNS] Rubber-band teleport (diff={diff:F2}m)");
            }
            else if (diff > 0.05f)
            {
                // Small divergence (5cm-1m) — smooth lerp
                var oldPos = transform.position;
                var newPos = Vector3.Lerp(
                    oldPos,
                    serverPos,
                    NetworkManager.ReconciliationLerpSpeed * Time.deltaTime / NetworkManager.TickInterval
                );

                if (_cc.enabled)
                {
                    _cc.enabled = false;
                    transform.position = newPos;
                    _cc.enabled = true;
                }
                else
                {
                    transform.position = newPos;
                }
            }
            // <5cm: ignore (normal floating point noise)

            // Rotation
            transform.rotation = Quaternion.Slerp(transform.rotation, serverRot, 0.5f);
        }
    }
}
