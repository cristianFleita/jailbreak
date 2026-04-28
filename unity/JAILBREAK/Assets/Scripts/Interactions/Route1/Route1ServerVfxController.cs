using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Interactions.Route1
{
    /// <summary>
    /// Drives clue-glow + sabotage spark VFX for a single Route1 server (Control prefab).
    ///
    /// Behavior:
    /// - Rim glow loops on this server when, for the LOCAL prisoner, the clue has been
    ///   found and this server's id matches state.correctServerId (and ventilation is
    ///   still powered). Glow turns off when ventilation goes down or clue clears.
    /// - Sparks play once on this server when a WRONG sabotage cue lands within
    ///   wrongMatchRadius of this transform (server_wrong_alarm).
    /// - Sparks play once on EVERY server when ventilation transitions powered → off
    ///   (correct sabotage). The transition is detected from the WorldStatePayload.
    /// </summary>
    [DisallowMultipleComponent]
    public class Route1ServerVfxController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Rim glow controller on this server's mesh. Loop is started/stopped from here.")]
        public RimGlowController glowController;

        [Tooltip("Sparks particle system that plays on wrong-server sabotage and on global power-off.")]
        public ParticleSystem sparksVfx;

        [Tooltip("Sibling sabotage interactable. Used to read the canonical RouteObjectId for this server.")]
        public ServerSabotageInteractable serverInteractable;

        [Header("Wrong-Server Match")]
        [Tooltip("Max distance (world units) from a server_wrong_alarm cue's position to this transform to consider it 'this server'.")]
        [Min(0f)] public float wrongMatchRadius = 1.5f;

        [Header("Behavior")]
        [Tooltip("Only show the clue glow to prisoners. Guards never receive Route1State so this is mostly cosmetic.")]
        public bool prisonersOnlyGlow = true;

        [Header("Debug")]
        public bool debugLogs;

        private bool networkSubscribed;
        private bool gameStateSubscribed;
        private bool lastVentilationPowered = true;
        private bool seenWorldState;
        private bool glowActive;

        private void Reset()
        {
            glowController     = GetComponentInChildren<RimGlowController>(true);
            serverInteractable = GetComponentInChildren<ServerSabotageInteractable>(true);
            sparksVfx          = GetComponentInChildren<ParticleSystem>(true);
        }

        private void OnEnable()
        {
            SubscribeIfNeeded();
        }

        private void Start()
        {
            SubscribeIfNeeded();

            var net = NetworkManager.Instance;
            if (net != null && net.CachedWorldState != null)
            {
                lastVentilationPowered = net.CachedWorldState.ventilationPowered;
                seenWorldState         = true;
            }

            var gsm = GameStateManager.Instance;
            if (gsm != null && gsm.Route1State != null)
                ApplyRoute1State(gsm.Route1State);
        }

        private void Update()
        {
            // Late-bound singletons: the controllers may instantiate after this component.
            SubscribeIfNeeded();
        }

        private void OnDisable()
        {
            var net = NetworkManager.Instance;
            if (net != null && networkSubscribed)
            {
                net.OnWorldCueEvent   -= HandleWorldCue;
                net.OnWorldStateEvent -= HandleWorldState;
            }
            networkSubscribed = false;

            var gsm = GameStateManager.Instance;
            if (gsm != null && gameStateSubscribed)
                gsm.onRoute1StateChanged.RemoveListener(ApplyRoute1State);
            gameStateSubscribed = false;
        }

        private void SubscribeIfNeeded()
        {
            var net = NetworkManager.Instance;
            if (net != null && !networkSubscribed)
            {
                net.OnWorldCueEvent   += HandleWorldCue;
                net.OnWorldStateEvent += HandleWorldState;
                networkSubscribed = true;
            }

            var gsm = GameStateManager.Instance;
            if (gsm != null && !gameStateSubscribed)
            {
                gsm.onRoute1StateChanged.AddListener(ApplyRoute1State);
                gameStateSubscribed = true;
            }
        }

        private void HandleWorldState(WorldStatePayload payload)
        {
            if (payload == null) return;

            bool poweredNow = payload.ventilationPowered;
            if (seenWorldState && lastVentilationPowered && !poweredNow)
            {
                // Correct sabotage went through — every server arcs.
                PlaySparks();
                // Power is gone, so this server can't be the "glowing clue" anymore.
                StopGlow();
            }

            lastVentilationPowered = poweredNow;
            seenWorldState         = true;
        }

        private void HandleWorldCue(WorldCuePayload payload)
        {
            if (payload == null || payload.cue != "server_wrong_alarm") return;

            Vector3 cuePos = payload.position.ToUnity();
            float   sqrTol = wrongMatchRadius * wrongMatchRadius;
            if ((transform.position - cuePos).sqrMagnitude > sqrTol) return;

            if (debugLogs)
                Debug.Log($"[Route1ServerVfx] wrong-alarm matched server '{ServerObjectId()}' (dist within {wrongMatchRadius}m)", this);

            PlaySparks();
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

            string myId = ServerObjectId();
            bool shouldGlow =
                state.clueFound &&
                !state.serverDisabled &&
                !string.IsNullOrEmpty(state.correctServerId) &&
                !string.IsNullOrEmpty(myId) &&
                state.correctServerId == myId;

            if (shouldGlow) StartGlow();
            else            StopGlow();
        }

        private string ServerObjectId()
        {
            if (serverInteractable == null) return null;
            // RouteObjectId is protected; resolve via NetworkInteractable as fallback.
            var ni = serverInteractable.GetComponent<NetworkInteractable>();
            string fromInteractable = serverInteractable.routeObjectId;
            if (!string.IsNullOrEmpty(fromInteractable)) return fromInteractable;
            return ni != null ? ni.NetworkId : null;
        }

        private void StartGlow()
        {
            if (glowController == null || glowActive) return;
            glowActive = true;
            glowController.enabled = true;
            glowController.StartLoop();
            if (debugLogs) Debug.Log($"[Route1ServerVfx] Start glow on '{ServerObjectId()}'", this);
        }

        private void StopGlow()
        {
            if (glowController == null || !glowActive) return;
            glowActive = false;
            glowController.StopLoop();
            if (debugLogs) Debug.Log($"[Route1ServerVfx] Stop glow on '{ServerObjectId()}'", this);
        }

        private void PlaySparks()
        {
            if (sparksVfx == null) return;
            sparksVfx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            sparksVfx.Clear(true);
            sparksVfx.Play(true);
        }
    }
}
