using System;
using System.Runtime.InteropServices;
using Jailbreak.Network;
using Jailbreak.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Jailbreak.Audio
{
    public enum VoiceConnectionState
    {
        Unavailable,
        Idle,
        PermissionPending,
        Connected,
        Muted,
        Error
    }

    /// <summary>
    /// Runtime owner for proximity voice in WebGL.
    ///
    /// Unity owns gameplay truth: room, user, alive/captured state, poses, and
    /// push-to-talk input. VoiceBridge.jslib owns browser microphone capture,
    /// WebRTC media transport, and Web Audio spatialization.
    /// </summary>
    public class VoiceChatManager : MonoBehaviour
    {
        public static VoiceChatManager Instance { get; private set; }

        [Header("Push To Talk")]
        [SerializeField] private bool voiceEnabled = true;
        [SerializeField] private bool localMuted;

        [Header("Spatial Voice")]
        [SerializeField, Min(1f)] private float voiceRange = 10f;
        [SerializeField, Min(0f)] private float fullVolumeDistance = 2f;
        [SerializeField, Min(1f)] private float poseSendRate = 10f;

        [Header("Occlusion")]
        [Tooltip("Optional. Assign wall/door/bar layers to reduce voice through blockers.")]
        [SerializeField] private LayerMask occlusionMask = 0;
        [SerializeField, Range(0f, 1f)] private float occludedVolumeMultiplier = 0.35f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs;

        public VoiceConnectionState State { get; private set; } = VoiceConnectionState.Idle;
        public bool IsPushToTalkActive { get; private set; }
        public bool LocalMuted => localMuted;

        private bool _initialized;
        private bool _subscribed;
        private float _poseTimer;
        private float _initializedAtRealtime;
        private Transform _localPlayer;
        private Camera _localCamera;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void Voice_Init(string json);
        [DllImport("__Internal")] private static extern void Voice_SetPushToTalk(string json);
        [DllImport("__Internal")] private static extern void Voice_SetLocalMuted(string json);
        [DllImport("__Internal")] private static extern void Voice_SetListenerPose(string json);
        [DllImport("__Internal")] private static extern void Voice_SetSpeakerPose(string json);
        [DllImport("__Internal")] private static extern void Voice_Dispose();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (Instance != null) return;
            var go = new GameObject("VoiceChatManager");
            go.AddComponent<VoiceChatManager>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnEnable()
        {
            SubscribeIfReady();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;

            DisposeVoice();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Unsubscribe();
            Instance = null;
        }

        private void Update()
        {
            SubscribeIfReady();

            if (!voiceEnabled)
            {
                if (_initialized) DisposeVoice();
                return;
            }

            var net = NetworkManager.Instance;
            if (_initialized && ShouldDispose(net))
            {
                DisposeVoice();
                return;
            }

            if (!_initialized)
            {
                TryInitialize(net);
                return;
            }

            if (State == VoiceConnectionState.PermissionPending &&
                Time.realtimeSinceStartup - _initializedAtRealtime > 1f)
            {
                State = localMuted ? VoiceConnectionState.Muted : VoiceConnectionState.Connected;
            }

            UpdatePushToTalk();
            UpdatePoses();
        }

        public void SetVoiceEnabled(bool enabled)
        {
            voiceEnabled = enabled;
            if (!voiceEnabled) DisposeVoice();
        }

        public void SetLocalMuted(bool muted)
        {
            localMuted = muted;
            State = muted ? VoiceConnectionState.Muted : VoiceConnectionState.Connected;
            SendLocalMuted();
            if (muted) SetPushToTalk(false);
        }

        private void TryInitialize(NetworkManager net)
        {
            if (net == null || net.State != ConnectionState.InGame) return;
            if (string.IsNullOrEmpty(net.CurrentRoomId) || string.IsNullOrEmpty(net.LocalUserId)) return;
            if (!IsVoiceScene()) return;

            _localPlayer = FindLocalPlayer();
            if (_localPlayer == null) return;

            _localCamera = _localPlayer.GetComponentInChildren<Camera>(true);

            var payload = new VoiceInitPayload
            {
                roomId = net.CurrentRoomId,
                userId = net.LocalUserId,
                range = voiceRange,
                fullVolumeDistance = fullVolumeDistance,
                occlusionMultiplier = occludedVolumeMultiplier,
            };

#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                Voice_Init(JsonUtility.ToJson(payload));
                _initialized = true;
                SendLocalMuted();
                _initializedAtRealtime = Time.realtimeSinceStartup;
                State = VoiceConnectionState.PermissionPending;
                if (debugLogs) Debug.Log($"[Voice] Initialized for room {payload.roomId} as {payload.userId}", this);
            }
            catch (Exception ex)
            {
                State = VoiceConnectionState.Error;
                Debug.LogError($"[Voice] Init failed: {ex}", this);
            }
#else
            _initialized = true;
            State = VoiceConnectionState.Unavailable;
            if (debugLogs) Debug.Log("[Voice] WebRTC voice is only active in WebGL builds.", this);
#endif
        }

        private bool ShouldDispose(NetworkManager net)
        {
            if (!IsVoiceScene()) return true;
            if (net == null || net.State != ConnectionState.InGame) return true;
            if (string.IsNullOrEmpty(net.CurrentRoomId)) return true;
            return false;
        }

        private void UpdatePushToTalk()
        {
            var shouldTransmit = CanTransmit() && IsSpaceHeldForPushToTalk();
            if (shouldTransmit == IsPushToTalkActive) return;
            SetPushToTalk(shouldTransmit);
        }

        private void SetPushToTalk(bool active)
        {
            IsPushToTalkActive = active;

            var payload = new BoolPayload { active = active };
#if UNITY_WEBGL && !UNITY_EDITOR
            Voice_SetPushToTalk(JsonUtility.ToJson(payload));
#endif

            if (debugLogs) Debug.Log($"[Voice] Push-to-talk {(active ? "on" : "off")}", this);
        }

        private void SendLocalMuted()
        {
            var payload = new MutePayload { muted = localMuted };
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_initialized) Voice_SetLocalMuted(JsonUtility.ToJson(payload));
#endif
        }

        private void UpdatePoses()
        {
            _poseTimer -= Time.unscaledDeltaTime;
            if (_poseTimer > 0f) return;
            _poseTimer = 1f / Mathf.Max(1f, poseSendRate);

            if (_localPlayer == null) _localPlayer = FindLocalPlayer();
            if (_localPlayer == null) return;
            if (_localCamera == null) _localCamera = _localPlayer.GetComponentInChildren<Camera>(true);

            var listener = _localCamera != null ? _localCamera.transform : _localPlayer;
            SendListenerPose(listener);
            SendSpeakerPoses(listener.position);
        }

        private void SendListenerPose(Transform listener)
        {
            var payload = new ListenerPosePayload
            {
                position = SVector3.FromUnity(listener.position),
                forward = SVector3.FromUnity(listener.forward),
                up = SVector3.FromUnity(listener.up),
            };

#if UNITY_WEBGL && !UNITY_EDITOR
            Voice_SetListenerPose(JsonUtility.ToJson(payload));
#endif
        }

        private void SendSpeakerPoses(Vector3 listenerPosition)
        {
            var gsm = GameStateManager.Instance;
            var net = NetworkManager.Instance;
            if (gsm == null || net == null || string.IsNullOrEmpty(net.LocalUserId)) return;

            foreach (var entry in gsm.Players)
            {
                var player = entry.Value;
                if (player == null || player.userId == net.LocalUserId) continue;

                var speakerPosition = ResolveSpeakerPosition(entry.Key, player);
                var payload = new SpeakerPosePayload
                {
                    userId = player.userId,
                    position = SVector3.FromUnity(speakerPosition),
                    alive = player.isAlive,
                    captured = !player.isAlive,
                    role = player.role,
                    occluded = IsOccluded(listenerPosition, speakerPosition),
                };

#if UNITY_WEBGL && !UNITY_EDITOR
                Voice_SetSpeakerPose(JsonUtility.ToJson(payload));
#endif
            }
        }

        private Vector3 ResolveSpeakerPosition(string playerId, PlayerStateData player)
        {
            var gsm = GameStateManager.Instance;
            if (gsm != null &&
                gsm.RemotePlayerGameObjects.TryGetValue(playerId, out var go) &&
                go != null)
            {
                return go.transform.position;
            }

            return player.position.ToUnity();
        }

        private bool IsOccluded(Vector3 from, Vector3 to)
        {
            if (occlusionMask.value == 0) return false;

            var delta = to - from;
            var distance = delta.magnitude;
            if (distance <= 0.01f) return false;

            return Physics.Raycast(
                from,
                delta / distance,
                distance,
                occlusionMask,
                QueryTriggerInteraction.Ignore);
        }

        private bool CanTransmit()
        {
            if (localMuted) return false;
            if (SpectatorController.Instance != null && SpectatorController.Instance.IsSpectating) return false;

            var net = NetworkManager.Instance;
            var gsm = GameStateManager.Instance;
            if (net == null || gsm == null || string.IsNullOrEmpty(net.LocalUserId)) return true;

            foreach (var entry in gsm.Players)
            {
                var player = entry.Value;
                if (player != null && player.userId == net.LocalUserId)
                    return player.isAlive;
            }

            return true;
        }

        private bool IsSpaceHeldForPushToTalk()
        {
            if (IsTextInputFocused()) return false;

            var kb = Keyboard.current;
            return kb != null && kb.spaceKey.isPressed;
        }

        private static bool IsTextInputFocused()
        {
            var eventSystem = EventSystem.current;
            var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            if (selected == null) return false;

            return selected.GetComponent("TMP_InputField") != null ||
                   selected.GetComponent("InputField") != null;
        }

        private static Transform FindLocalPlayer()
        {
#if UNITY_2023_1_OR_NEWER
            var sync = FindFirstObjectByType<PlayerNetworkSync>();
#else
            var sync = FindObjectOfType<PlayerNetworkSync>();
#endif
            return sync != null ? sync.transform : null;
        }

        private static bool IsVoiceScene()
        {
            return SceneManager.GetActiveScene().name == "GameScene";
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _localPlayer = null;
            _localCamera = null;
            if (scene.name != "GameScene") DisposeVoice();
        }

        private void SubscribeIfReady()
        {
            if (_subscribed) return;
            var net = NetworkManager.Instance;
            if (net == null) return;

            net.OnGameEndEvent += OnGameEnded;
            net.OnDisconnectedEvent += OnNetworkDisconnected;
            net.OnRoomDestroyedEvent += OnRoomClosed;
            net.OnRoomKickedEvent += OnRoomClosed;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            var net = NetworkManager.Instance;
            if (net != null)
            {
                net.OnGameEndEvent -= OnGameEnded;
                net.OnDisconnectedEvent -= OnNetworkDisconnected;
                net.OnRoomDestroyedEvent -= OnRoomClosed;
                net.OnRoomKickedEvent -= OnRoomClosed;
            }
            _subscribed = false;
        }

        private void OnGameEnded(GameEndPayload _) => DisposeVoice();
        private void OnNetworkDisconnected() => DisposeVoice();
        private void OnRoomClosed(RoomDestroyedPayload _) => DisposeVoice();
        private void OnRoomClosed(RoomKickedPayload _) => DisposeVoice();

        private void DisposeVoice()
        {
            if (!_initialized) return;

            if (IsPushToTalkActive) SetPushToTalk(false);
#if UNITY_WEBGL && !UNITY_EDITOR
            Voice_Dispose();
#endif
            _initialized = false;
            _localPlayer = null;
            _localCamera = null;
            State = VoiceConnectionState.Idle;
            if (debugLogs) Debug.Log("[Voice] Disposed", this);
        }

        [Serializable]
        private class VoiceInitPayload
        {
            public string roomId;
            public string userId;
            public float range;
            public float fullVolumeDistance;
            public float occlusionMultiplier;
        }

        [Serializable]
        private class BoolPayload
        {
            public bool active;
        }

        [Serializable]
        private class MutePayload
        {
            public bool muted;
        }

        [Serializable]
        private class ListenerPosePayload
        {
            public SVector3 position;
            public SVector3 forward;
            public SVector3 up;
        }

        [Serializable]
        private class SpeakerPosePayload
        {
            public string userId;
            public SVector3 position;
            public bool alive;
            public bool captured;
            public string role;
            public bool occluded;
        }
    }
}
