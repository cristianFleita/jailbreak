using System.Collections.Generic;
using Jailbreak.Network;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Jailbreak.Player
{
    /// <summary>
    /// Counter-Strike style spectator camera for captured prisoners.
    ///
    /// Trigger: GameStateManager.onPlayerCaught fires with our own id.
    /// Behavior:
    ///   - Disables local input, CC, network sync, and the FPS camera controller.
    ///   - Re-uses the existing local FPS camera (un-parents it from the dead body).
    ///   - Each LateUpdate snaps the camera to the spectated target's head bone.
    ///   - Left / Right arrow keys cycle through alive prisoner targets.
    ///   - Auto-cycles when the current target dies or disconnects.
    ///
    /// Self-bootstraps via RuntimeInitializeOnLoadMethod, so no scene wiring needed.
    /// </summary>
    public class SpectatorController : MonoBehaviour
    {
        // ─── Tuning ─────────────────────────────────────────────────────────
        [Header("Tuning")]
        [Tooltip("If true, only alive prisoners are spectatable. If false, the guard is also a valid target.")]
        public bool watchOnlyPrisoners = true;

        [Tooltip("Vertical offset applied to the spectated target when no head bone is found.")]
        public float fallbackEyeHeight = 1.65f;

        [Tooltip("Forward offset along target body forward (0 = pure first-person; positive = slightly inside head).")]
        public float forwardOffset = 0.05f;

        // ─── Public API ─────────────────────────────────────────────────────
        public static SpectatorController Instance { get; private set; }
        public bool IsSpectating { get; private set; }
        public string CurrentTargetId { get; private set; }
        public string CurrentTargetDisplay { get; private set; }

        public System.Action<string> OnTargetChanged;

        // ─── Private ────────────────────────────────────────────────────────
        private GameObject _localPlayer;
        private Camera     _camera;
        private FPSCameraController _fpsCam;
        private readonly List<string> _orderedTargets = new();
        private int _currentIndex = -1;
        private bool _hooked;

        // Track which renderers we explicitly hid so we restore exactly those on cleanup.
        private readonly List<Renderer> _hiddenRenderers = new();

        // ─── Bootstrap ──────────────────────────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (Instance != null) return;
            var go = new GameObject("SpectatorController");
            go.AddComponent<SpectatorController>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            TryHook();
        }

        private void OnDisable()
        {
            Unhook();
        }

        private void Update()
        {
            // GameStateManager may load after we do — keep retrying to hook until it exists.
            if (!_hooked) TryHook();

            // F5-reconnect-after-capture: server sends us back with isAlive=false.
            // The catch event already played on the original socket, so we won't get
            // a fresh onPlayerCaught — detect the dead-on-arrival case here.
            if (!IsSpectating) DetectReconnectAsCaptured();

            if (!IsSpectating) return;

            RefreshTargets();

            // Cycle inputs (arrow keys, like CS).
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.rightArrowKey.wasPressedThisFrame) Cycle(+1);
                else if (kb.leftArrowKey.wasPressedThisFrame) Cycle(-1);
            }
        }

        private void LateUpdate()
        {
            if (!IsSpectating) return;
            FollowTarget();
        }

        // ─── Hook / Unhook ──────────────────────────────────────────────────
        private void TryHook()
        {
            var gsm = GameStateManager.Instance;
            if (gsm == null) return;
            gsm.onPlayerCaught.AddListener(HandlePlayerCaught);
            gsm.onGameEnd.AddListener(HandleGameEnd);
            _hooked = true;
        }

        private void Unhook()
        {
            if (!_hooked) return;
            var gsm = GameStateManager.Instance;
            if (gsm != null)
            {
                gsm.onPlayerCaught.RemoveListener(HandlePlayerCaught);
                gsm.onGameEnd.RemoveListener(HandleGameEnd);
            }
            _hooked = false;
        }

        // ─── Catch handler ──────────────────────────────────────────────────
        private void HandlePlayerCaught(string targetId)
        {
            var localId = NetworkManager.Instance?.LocalPlayerId;

            if (!string.IsNullOrEmpty(localId) && targetId == localId)
            {
                EnterSpectatorMode();
                return;
            }

            // Currently-spectated player got caught → cycle off them.
            if (IsSpectating && targetId == CurrentTargetId)
                Cycle(+1);
        }

        private void HandleGameEnd(string _)
        {
            ExitSpectatorMode();
        }

        private void DetectReconnectAsCaptured()
        {
            var gsm = GameStateManager.Instance;
            if (gsm == null) return;
            if (!gsm.GameActive) return;
            var localId = NetworkManager.Instance?.LocalPlayerId;
            if (string.IsNullOrEmpty(localId)) return;

            foreach (var kvp in gsm.Players)
            {
                var p = kvp.Value;
                if (p == null) continue;
                if (p.userId != localId) continue;
                if (!p.isAlive)
                {
                    Debug.Log("[Spectator] Detected reconnect-as-captured — entering spectator mode");
                    EnterSpectatorMode();
                }
                return;
            }
        }

        // ─── Enter / Exit ───────────────────────────────────────────────────
        private void EnterSpectatorMode()
        {
            if (IsSpectating) return;

            // Locate the local prisoner instance via its components; PlayerSpawnController
            // also exposes a reference but is not always reachable from here.
            var localPns = FindAnyObjectByType<PlayerNetworkSync>();
            _localPlayer = localPns != null ? localPns.gameObject : null;

            if (_localPlayer == null)
            {
                Debug.LogWarning("[Spectator] Local player not found — entering spectator without local cleanup");
            }
            else
            {
                // Freeze the dead body: input off, CC off, network sync off.
                foreach (var ic in _localPlayer.GetComponentsInChildren<PlayerInputController>(true))
                    ic.InputEnabled = false;

                foreach (var cc in _localPlayer.GetComponentsInChildren<CharacterController>(true))
                    cc.enabled = false;

                foreach (var pns in _localPlayer.GetComponentsInChildren<PlayerNetworkSync>(true))
                    pns.enabled = false;

                // Hide our own visuals so the camera isn't stuck inside our model.
                _hiddenRenderers.Clear();
                foreach (var r in _localPlayer.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.enabled)
                    {
                        r.enabled = false;
                        _hiddenRenderers.Add(r);
                    }
                }

                // Take over the FPS camera. Disable the controller so it stops
                // overriding our transform; un-parent so the body's transform
                // doesn't drag the camera around.
                _fpsCam = _localPlayer.GetComponentInChildren<FPSCameraController>(true);
                if (_fpsCam != null)
                {
                    _fpsCam.enabled = false;
                    _camera = _fpsCam.GetComponent<Camera>();
                }
                if (_camera == null)
                    _camera = _localPlayer.GetComponentInChildren<Camera>(true);

                if (_camera != null)
                    _camera.transform.SetParent(null, worldPositionStays: true);
            }

            IsSpectating = true;
            RefreshTargets(forceTargetChange: true);
            Debug.Log($"[Spectator] Entered spectator mode. Targets: {_orderedTargets.Count}");
        }

        private void ExitSpectatorMode()
        {
            if (!IsSpectating) return;
            IsSpectating = false;
            CurrentTargetId = null;
            CurrentTargetDisplay = null;
            _orderedTargets.Clear();
            _currentIndex = -1;
            // We deliberately do NOT re-enable input / restore renderers / re-parent
            // the camera. The captured player is dead for the rest of the round; the
            // EndGameUI scene takes over from here.
        }

        // ─── Target list management ─────────────────────────────────────────
        private void RefreshTargets(bool forceTargetChange = false)
        {
            var gsm = GameStateManager.Instance;
            if (gsm == null) return;
            var localId = NetworkManager.Instance?.LocalPlayerId;

            _orderedTargets.Clear();
            foreach (var kvp in gsm.Players)
            {
                var p = kvp.Value;
                if (p == null) continue;
                if (!p.isAlive) continue;
                if (!string.IsNullOrEmpty(localId) && p.userId == localId) continue;
                if (watchOnlyPrisoners && p.role != "prisoner") continue;
                _orderedTargets.Add(kvp.Key);
            }
            // Stable, deterministic order across all spectators.
            _orderedTargets.Sort(System.StringComparer.Ordinal);

            if (_orderedTargets.Count == 0)
            {
                if (CurrentTargetId != null)
                {
                    CurrentTargetId = null;
                    CurrentTargetDisplay = null;
                    OnTargetChanged?.Invoke(null);
                }
                _currentIndex = -1;
                return;
            }

            // If the current target is gone (caught/left), or we never had one, pick a sane default.
            int idx = string.IsNullOrEmpty(CurrentTargetId)
                ? -1
                : _orderedTargets.IndexOf(CurrentTargetId);

            if (idx < 0)
            {
                // Keep the cursor near where it was so cycling feels stable.
                idx = Mathf.Clamp(_currentIndex, 0, _orderedTargets.Count - 1);
                if (idx < 0) idx = 0;
                SetTarget(idx);
            }
            else
            {
                _currentIndex = idx;
                if (forceTargetChange) OnTargetChanged?.Invoke(CurrentTargetId);
            }
        }

        private void Cycle(int dir)
        {
            if (_orderedTargets.Count == 0) return;
            int next = (_currentIndex + dir + _orderedTargets.Count) % _orderedTargets.Count;
            SetTarget(next);
        }

        private void SetTarget(int index)
        {
            _currentIndex = index;
            CurrentTargetId = _orderedTargets[index];
            CurrentTargetDisplay = ShortDisplay(CurrentTargetId);
            OnTargetChanged?.Invoke(CurrentTargetId);
            Debug.Log($"[Spectator] Now spectating {CurrentTargetId} ({_currentIndex + 1}/{_orderedTargets.Count})");
        }

        // ─── Camera follow ──────────────────────────────────────────────────
        private void FollowTarget()
        {
            if (_camera == null || string.IsNullOrEmpty(CurrentTargetId)) return;
            var gsm = GameStateManager.Instance;
            if (gsm == null) return;

            if (!gsm.RemotePlayerGameObjects.TryGetValue(CurrentTargetId, out var targetGo) || targetGo == null)
                return;

            var head = FindHead(targetGo.transform);
            if (head != null)
            {
                // First-person from the target's head bone.
                _camera.transform.position = head.position + targetGo.transform.forward * forwardOffset;
                _camera.transform.rotation = head.rotation;
            }
            else
            {
                // Fallback: ride above the root, looking where the body faces.
                _camera.transform.position = targetGo.transform.position + Vector3.up * fallbackEyeHeight
                                             + targetGo.transform.forward * forwardOffset;
                _camera.transform.rotation = targetGo.transform.rotation;
            }
        }

        private static readonly string[] HeadBoneNames =
        {
            "Head", "head", "mixamorig:Head", "mixamorig1:Head", "Bip01 Head", "Bip001 Head"
        };

        private static Transform FindHead(Transform root)
        {
            // Fast path — direct named children (most rigs).
            foreach (var n in HeadBoneNames)
            {
                var t = root.Find(n);
                if (t != null) return t;
            }
            // Slow path — search the whole rig once.
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                var lname = t.name.ToLowerInvariant();
                if (lname == "head" || lname.EndsWith(":head") || lname.EndsWith("_head"))
                    return t;
            }
            return null;
        }

        // ─── HUD ────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (!IsSpectating) return;

            const int padding = 16;
            int barH = 56;
            var bgRect = new Rect(0, Screen.height - barH, Screen.width, barH);

            var prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(bgRect, Texture2D.whiteTexture);
            GUI.color = prevColor;

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                normal = { textColor = Color.white },
            };

            string body;
            if (_orderedTargets.Count == 0)
            {
                body = "CAPTURED — no other prisoners to spectate";
            }
            else
            {
                int n = _orderedTargets.Count;
                int idx1 = _currentIndex + 1;
                body = $"SPECTATING  {CurrentTargetDisplay}   ({idx1}/{n})    [ ← ]   prev    /    next   [ → ]";
            }

            GUI.Label(new Rect(padding, Screen.height - barH, Screen.width - 2 * padding, barH), body, style);
        }

        private static string ShortDisplay(string id)
        {
            if (string.IsNullOrEmpty(id)) return "—";
            // userIds are persistent UUIDs; show a 6-char tail for readability.
            return id.Length <= 6 ? id : "…" + id.Substring(id.Length - 6);
        }
    }
}
