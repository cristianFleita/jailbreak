using System;
using System.Collections;
using System.Collections.Generic;
using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Interactions.Pickable
{
    /// <summary>
    /// Phase C bridge between scene-authored spawn areas and backend authority.
    ///
    /// Responsibilities:
    ///  1. Discover every <see cref="RouteSpawnArea"/> in the loaded GameScene.
    ///  2. If the local client is the host, send the list once through
    ///     <see cref="NetworkManager.SendRegisterSpawnAreas"/>.
    ///  3. React to authoritative <c>item:state</c> broadcasts: instantiate
    ///     (or reposition / hide) the matching prefab for each route item.
    ///
    /// The instantiated prefab owns its own lifecycle via
    /// <see cref="NetworkRoutePickable"/> — the registry only makes sure the
    /// prefab exists, sits at the server-chosen world position, and is enabled
    /// when the item is in the world.
    ///
    /// Plan: design/gdd/ruta-1-implementation-plan.md (Phase C-01 + C-03)
    /// </summary>
    [DisallowMultipleComponent]
    public class RouteItemRegistry : MonoBehaviour
    {
        [Serializable]
        public class ItemPrefabEntry
        {
            [Tooltip("Stable backend item id (e.g. route1_cutters, route1_wrench).")]
            public string itemId;

            [Tooltip("Prefab to instantiate when backend places this item in a spawn area. Must carry NetworkRoutePickable.")]
            public GameObject prefab;
        }

        [Header("Item prefabs")]
        [Tooltip("Mapping of itemId -> networked pickable prefab. Must include every route-critical item expected in this scene.")]
        public ItemPrefabEntry[] itemPrefabs;

        [Header("Spawn area discovery")]
        [Tooltip("How long to wait after GameStateManager confirms activeRouteId before collecting spawn areas. Zero = next frame.")]
        public float scanDelaySeconds = 0.25f;

        [Tooltip("If true, keep listening for new spawn areas added after the initial scan. Leave off unless scenes spawn areas dynamically.")]
        public bool continuousRescan = false;

        [Header("Debug")]
        public bool debugLogs = true;

        private readonly Dictionary<string, GameObject> _prefabByItemId = new();
        private readonly Dictionary<string, GameObject> _runtimeInstances = new();

        private bool _spawnAreasSent;
        private bool _subscribed;
        private Coroutine _scanCoroutine;

        private void Awake()
        {
            BuildPrefabMap();
        }

        private void OnEnable()
        {
            SubscribeIfNeeded();
        }

        private void Start()
        {
            SubscribeIfNeeded();

            // If the active route already landed while this component was
            // loading, kick off the scan now. Otherwise we wait for the
            // onActiveRouteChanged event.
            var gsm = GameStateManager.Instance;
            if (gsm != null && !string.IsNullOrEmpty(gsm.ActiveRouteId))
                ScheduleSpawnAreaScan();

            HydrateFromCache();
        }

        private void OnDisable()
        {
            UnsubscribeNetwork();
            UnsubscribeGameState();

            if (_scanCoroutine != null)
            {
                StopCoroutine(_scanCoroutine);
                _scanCoroutine = null;
            }
        }

        // ─── Setup ───────────────────────────────────────────────────────

        private void BuildPrefabMap()
        {
            _prefabByItemId.Clear();
            if (itemPrefabs == null) return;

            foreach (var entry in itemPrefabs)
            {
                if (entry == null || string.IsNullOrEmpty(entry.itemId) || entry.prefab == null)
                    continue;
                _prefabByItemId[entry.itemId] = entry.prefab;
            }
        }

        private void SubscribeIfNeeded()
        {
            if (_subscribed) return;

            var net = NetworkManager.Instance;
            var gsm = GameStateManager.Instance;
            if (net == null || gsm == null) return;

            net.OnItemStateEvent += HandleItemState;
            gsm.onActiveRouteChanged.AddListener(HandleActiveRouteChanged);
            _subscribed = true;
        }

        private void UnsubscribeNetwork()
        {
            var net = NetworkManager.Instance;
            if (net != null) net.OnItemStateEvent -= HandleItemState;
        }

        private void UnsubscribeGameState()
        {
            var gsm = GameStateManager.Instance;
            if (gsm != null) gsm.onActiveRouteChanged.RemoveListener(HandleActiveRouteChanged);
            _subscribed = false;
        }

        // ─── Active route → spawn area scan ──────────────────────────────

        private void HandleActiveRouteChanged(string routeId)
        {
            if (string.IsNullOrEmpty(routeId)) return;
            ScheduleSpawnAreaScan();
        }

        private void ScheduleSpawnAreaScan()
        {
            if (_spawnAreasSent && !continuousRescan) return;
            if (_scanCoroutine != null) return;
            _scanCoroutine = StartCoroutine(DelayedSpawnAreaScan());
        }

        private IEnumerator DelayedSpawnAreaScan()
        {
            if (scanDelaySeconds > 0f)
                yield return new WaitForSeconds(scanDelaySeconds);
            else
                yield return null;

            _scanCoroutine = null;
            ScanAndRegisterSpawnAreas();
        }

        private void ScanAndRegisterSpawnAreas()
        {
            var net = NetworkManager.Instance;
            if (net == null)
            {
                if (debugLogs) Debug.LogWarning("[RouteItemRegistry] NetworkManager unavailable — skipping spawn-area registration");
                return;
            }
            if (!net.IsHost)
            {
                if (debugLogs) Debug.Log("[RouteItemRegistry] Local client is not host — not sending spawn areas");
                return;
            }

#if UNITY_2023_1_OR_NEWER
            var areas = UnityEngine.Object.FindObjectsByType<RouteSpawnArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            var areas = UnityEngine.Object.FindObjectsOfType<RouteSpawnArea>();
#endif

            var valid = new List<RouteSpawnAreaData>();
            foreach (var area in areas)
            {
                if (area == null) continue;
                if (!area.IsValid())
                {
                    if (debugLogs) Debug.LogWarning($"[RouteItemRegistry] Skipping invalid RouteSpawnArea on {area.gameObject.name}", area);
                    continue;
                }

                valid.Add(new RouteSpawnAreaData
                {
                    spawnAreaId    = area.spawnAreaId,
                    zoneId         = area.zoneId,
                    position       = SVector3.FromUnity(area.GetWorldPosition()),
                    allowedItemIds = (string[])area.allowedItemIds.Clone(),
                });
            }

            if (valid.Count == 0)
            {
                if (debugLogs) Debug.LogWarning("[RouteItemRegistry] No RouteSpawnArea components found in scene — backend cannot place route items");
                return;
            }

            var payload = new RegisterSpawnAreasPayload { areas = valid.ToArray() };
            if (debugLogs)
                Debug.Log($"[RouteItemRegistry] Registering {valid.Count} spawn areas with backend (host)");
            net.SendRegisterSpawnAreas(payload);
            _spawnAreasSent = true;
        }

        // ─── Hydration from cached item:state payloads ───────────────────

        private void HydrateFromCache()
        {
            var net = NetworkManager.Instance;
            if (net == null) return;

            foreach (var kv in net.CachedItemStates)
                HandleItemState(kv.Value);
        }

        // ─── item:state handler ──────────────────────────────────────────

        /// <summary>
        /// Instantiates the route prefab once the backend confirms the item
        /// has a real placement. Positioning and visibility afterwards is
        /// delegated to the prefab's own <see cref="NetworkRoutePickable"/>,
        /// which reads the same item:state payload and handles held/stored/
        /// dropped transitions (including teleport-to-spawn-area).
        /// </summary>
        private void HandleItemState(ItemStateBroadcastPayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.itemId)) return;
            if (!_prefabByItemId.TryGetValue(payload.itemId, out var prefab)) return;

            // Only instantiate once the backend has actually assigned a
            // spawn area (or the item is held/stored/dropped — in all of
            // those cases something visible should exist on the client).
            // Early placeholder (state=spawned, spawnAreaId=null, pos=0) is
            // ignored; we'll instantiate on the next broadcast.
            if (!ShouldInstantiate(payload)) return;

            EnsureInstance(prefab, payload);
        }

        private static GameObject FindSceneInstance(string itemId)
        {
#if UNITY_2023_1_OR_NEWER
            var all = UnityEngine.Object.FindObjectsByType<NetworkRoutePickable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var all = UnityEngine.Object.FindObjectsOfType<NetworkRoutePickable>(true);
#endif
            foreach (var rp in all)
            {
                if (rp == null) continue;
                if (rp.itemId == itemId) return rp.gameObject;
            }
            return null;
        }

        private static bool ShouldInstantiate(ItemStateBroadcastPayload payload)
        {
            switch (payload.state)
            {
                case "held":
                case "stored":
                case "dropped":
                case "respawning":
                    return true;
                case "spawned":
                    if (!string.IsNullOrEmpty(payload.spawnAreaId)) return true;
                    var p = payload.position;
                    return Mathf.Abs(p.x) + Mathf.Abs(p.y) + Mathf.Abs(p.z) > 0.001f;
                default:
                    return false;
            }
        }

        private GameObject EnsureInstance(GameObject prefab, ItemStateBroadcastPayload payload)
        {
            if (_runtimeInstances.TryGetValue(payload.itemId, out var existing) && existing != null)
                return existing;

            // If someone pre-placed the prefab in the scene with the matching
            // itemId, adopt it instead of creating a duplicate. Handy during
            // hot-reload or when the scene has legacy authored props.
            var preplaced = FindSceneInstance(payload.itemId);
            if (preplaced != null)
            {
                _runtimeInstances[payload.itemId] = preplaced;
                if (debugLogs)
                    Debug.Log($"[RouteItemRegistry] Adopted pre-placed instance for {payload.itemId}", preplaced);
                return preplaced;
            }

            var spawnPos = ShouldInstantiate(payload) ? payload.position.ToUnity() : Vector3.zero;
            var go = Instantiate(prefab, spawnPos, Quaternion.identity);
            go.name = $"{payload.itemId}_runtime";

            var route = go.GetComponent<NetworkRoutePickable>();
            if (route != null)
            {
                route.itemId = payload.itemId;
                var ni = go.GetComponent<NetworkInteractable>();
                if (ni != null) ni.networkId = payload.itemId;
            }
            else if (debugLogs)
            {
                Debug.LogWarning($"[RouteItemRegistry] Prefab for {payload.itemId} has no NetworkRoutePickable — lifecycle will be local-only", go);
            }

            _runtimeInstances[payload.itemId] = go;
            if (debugLogs)
                Debug.Log($"[RouteItemRegistry] Instantiated {payload.itemId} at {spawnPos} (state={payload.state})", go);
            return go;
        }
    }
}
