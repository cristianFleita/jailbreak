using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Interactions.Route1
{
    /// <summary>
    /// Drives rim-glow VFX on a Route1 vent grille.
    ///
    /// Behavior: once the correct server is disabled (state.serverDisabled = true)
    /// AND this vent is not yet open, the rim glow loops to guide the prisoner.
    /// As soon as the vent is opened the glow turns off.
    /// </summary>
    [DisallowMultipleComponent]
    public class Route1VentVfxController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Rim glow controller on this vent's grille mesh.")]
        public RimGlowController glowController;

        [Tooltip("Sibling vent interactable used to resolve this vent's RouteObjectId.")]
        public VentUnscrewInteractable ventInteractable;

        [Header("Behavior")]
        [Tooltip("Only show the guidance glow to prisoners. Guards still see the vent open visual via WorldState.")]
        public bool prisonersOnlyGlow = true;

        [Header("Debug")]
        public bool debugLogs;

        private bool gameStateSubscribed;
        private bool glowActive;

        private void Reset()
        {
            glowController   = GetComponentInChildren<RimGlowController>(true);
            ventInteractable = GetComponentInChildren<VentUnscrewInteractable>(true);
        }

        private void OnEnable()
        {
            SubscribeIfNeeded();
        }

        private void Start()
        {
            SubscribeIfNeeded();
            var gsm = GameStateManager.Instance;
            if (gsm != null && gsm.Route1State != null)
                ApplyRoute1State(gsm.Route1State);
        }

        private void Update()
        {
            SubscribeIfNeeded();
        }

        private void OnDisable()
        {
            var gsm = GameStateManager.Instance;
            if (gsm != null && gameStateSubscribed)
                gsm.onRoute1StateChanged.RemoveListener(ApplyRoute1State);
            gameStateSubscribed = false;
        }

        private void SubscribeIfNeeded()
        {
            var gsm = GameStateManager.Instance;
            if (gsm == null || gameStateSubscribed) return;
            gsm.onRoute1StateChanged.AddListener(ApplyRoute1State);
            gameStateSubscribed = true;
        }

        private void ApplyRoute1State(EscapeRoute1StatePayload state)
        {
            if (state == null) { StopGlow(); return; }

            if (prisonersOnlyGlow)
            {
                var gsm = GameStateManager.Instance;
                if (gsm != null && !string.IsNullOrEmpty(gsm.LocalRole) && gsm.LocalRole != "prisoner")
                {
                    StopGlow();
                    return;
                }
            }

            string ventId = VentObjectId();
            bool shouldGlow = state.serverDisabled && !IsVentOpen(state, ventId);

            if (shouldGlow) StartGlow();
            else            StopGlow();
        }

        private static bool IsVentOpen(EscapeRoute1StatePayload state, string ventId)
        {
            if (state == null || state.openVentIds == null || string.IsNullOrEmpty(ventId)) return false;
            foreach (var id in state.openVentIds)
                if (id == ventId) return true;
            return false;
        }

        private string VentObjectId()
        {
            if (ventInteractable == null) return null;
            string fromInteractable = ventInteractable.routeObjectId;
            if (!string.IsNullOrEmpty(fromInteractable)) return fromInteractable;
            var ni = ventInteractable.GetComponent<NetworkInteractable>();
            return ni != null ? ni.NetworkId : null;
        }

        private void StartGlow()
        {
            if (glowController == null || glowActive) return;
            glowActive = true;
            glowController.enabled = true;
            glowController.StartLoop();
            if (debugLogs) Debug.Log($"[Route1VentVfx] Start glow on vent '{VentObjectId()}'", this);
        }

        private void StopGlow()
        {
            if (glowController == null || !glowActive) return;
            glowActive = false;
            glowController.StopLoop();
            if (debugLogs) Debug.Log($"[Route1VentVfx] Stop glow on vent '{VentObjectId()}'", this);
        }
    }
}
