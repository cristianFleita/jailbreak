using System.Collections.Generic;
using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Player
{
    /// <summary>
    /// Attach to REMOTE player GameObjects (spawned by GameStateManager).
    /// Buffers received positions and interpolates with 100ms delay.
    /// </summary>
    public class RemotePlayerSync : MonoBehaviour
    {
        private readonly List<(float time, Vector3 pos, Quaternion rot)> _buffer = new();

        public string PlayerId { get; set; }
        public string Role { get; set; }
        
        // Expose the state arriving from the network so the Animator can read it
        public string MovementState { get; private set; } = "idle";

        // ─── Called by GameStateManager when player:state arrives ────────────

        public void PushState(PlayerStateData data)
        {
            MovementState = data.movementState;
            _buffer.Add((Time.time, data.position.ToUnity(), data.rotation.ToUnity()));

            // Prune old entries (keep last 500ms worth)
            float cutoff = Time.time - 0.5f;
            _buffer.RemoveAll(e => e.time < cutoff);
        }

        // ─── Interpolation (100ms = INTERPOLATION_BUFFER behind present) ─────

        private void Update()
        {
            if (_buffer.Count < 1) return;

            float renderTime = Time.time - NetworkManager.InterpolationBuffer;

            // Find the two buffer entries that straddle renderTime
            int i = 0;
            while (i < _buffer.Count - 1 && _buffer[i + 1].time <= renderTime)
                i++;

            if (i >= _buffer.Count - 1)
            {
                // No future sample yet — hold at last known position (with slight lerp)
                var last = _buffer[^1];
                transform.position = Vector3.Lerp(transform.position, last.pos, 0.5f * Time.deltaTime * 60f);
                transform.rotation = last.rot;
                return;
            }

            var from = _buffer[i];
            var to = _buffer[i + 1];
            float span = to.time - from.time;
            float alpha = span > 0.0001f ? (renderTime - from.time) / span : 0f;
            alpha = Mathf.Clamp01(alpha);

            transform.position = Vector3.Lerp(from.pos, to.pos, alpha);
            transform.rotation = Quaternion.Slerp(from.rot, to.rot, alpha);
        }
    }
}