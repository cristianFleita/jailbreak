using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Interactions.Route1
{
    public class VentEscapeInteractable : Route1ProgressInteractable
    {
        [Header("Tunnel Availability")]
        public GameObject tunnelVisual;
        public bool hideVisualUntilVentOpen = true;
        public bool disableAssignedCollidersUntilVentOpen = true;
        public Collider[] collidersToEnableWhenOpen;

        private bool subscribedWorldState;

        protected override string StartAction => "route1.escape.start";
        protected override string StopAction => "route1.escape.stop";
        protected override string StateAction => "route1.escape";
        protected override string DefaultProgressLabel => "Escaping...";
        protected override string DefaultStartLabel => "Escape";
        protected override float DefaultDurationSeconds => 5f;
        protected override int DefaultPriority => 35;

        protected override void Awake()
        {
            base.Awake();
            SetTunnelAvailable(IsVentOpen(GameStateManager.Instance != null ? GameStateManager.Instance.Route1State : null, RouteObjectId));
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SubscribeWorldStateIfNeeded();
        }

        protected override void Start()
        {
            base.Start();
            SubscribeWorldStateIfNeeded();

            var net = NetworkManager.Instance;
            if (net != null && net.CachedWorldState != null)
                ApplyWorldState(net.CachedWorldState);
        }

        protected override void Update()
        {
            base.Update();
            SubscribeWorldStateIfNeeded();
        }

        protected override void OnDisable()
        {
            var net = NetworkManager.Instance;
            if (net != null && subscribedWorldState)
                net.OnWorldStateEvent -= ApplyWorldState;
            subscribedWorldState = false;

            base.OnDisable();
        }

        protected override bool IsAvailable(EscapeRoute1StatePayload state)
        {
            return IsVentOpen(state, RouteObjectId);
        }

        protected override void OnServerStateApplied(EscapeRoute1StatePayload state)
        {
            SetTunnelAvailable(IsVentOpen(state, RouteObjectId));
        }

        private void SubscribeWorldStateIfNeeded()
        {
            if (subscribedWorldState) return;
            var net = NetworkManager.Instance;
            if (net == null) return;

            net.OnWorldStateEvent += ApplyWorldState;
            subscribedWorldState = true;
        }

        private void ApplyWorldState(WorldStatePayload state)
        {
            SetTunnelAvailable(Contains(state?.openVentIds, RouteObjectId));
        }

        private void SetTunnelAvailable(bool available)
        {
            bool visualEnabled = available || !hideVisualUntilVentOpen;

            if (tunnelVisual != null)
            {
                if (tunnelVisual == gameObject)
                    SetRenderersEnabled(tunnelVisual, visualEnabled);
                else
                    tunnelVisual.SetActive(visualEnabled);
            }

            if (collidersToEnableWhenOpen == null) return;
            bool collidersEnabled = available || !disableAssignedCollidersUntilVentOpen;
            foreach (var c in collidersToEnableWhenOpen)
                if (c != null) c.enabled = collidersEnabled;
        }

        private static bool Contains(string[] values, string needle)
        {
            if (values == null || string.IsNullOrEmpty(needle)) return false;
            foreach (var value in values)
                if (value == needle) return true;
            return false;
        }

        private static void SetRenderersEnabled(GameObject root, bool enabled)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
                if (renderer != null) renderer.enabled = enabled;
        }
    }
}
