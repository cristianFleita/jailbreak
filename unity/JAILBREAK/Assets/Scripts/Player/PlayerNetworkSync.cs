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

            // If already connected/in-game (scene loaded after connection), start immediately
            if (net.State == ConnectionState.InGame || net.State == ConnectionState.Connected)
            {
                Debug.Log("[PNS] Already connected — starting movement send loop");
                OnConnected();
            }
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

        private int _sendCount;

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

            _sendCount++;
            if (_sendCount % 20 == 1) // Log every ~1s (every 20th send at 50ms interval)
                Debug.Log($"[PNS] SEND player:move #{_sendCount} id={payload.playerId} pos={payload.position} state={payload.movementState}");

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
        // The local player trusts its own client-side prediction.
        // Only hard-teleport on major desync (>5m), which indicates cheating
        // or a genuine disconnect/reconnect. Normal latency (~1-2m at sprint)
        // is expected and should NOT trigger corrections.

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
                // Major desync (>5m) — hard teleport (cheat detection / reconnect)
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

                transform.rotation = serverRot;
                Debug.LogWarning($"[PNS] Hard teleport — major desync (diff={diff:F2}m)");
            }
            // Below threshold: trust client-side prediction, don't fight the input controller
        }
    }
}
