using System.Collections.Generic;
using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.NPC
{
    /// <summary>
    /// Manages a pool of NPC GameObjects.
    /// Attach to an empty "NPCPool" GameObject in the scene.
    ///
    /// Replace the default spawn logic with proper prefabs later.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class NPCNetworkSync : MonoBehaviour
    {
        [Header("NPC Prefab (optional — defaults to colored capsule)")]
        [SerializeField] private GameObject npcPrefab;

        private readonly Dictionary<string, Transform> _npcs = new();
        private readonly Dictionary<string, Vector3> _npcTargets = new();

        private const float NpcLerpSpeed = 5f; // smooth over ~200ms at 60fps

        // ─── Unity Lifecycle ─────────────────────────────────────────────────

        private void Start()
        {
            var net = NetworkManager.Instance;
            if (net == null)
            {
                Debug.LogError("[NPC] NetworkManager not found");
                return;
            }

            net.OnGameStartEvent     += HandleGameStart;
            net.OnNPCPositionsEvent  += HandleNPCPositions;
            net.OnGameReconnectEvent += HandleGameReconnect;

            // Emergent behavior & mood events
            // net.OnNPCEmergentEvent   += HandleNPCEmergent; // Removed
            net.OnNPCMoodShiftEvent  += HandleNPCMoodShift;

            // If game:start already fired before this scene loaded, spawn NPCs now
            if (net.State == ConnectionState.InGame)
            {
                if (net.CachedGameStart?.npcs != null)
                {
                    Debug.Log("[NPC] Processing cached game:start NPCs");
                    HandleGameStart(net.CachedGameStart);
                }
                else if (net.CachedGameReconnect?.npcs != null)
                {
                    Debug.Log("[NPC] Processing cached game:reconnect NPCs");
                    HandleGameReconnect(net.CachedGameReconnect);
                }
            }

            Debug.Log("[NPC] Initialized");
        }

        private void OnDestroy()
        {
            var net = NetworkManager.Instance;
            if (net == null) return;

            net.OnGameStartEvent     -= HandleGameStart;
            net.OnNPCPositionsEvent  -= HandleNPCPositions;
            net.OnGameReconnectEvent -= HandleGameReconnect;
            // net.OnNPCEmergentEvent   -= HandleNPCEmergent; // Removed
            net.OnNPCMoodShiftEvent  -= HandleNPCMoodShift;
        }

        private float _syncTimer;

        private void Update()
        {
            var net = NetworkManager.Instance;
            bool isHost = net != null && net.IsHost && net.State == ConnectionState.InGame;

            if (isHost)
            {
                _syncTimer -= Time.deltaTime;
                if (_syncTimer <= 0f)
                {
                    _syncTimer = 1.0f; // sync every 1 second
                    SendNPCSyncs(net);
                }
            }

            // Lerp all NPC transforms toward their server targets.
            // Skip any NPC that is locally driven — NavMesh / SitInteraction
            // are the source of truth once the NPC has a behavior assigned.
            foreach (var (id, t) in _npcs)
            {
                if (t == null || !_npcTargets.TryGetValue(id, out var target)) continue;

                var behavior = t.GetComponent<NPCBehaviorController>();
                if (behavior != null && (behavior.IsBehaviorDriven || behavior.IsNavigating || behavior.IsSitting))
                    continue;

                t.position = Vector3.Lerp(t.position, target, NpcLerpSpeed * Time.deltaTime);
            }
        }

        private void SendNPCSyncs(NetworkManager net)
        {
            var payload = new NPCSyncStatePayload();
            var syncList = new List<NPCStateSync>();

            foreach (var (id, t) in _npcs)
            {
                if (t == null) continue;
                var behavior = t.GetComponent<NPCBehaviorController>();
                if (behavior != null && behavior.IsBehaviorDriven)
                {
                    syncList.Add(new NPCStateSync
                    {
                        npcId = id,
                        position = SVector3.FromUnity(t.position),
                        rotation = SQuaternion.FromUnity(t.rotation),
                        currentSequenceIndex = behavior.CurrentSequenceIndex,
                        currentActionId = behavior.CurrentActionId ?? ""
                    });
                }
            }

            if (syncList.Count > 0)
            {
                payload.npcs = syncList.ToArray();
                net.SendNPCSyncState(payload);
            }
        }

        // ─── Event Handlers ──────────────────────────────────────────────────

        // Spawn all NPCs immediately from game:start payload so they appear
        // before the first npc:positions tick (200ms later).
        private void HandleGameStart(GameStartPayload data)
        {
            if (data.npcs == null) return;
            DespawnAll();
            foreach (var npc in data.npcs)
                EnsureNPC(npc);
            Debug.Log($"[NPC] Spawned {data.npcs.Length} NPCs from game:start");
        }

        private void HandleGameReconnect(GameReconnectPayload data)
        {
            if (data.npcs == null) return;
            DespawnAll();
            foreach (var npc in data.npcs)
                EnsureNPC(npc);
            Debug.Log($"[NPC] Reconnected with {data.npcs.Length} NPCs");
        }

        private void HandleNPCPositions(NPCPositionUpdate data)
        {
            if (data.npcs == null) return;
            foreach (var npc in data.npcs)
            {
                EnsureNPC(npc);
                _npcTargets[npc.id] = npc.position.ToUnity();
            }
        }

        // ─── Emergent Behavior & Mood Handlers ─────────────────────────────

        private void HandleNPCMoodShift(NPCMoodShiftData data)
        {
            if (!_npcs.TryGetValue(data.npcId, out var npcTransform)) return;

            var behavior = npcTransform.GetComponent<NPCBehaviorController>();
            if (behavior != null)
            {
                behavior.ApplyMoodHint(data.animHint);
                // Debug.Log($"[NPC] Mood shift: {data.npcId} → {data.newMood} (hint={data.animHint})");
            }
        }

        // ─── NPC Pool Helpers ────────────────────────────────────────────────

        private void EnsureNPC(NPCStateData data)
        {
            if (_npcs.ContainsKey(data.id)) return;

            GameObject go;

            if (npcPrefab != null)
            {
                go = Instantiate(npcPrefab, data.position.ToUnity(), data.rotation.ToUnity(), transform);
            }
            else
            {
                // Default placeholder: capsule, color-coded by type
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.transform.SetParent(transform);
                go.transform.position = data.position.ToUnity();
                go.transform.rotation = data.rotation.ToUnity();
                go.transform.localScale = new Vector3(0.5f, 1f, 0.5f);

                // Remove collider — use Destroy (not DestroyImmediate) in builds
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);

                // All NPCs are prisoners — same blue color.
                // Use MaterialPropertyBlock to override color WITHOUT creating a new
                // material instance — avoids URP shader breakage in WebGL (pink capsules).
                var renderer = go.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var color = new Color(0.25f, 0.55f, 0.85f, 1f); // prisoner blue
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetColor("_BaseColor", color); // URP Lit
                    mpb.SetColor("_Color",     color); // Built-in RP fallback
                    renderer.SetPropertyBlock(mpb);
                }
            }

            if (UnityEngine.AI.NavMesh.SamplePosition(go.transform.position, out var hit, 2.0f, UnityEngine.AI.NavMesh.AllAreas))
            {
                go.transform.position = hit.position;
            }

            go.name = $"NPC_{data.id}_{data.type}";

            var identity = go.GetComponent<NPCIdentity>();
            if (identity == null) identity = go.AddComponent<NPCIdentity>();
            identity.NpcId = data.id;
            identity.NpcType = data.type;

            _npcs[data.id] = go.transform;
            _npcTargets[data.id] = data.position.ToUnity();

            Debug.Log($"[NPC] Spawned {data.type}: {data.id}");
        }

        private void DespawnAll()
        {
            foreach (var t in _npcs.Values)
            {
                if (t != null) Destroy(t.gameObject);
            }

            _npcs.Clear();
            _npcTargets.Clear();
            Debug.Log("[NPC] Despawned all NPCs");
        }

        public GameObject GetNPC(string npcId)
        {
            if (_npcs.TryGetValue(npcId, out var t) && t != null)
                return t.gameObject;
            return null;
        }
    }
}
