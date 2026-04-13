using System.Collections.Generic;
using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.NPC
{
    /// <summary>
    /// Manages a pool of NPC GameObjects.
    /// Attach to an empty "NPCPool" GameObject in the scene.
    ///
    /// NPCs are spawned as placeholder capsules (red=guard, blue=helper).
    /// Replace the default spawn logic with proper prefabs later.
    /// </summary>
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
        }

        private void Update()
        {
            // Lerp all NPC transforms toward their server targets
            foreach (var (id, t) in _npcs)
            {
                if (t == null || !_npcTargets.TryGetValue(id, out var target)) continue;

                t.position = Vector3.Lerp(t.position, target, NpcLerpSpeed * Time.deltaTime);
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

            go.name = $"NPC_{data.id}_{data.type}";

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
    }
}
