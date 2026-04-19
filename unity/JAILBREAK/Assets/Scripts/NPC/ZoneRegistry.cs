using System.Collections.Generic;
using UnityEngine;

namespace Jailbreak.NPC
{
    /// <summary>
    /// Replaces the old WaypointRegistry.
    /// Maps zone ID strings (e.g., "cafeteria", "yard_benches") to physical BoxCollider bounds.
    /// Provides deterministic random point generation inside those bounds using a seed.
    /// </summary>
    public class ZoneRegistry : MonoBehaviour
    {
        [System.Serializable]
        public class ZoneEntry
        {
            public string zoneId;
            public BoxCollider bounds;
        }

        [SerializeField] private List<ZoneEntry> zones = new();

        private Dictionary<string, BoxCollider> _lookup;

        public void Init()
        {
            _lookup = new Dictionary<string, BoxCollider>(zones.Count);
            foreach (var entry in zones)
            {
                if (!string.IsNullOrEmpty(entry.zoneId) && entry.bounds != null)
                {
                    _lookup[entry.zoneId] = entry.bounds;
                }
            }
            Debug.Log($"[ZoneRegistry] Initialized {_lookup.Count} zones.");
        }

        private void Awake()
        {
            Init();
        }

        public BoxCollider GetZoneBounds(string zoneId)
        {
            if (_lookup == null) Init();
            if (!string.IsNullOrEmpty(zoneId) && _lookup.TryGetValue(zoneId, out var col))
            {
                return col;
            }
            return null;
        }

        public SitPoint GetDeterministicSitPoint(string zoneId, uint seed)
        {
            var col = GetZoneBounds(zoneId);
            if (col == null) return null;

            // Test XZ containment only. Zone BoxColliders are usually thin slabs
            // at floor level, while SitPoints live at seat height (~0.5m), so a
            // full 3D Contains() check would reject valid sit points.
            var b = col.bounds;
            var all = FindObjectsOfType<SitPoint>();
            var inZone = new List<SitPoint>();
            foreach (var sp in all)
            {
                var p = sp.transform.position;
                if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z)
                    inZone.Add(sp);
            }

            if (inZone.Count == 0) return null;

            inZone.Sort((a, b2) => {
                int cmp = a.transform.position.x.CompareTo(b2.transform.position.x);
                if (cmp == 0) cmp = a.transform.position.z.CompareTo(b2.transform.position.z);
                return cmp;
            });

            return inZone[(int)(seed % inZone.Count)];
        }

        /// <summary>
        /// Returns a deterministic FoodCounterInteractable inside the requested zone.
        /// Mirrors GetDeterministicSitPoint: XZ-bounds scan + stable sort + seed index,
        /// so every client (and the backend's seed) resolves to the same counter.
        /// </summary>
        public FoodCounterInteractable GetDeterministicFoodCounter(string zoneId, uint seed)
        {
            var col = GetZoneBounds(zoneId);
            if (col == null) return null;

            var b = col.bounds;
            var all = FindObjectsOfType<FoodCounterInteractable>();
            var inZone = new List<FoodCounterInteractable>();
            foreach (var fc in all)
            {
                var p = fc.transform.position;
                if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z)
                    inZone.Add(fc);
            }

            if (inZone.Count == 0) return null;

            inZone.Sort((a, b2) => {
                int cmp = a.transform.position.x.CompareTo(b2.transform.position.x);
                if (cmp == 0) cmp = a.transform.position.z.CompareTo(b2.transform.position.z);
                return cmp;
            });

            return inZone[(int)(seed % inZone.Count)];
        }

        /// <summary>
        /// Generates a deterministic random point inside the requested zone's bounds using Mulberry32.
        /// The point is generated on the XZ plane at the collider's center Y.
        /// Callers (e.g., NPCBehaviorController) should use NavMesh.SamplePosition to snap it to the floor.
        /// </summary>
        public Vector3? GetDeterministicPoint(string zoneId, uint seed)
        {
            if (_lookup == null) Init();

            if (string.IsNullOrEmpty(zoneId) || !_lookup.TryGetValue(zoneId, out var col))
            {
                return null;
            }

            // Mulberry32 RNG (matches backend implementation)
            uint t = seed;
            t = t + 0x6D2B79F5;
            uint r = t ^ (t >> 15);
            r = (r * (1 | t)) & 0xFFFFFFFF;
            r = r ^ (r >> 7);
            r = (r + (r * (61 | r))) & 0xFFFFFFFF;
            r = r ^ (r >> 14);
            
            float rand01_x = (r >> 0) / 4294967296f; // Use lower bits for X

            // Advance state for Z
            t = t + 0x6D2B79F5;
            r = t ^ (t >> 15);
            r = (r * (1 | t)) & 0xFFFFFFFF;
            r = r ^ (r >> 7);
            r = (r + (r * (61 | r))) & 0xFFFFFFFF;
            r = r ^ (r >> 14);

            float rand01_z = (r >> 0) / 4294967296f; // Use lower bits for Z

            var center = col.bounds.center;
            var extents = col.bounds.extents;

            float x = center.x - extents.x + rand01_x * (extents.x * 2f);
            float z = center.z - extents.z + rand01_z * (extents.z * 2f);
            float y = center.y - extents.y + 0.1f; // Slightly above bottom

            return new Vector3(x, y, z);
        }
    }
}
