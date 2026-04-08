using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Player
{
    /// <summary>
    /// Colors the local player capsule based on the assigned role.
    /// Guard = red, Prisoner = blue.
    ///
    /// Attach to the LocalPlayer capsule GO alongside PlayerInputController.
    /// Works in Editor (SocketIOUnity) and WebGL (jslib) builds.
    /// </summary>
    public class LocalPlayerRoleVisual : MonoBehaviour
    {
        [Header("Role Colors")]
        public Color guardColor    = new Color(0.9f, 0.15f, 0.15f, 1f);
        public Color prisonerColor = new Color(0.25f, 0.55f, 0.85f, 1f);

        private bool _applied;

        private void Start()
        {
            var net = NetworkManager.Instance;
            if (net == null) return;

            net.OnGameStartEvent    += OnGameStart;
            net.OnPlayerJoinedEvent += OnPlayerJoined;
        }

        private void OnDestroy()
        {
            var net = NetworkManager.Instance;
            if (net == null) return;

            net.OnGameStartEvent    -= OnGameStart;
            net.OnPlayerJoinedEvent -= OnPlayerJoined;
        }

        private void Update()
        {
            // Retry each frame until role is known (handles WebGL timing)
            if (_applied) return;
            TryApplyRole();
        }

        private void OnGameStart(GameStartPayload data)   => TryApplyRole();
        private void OnPlayerJoined(PlayerJoinedPayload data) => TryApplyRole();

        private void TryApplyRole()
        {
            var gsm = GameStateManager.Instance;
            if (gsm == null || string.IsNullOrEmpty(gsm.LocalRole)) return;

            var color = gsm.LocalRole == "guard" ? guardColor : prisonerColor;
            ApplyColor(color);
            _applied = true;
            Debug.Log($"[LocalPlayerVisual] Role={gsm.LocalRole}, color applied");
        }

        private void ApplyColor(Color color)
        {
            var rend = GetComponent<Renderer>();
            if (rend == null) return;

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color); // URP Lit
            mpb.SetColor("_Color",     color); // Built-in RP fallback
            rend.SetPropertyBlock(mpb);
        }
    }
}
