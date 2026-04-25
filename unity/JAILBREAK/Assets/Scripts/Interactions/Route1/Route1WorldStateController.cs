using System;
using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Interactions.Route1
{
    public class Route1WorldStateController : MonoBehaviour
    {
        [Serializable]
        public class VentVisualEntry
        {
            public string ventId;
            public GameObject closedVisual;
            public GameObject openVisual;
            public Animator animator;
            public string openBool = "isOpen";
        }

        [Header("Ventilation Power")]
        public GameObject[] poweredVisuals;
        public GameObject[] unpoweredVisuals;
        public Animator[] fanAnimators;
        public string fanPoweredBool = "isPowered";

        [Header("Vents")]
        public VentVisualEntry[] vents;

        [Header("Cue Audio")]
        public AudioSource wrongServerAlarm;
        public AudioSource powerOffCue;
        public AudioSource ventOpenedCue;
        public AudioSource ventNoiseCue;

        [Header("Debug")]
        public bool debugLogs = false;

        private bool subscribed;

        private void OnEnable()
        {
            SubscribeIfNeeded();
        }

        private void Start()
        {
            SubscribeIfNeeded();
            var net = NetworkManager.Instance;
            if (net != null && net.CachedWorldState != null)
                ApplyWorldState(net.CachedWorldState);
        }

        private void Update()
        {
            SubscribeIfNeeded();
        }

        private void OnDisable()
        {
            var net = NetworkManager.Instance;
            if (net == null || !subscribed) return;

            net.OnWorldStateEvent -= ApplyWorldState;
            net.OnWorldCueEvent -= ApplyWorldCue;
            subscribed = false;
        }

        private void SubscribeIfNeeded()
        {
            if (subscribed) return;
            var net = NetworkManager.Instance;
            if (net == null) return;

            net.OnWorldStateEvent += ApplyWorldState;
            net.OnWorldCueEvent += ApplyWorldCue;
            subscribed = true;
        }

        private void ApplyWorldState(WorldStatePayload payload)
        {
            if (payload == null) return;

            SetObjectsActive(poweredVisuals, payload.ventilationPowered);
            SetObjectsActive(unpoweredVisuals, !payload.ventilationPowered);

            if (fanAnimators != null)
            {
                foreach (var animator in fanAnimators)
                    if (animator != null && !string.IsNullOrEmpty(fanPoweredBool))
                        animator.SetBool(fanPoweredBool, payload.ventilationPowered);
            }

            ApplyConfiguredVentVisuals(payload.openVentIds);
            ApplyInteractableVentVisuals(payload.openVentIds);

            if (debugLogs)
                Debug.Log($"[Route1WorldState] ventilationPowered={payload.ventilationPowered} openVents={string.Join(",", payload.openVentIds ?? Array.Empty<string>())}", this);
        }

        private void ApplyWorldCue(WorldCuePayload payload)
        {
            if (payload == null) return;

            switch (payload.cue)
            {
                case "server_wrong_alarm":
                    Play(wrongServerAlarm);
                    break;
                case "server_correct_power_off":
                    Play(powerOffCue);
                    break;
                case "vent_opened":
                    Play(ventOpenedCue);
                    break;
                case "vent_unscrew_noise":
                    Play(ventNoiseCue);
                    break;
            }

            if (debugLogs)
                Debug.Log($"[Route1WorldCue] {payload.cue} zone={payload.zone ?? ""} pos={payload.position}", this);
        }

        private void ApplyConfiguredVentVisuals(string[] openVentIds)
        {
            if (vents == null) return;

            foreach (var vent in vents)
            {
                if (vent == null || string.IsNullOrEmpty(vent.ventId)) continue;
                bool isOpen = Contains(openVentIds, vent.ventId);
                SetVentVisuals(vent.closedVisual, vent.openVisual, isOpen);
                if (vent.animator != null && !string.IsNullOrEmpty(vent.openBool))
                    vent.animator.SetBool(vent.openBool, isOpen);
            }
        }

        private void ApplyInteractableVentVisuals(string[] openVentIds)
        {
#if UNITY_2023_1_OR_NEWER
            var interactables = UnityEngine.Object.FindObjectsByType<VentUnscrewInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var interactables = UnityEngine.Object.FindObjectsOfType<VentUnscrewInteractable>(true);
#endif
            foreach (var vent in interactables)
            {
                if (vent == null) continue;
                var ni = vent.GetComponent<NetworkInteractable>();
                var ventId = !string.IsNullOrEmpty(vent.BackendObjectId)
                    ? vent.BackendObjectId
                    : ni != null ? ni.NetworkId : null;
                if (string.IsNullOrEmpty(ventId)) continue;
                vent.SetOpenVisual(Contains(openVentIds, ventId));
            }
        }

        private static void SetObjectsActive(GameObject[] objects, bool active)
        {
            if (objects == null) return;
            foreach (var go in objects)
                if (go != null) go.SetActive(active);
        }

        private static void SetVentVisuals(GameObject closedVisual, GameObject openVisual, bool isOpen)
        {
            if (closedVisual != null && closedVisual == openVisual)
            {
                closedVisual.SetActive(true);
                return;
            }

            if (closedVisual != null) closedVisual.SetActive(!isOpen);
            if (openVisual != null) openVisual.SetActive(isOpen);
        }

        private static bool Contains(string[] values, string needle)
        {
            if (values == null || string.IsNullOrEmpty(needle)) return false;
            foreach (var value in values)
                if (value == needle) return true;
            return false;
        }

        private static void Play(AudioSource source)
        {
            if (source == null) return;
            source.Play();
        }
    }
}
