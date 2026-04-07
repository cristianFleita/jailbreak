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

            net.OnNPCPositionsEvent += HandleNPCPositions;
            net.OnGameReconnectEvent += HandleGameReconnect;
            net.OnPlayerJoinedEvent += HandlePlayerJoined;

            Debug.Log("[NPC] Initialized");
        }

        private void OnDestroy()
        {
            var net = NetworkManager.Instance;
            if (net == null) return;

            net.OnNPCPositionsEvent -= HandleNPCPositions;
            net.OnGameReconnectEvent -= HandleGameReconnect;
            net.OnPlayerJoinedEvent -= HandlePlayerJoined;
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

        private void HandleGameReconnect(GameReconnectPayload data)
        {
            if (data.npcs == null) return;

            DespawnAll();

            foreach (var npc in data.npcs)
            {
                EnsureNPC(npc);
            }

            Debug.Log($"[NPC] Reconnected with {data.npcs.Length} NPCs");
        }

        private void HandlePlayerJoined(PlayerJoinedPayload data)
        {
            // NPC positions come in the first npc:positions after game starts
            // No action needed here — HandleNPCPositions will spawn them
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

                // Remove collider (optional)
                var col = go.GetComponent<Collider>();
                if (col != null) DestroyImmediate(col);

                // Color code by type
                var renderer = go.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var mat = renderer.material;
                    mat.color = data.type == "guard"
                        ? new Color(0.8f, 0.2f, 0.2f, 1f)  // Red for guards
                        : new Color(0.2f, 0.5f, 0.8f, 1f); // Blue for helpers
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
