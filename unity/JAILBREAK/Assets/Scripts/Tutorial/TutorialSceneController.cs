using System.Collections;
using System.Collections.Generic;
using Jailbreak.Network;
using Jailbreak.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Jailbreak.Tutorial
{
    public class TutorialSceneController : MonoBehaviour
    {
        [Header("Scene Flow")]
        [SerializeField] private string gameSceneName = "GameScene";
        [SerializeField] private bool allowManualPreview = true;
        [SerializeField] private string previewRole = "prisoner";

        [Header("Player Prefabs")]
        [SerializeField] private GameObject prisonerPrefab;
        [SerializeField] private GameObject guardPrefab;
        [SerializeField] private GameObject remotePrisonerPrefab;
        [SerializeField] private GameObject remoteGuardPrefab;
        [SerializeField] private bool spawnRemotePlayers = true;

        [Header("Spawn Points")]
        [SerializeField] private Transform[] prisonerSpawns;
        [SerializeField] private Transform guardSpawn;
        [SerializeField] private Vector3 fallbackPrisonerSpawn = new Vector3(-3f, 1.5f, 0f);
        [SerializeField] private Vector3 fallbackGuardSpawn = new Vector3(3f, 1.5f, 0f);

        [Header("Tutorial UI")]
        [SerializeField] private UIDocument prisonerDocument;
        [SerializeField] private UIDocument guardDocument;

        [Header("Guard Practice")]
        [SerializeField] private LayerMask guardCaptureTargetLayer;
        [SerializeField] private float captureRange = 2.0f;
        [SerializeField] private float captureFocusTime = 0.5f;

        private GameObject _localPlayer;
        private readonly Dictionary<string, GameObject> _remotePlayers = new();
        private bool _spawned;
        private bool _loadingNextScene;

        private void OnEnable()
        {
            var net = NetworkManager.Instance;
            if (net != null)
            {
                net.OnTutorialStartEvent += HandleTutorialStart;
                net.OnTutorialEndEvent += HandleTutorialEnd;
                net.OnPlayerStateEvent += HandlePlayerState;

                if (net.CachedTutorialStart != null)
                    HandleTutorialStart(net.CachedTutorialStart);
                if (net.CachedTutorialEnd != null)
                    HandleTutorialEnd(net.CachedTutorialEnd);
            }
            else if (allowManualPreview)
            {
                HandleTutorialStart(new TutorialStartPayload
                {
                    duration = 60f,
                    role = previewRole,
                    seed = 1,
                    missions = null
                });
            }
        }

        private void Start()
        {
            if (!_spawned && allowManualPreview)
            {
                var cached = NetworkManager.Instance != null ? NetworkManager.Instance.CachedTutorialStart : null;
                HandleTutorialStart(cached ?? new TutorialStartPayload
                {
                    duration = 60f,
                    role = previewRole,
                    seed = 1,
                    missions = null
                });
            }
        }

        private void OnDisable()
        {
            var net = NetworkManager.Instance;
            if (net != null)
            {
                net.OnTutorialStartEvent -= HandleTutorialStart;
                net.OnTutorialEndEvent -= HandleTutorialEnd;
                net.OnPlayerStateEvent -= HandlePlayerState;
            }
        }

        private void HandleTutorialStart(TutorialStartPayload payload)
        {
            if (payload == null) return;

            var role = string.IsNullOrEmpty(payload.role) ? "prisoner" : payload.role;
            ActivateRoleUi(role, payload);
            SpawnLocalPlayer(role, payload);
            SpawnInitialRemotePlayers(payload);
            ApplyTutorialNpcAssignments(payload);
        }

        private void ApplyTutorialNpcAssignments(TutorialStartPayload payload)
        {
            if (payload?.npcAssignments == null || payload.npcAssignments.Length == 0) return;

#if UNITY_2023_1_OR_NEWER
            var controllers = FindObjectsByType<TutorialNpcRoutineController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            var controllers = FindObjectsOfType<TutorialNpcRoutineController>();
#endif
            if (controllers == null || controllers.Length == 0)
            {
                Debug.LogWarning("[TutorialScene] Got npcAssignments but no TutorialNpcRoutineController is active in the scene.");
                return;
            }

            foreach (var controller in controllers)
                controller.ApplyAssignments(payload.npcAssignments);
        }

        private void HandleTutorialEnd(TutorialEndPayload payload)
        {
            if (_loadingNextScene) return;
            _loadingNextScene = true;

            var nextScene = !string.IsNullOrEmpty(payload?.nextScene)
                ? payload.nextScene
                : gameSceneName;
            StartCoroutine(LoadNextScene(nextScene));
        }

        private IEnumerator LoadNextScene(string sceneName)
        {
            TutorialMissionEvents.Toast("The real day is starting...", 1.2f);
            yield return new WaitForSecondsRealtime(0.25f);
            SceneManager.LoadScene(sceneName);
        }

        private void ActivateRoleUi(string role, TutorialStartPayload payload)
        {
            var isGuard = string.Equals(role, "guard", System.StringComparison.OrdinalIgnoreCase);

            if (prisonerDocument != null)
                prisonerDocument.gameObject.SetActive(!isGuard);
            if (guardDocument != null)
                guardDocument.gameObject.SetActive(isGuard);

            var controller = ResolveActiveMissionController();
            if (controller != null)
                controller.ApplyStart(payload);
        }

        private TutorialMissionController ResolveActiveMissionController()
        {
            if (TutorialMissionController.Active != null)
                return TutorialMissionController.Active;

#if UNITY_2023_1_OR_NEWER
            var controllers = FindObjectsByType<TutorialMissionController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var controllers = Resources.FindObjectsOfTypeAll<TutorialMissionController>();
#endif
            foreach (var controller in controllers)
            {
                if (controller != null && controller.gameObject.activeInHierarchy)
                    return controller;
            }
            return null;
        }

        private void SpawnLocalPlayer(string role, TutorialStartPayload payload)
        {
            if (_spawned) return;

            var isGuard = string.Equals(role, "guard", System.StringComparison.OrdinalIgnoreCase);
            var prefab = isGuard ? guardPrefab : prisonerPrefab;
            if (prefab == null)
            {
                Debug.LogError($"[TutorialScene] Missing local prefab for role '{role}'.");
                return;
            }

            var spawn = ResolveLocalSpawn(role, payload);
            _localPlayer = Instantiate(prefab, spawn.position, spawn.rotation);
            _localPlayer.name = $"TutorialLocalPlayer_{role}";
            _spawned = true;

            CorrectCharacterController(_localPlayer);
            InitializeRealMovementNetworking(_localPlayer, spawn);
            EnsureTutorialComponents(_localPlayer, isGuard);
        }

        private Pose ResolveGuardSpawn()
        {
            var t = guardSpawn != null ? guardSpawn : GameObject.Find("tutorial_guard_spawn_01")?.transform;
            return t != null
                ? new Pose(t.position, t.rotation)
                : new Pose(fallbackGuardSpawn, Quaternion.identity);
        }

        private Pose ResolveLocalSpawn(string role, TutorialStartPayload payload)
        {
            if (string.Equals(role, "guard", System.StringComparison.OrdinalIgnoreCase))
                return ResolveGuardSpawn();

            var localId = NetworkManager.Instance != null ? NetworkManager.Instance.LocalPlayerId : null;
            var roster = payload != null ? payload.players : null;
            if (!string.IsNullOrEmpty(localId) && roster != null)
            {
                var index = 0;
                foreach (var player in roster)
                {
                    if (player == null || string.Equals(player.role, "guard", System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (player.userId == localId)
                        return ResolvePrisonerSpawn(index);

                    index++;
                }
            }

            return ResolvePrisonerSpawn(payload != null ? payload.seed : 0);
        }

        private Pose ResolveSpawnForPlayer(PlayerStateData player, PlayerStateData[] roster, int seed)
        {
            if (player == null) return new Pose(fallbackPrisonerSpawn, Quaternion.identity);
            if (string.Equals(player.role, "guard", System.StringComparison.OrdinalIgnoreCase))
                return ResolveGuardSpawn();

            if (roster != null)
            {
                var index = 0;
                foreach (var candidate in roster)
                {
                    if (candidate == null || string.Equals(candidate.role, "guard", System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    if ((!string.IsNullOrEmpty(player.userId) && candidate.userId == player.userId)
                        || (!string.IsNullOrEmpty(player.id) && candidate.id == player.id))
                        return ResolvePrisonerSpawn(index);

                    index++;
                }
            }

            return ResolvePrisonerSpawn(seed);
        }

        private Pose ResolvePrisonerSpawn(int index)
        {
            if (prisonerSpawns != null && prisonerSpawns.Length > 0)
            {
                var idx = Mathf.Abs(index) % prisonerSpawns.Length;
                if (prisonerSpawns[idx] != null)
                    return new Pose(prisonerSpawns[idx].position, prisonerSpawns[idx].rotation);
            }

            var preferred = (Mathf.Abs(index) % 3) + 1;
            var preferredTransform = GameObject.Find($"tutorial_prisoner_spawn_{preferred:00}")?.transform;
            if (preferredTransform != null)
                return new Pose(preferredTransform.position, preferredTransform.rotation);

            for (int i = 1; i <= 3; i++)
            {
                var t = GameObject.Find($"tutorial_prisoner_spawn_{i:00}")?.transform;
                if (t != null) return new Pose(t.position, t.rotation);
            }

            return new Pose(fallbackPrisonerSpawn, Quaternion.identity);
        }

        private static void CorrectCharacterController(GameObject player)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null && Mathf.Abs(cc.center.y) < 0.01f && cc.height > 0f)
                cc.center = new Vector3(cc.center.x, cc.height * 0.5f, cc.center.z);
        }

        private static void InitializeRealMovementNetworking(GameObject player, Pose spawn)
        {
            var pns = player.GetComponent<PlayerNetworkSync>();
            var cc = player.GetComponent<CharacterController>();

            player.transform.rotation = spawn.rotation;

            if (pns != null)
            {
                pns.enabled = true;
                pns.TeleportToSpawn(spawn.position);
                if (cc != null) cc.enabled = true;
                return;
            }

            if (cc != null) cc.enabled = true;
            var input = player.GetComponent<PlayerInputController>();
            if (input != null) input.InputEnabled = true;
        }

        private void EnsureTutorialComponents(GameObject player, bool isGuard)
        {
            if (player.GetComponent<TutorialMissionTracker>() == null)
                player.AddComponent<TutorialMissionTracker>();

            if (!isGuard)
            {
                if (player.GetComponent<ItemInventory>() != null
                    && player.GetComponent<TutorialInventoryDropInput>() == null)
                    player.AddComponent<TutorialInventoryDropInput>();

                // Wire inventory HUD directly so it doesn't rely on InteractionManager search
                var hud = FindFirstActiveHud();
                if (hud != null)
                    hud.BindPlayer(player);
                return;
            }

            // Guard: add capture controller and wire camera directly
            var capture = player.GetComponent<TutorialGuardCaptureController>();
            if (capture == null) capture = player.AddComponent<TutorialGuardCaptureController>();

            // Include inactive = true so we get the camera even if it starts disabled
            var cam = player.GetComponentInChildren<Camera>(true);
            capture.Configure(cam, guardCaptureTargetLayer, captureRange, captureFocusTime);
        }

        private TutorialInventoryHud FindFirstActiveHud()
        {
#if UNITY_2023_1_OR_NEWER
            var huds = FindObjectsByType<TutorialInventoryHud>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var huds = Resources.FindObjectsOfTypeAll<TutorialInventoryHud>();
#endif
            foreach (var hud in huds)
                if (hud != null && hud.gameObject.activeInHierarchy) return hud;
            return null;
        }

        private void SpawnInitialRemotePlayers(TutorialStartPayload payload)
        {
            if (!spawnRemotePlayers || payload?.players == null) return;

            foreach (var player in payload.players)
                SpawnOrUpdateRemotePlayer(player, payload.players, payload.seed, false);
        }

        private void HandlePlayerState(PlayerStateUpdate data)
        {
            if (!spawnRemotePlayers || data?.players == null) return;

            foreach (var player in data.players)
                SpawnOrUpdateRemotePlayer(player, null, 0, true);
        }

        private void SpawnOrUpdateRemotePlayer(PlayerStateData player, PlayerStateData[] roster, int seed, bool pushNetworkState)
        {
            if (player == null) return;

            var localId = NetworkManager.Instance != null ? NetworkManager.Instance.LocalPlayerId : null;
            if (!string.IsNullOrEmpty(localId) && player.userId == localId) return;
            if (string.IsNullOrEmpty(player.id)) return;

            if (!_remotePlayers.TryGetValue(player.id, out var go) || go == null)
            {
                var pose = roster != null
                    ? ResolveSpawnForPlayer(player, roster, seed)
                    : new Pose(player.position.ToUnity(), player.rotation.ToUnity());

                var prefab = ResolveRemotePrefab(player.role);
                go = prefab != null
                    ? Instantiate(prefab, pose.position, pose.rotation)
                    : CreateFallbackRemoteAvatar(player, pose);
                go.name = $"TutorialRemotePlayer_{player.id}_{player.role}";
                PrepareRemoteAvatar(go, player);
                _remotePlayers[player.id] = go;
            }

            if (!pushNetworkState) return;

            var sync = go.GetComponent<RemotePlayerSync>();
            if (sync != null) sync.PushState(player);
        }

        private GameObject ResolveRemotePrefab(string role)
        {
            var isGuard = string.Equals(role, "guard", System.StringComparison.OrdinalIgnoreCase);
            var explicitPrefab = isGuard ? remoteGuardPrefab : remotePrisonerPrefab;
            if (explicitPrefab != null) return explicitPrefab;
            return isGuard ? guardPrefab : prisonerPrefab;
        }

        private static GameObject CreateFallbackRemoteAvatar(PlayerStateData player, Pose pose)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            go.transform.localScale = new Vector3(0.6f, 1.2f, 0.6f);

            var color = string.Equals(player.role, "guard", System.StringComparison.OrdinalIgnoreCase)
                ? new Color(0.15f, 0.8f, 0.15f, 1f)
                : new Color(0.1f, 0.1f, 0.1f, 1f);

            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", color);
                mpb.SetColor("_Color", color);
                rend.SetPropertyBlock(mpb);
            }

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            return go;
        }

        private static void PrepareRemoteAvatar(GameObject go, PlayerStateData player)
        {
            foreach (var input in go.GetComponentsInChildren<PlayerInputController>(true))
                Destroy(input);
            foreach (var movement in go.GetComponentsInChildren<PlayerMovement>(true))
                Destroy(movement);
            foreach (var netSync in go.GetComponentsInChildren<PlayerNetworkSync>(true))
                Destroy(netSync);
            foreach (var cc in go.GetComponentsInChildren<CharacterController>(true))
                Destroy(cc);
            foreach (var fpsCam in go.GetComponentsInChildren<FPSCameraController>(true))
                Destroy(fpsCam);
            foreach (var im in go.GetComponentsInChildren<InteractionManager>(true))
                Destroy(im);
            foreach (var visual in go.GetComponentsInChildren<LocalPlayerRoleVisual>(true))
                Destroy(visual);
            foreach (var tracker in go.GetComponentsInChildren<TutorialMissionTracker>(true))
                Destroy(tracker);
            foreach (var drop in go.GetComponentsInChildren<TutorialInventoryDropInput>(true))
                Destroy(drop);
            foreach (var capture in go.GetComponentsInChildren<TutorialGuardCaptureController>(true))
                Destroy(capture);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
                Destroy(rb);
            foreach (var cam in go.GetComponentsInChildren<Camera>(true))
                Destroy(cam.gameObject);
            foreach (var listener in go.GetComponentsInChildren<AudioListener>(true))
                Destroy(listener);

            var sync = go.GetComponent<RemotePlayerSync>();
            if (sync == null) sync = go.AddComponent<RemotePlayerSync>();
            sync.PlayerId = player.id;
            sync.Role = player.role;

            var remoteInteract = go.GetComponent<RemoteInteractionHandler>();
            if (remoteInteract == null) remoteInteract = go.AddComponent<RemoteInteractionHandler>();
            remoteInteract.PlayerId = player.id;
        }
    }
}
