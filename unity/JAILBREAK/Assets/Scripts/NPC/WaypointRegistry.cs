using System.Collections.Generic;
using UnityEngine;

namespace Jailbreak.NPC
{
    /// <summary>
    /// MonoBehaviour registry mapping waypoint ID strings to scene GameObjects.
    /// Backend only knows waypoint ID strings — this resolves them to Vector3 positions
    /// for NavMeshAgent destinations.
    ///
    /// Usage:
    ///   1. Run menu: Jailbreak → Setup WaypointRegistry in Scene
    ///   2. Add parent GameObjects to "Waypoint Roots" (one per zone)
    ///   3. Name each child GameObject to match its waypointId (e.g. "yard_bench_01")
    ///   4. Assign this component to JailRoutineManager.waypointRegistry
    ///
    /// Auto-resolve: On Init, children of each root are scanned recursively.
    /// If child.name matches a waypointId, the waypointObject is assigned automatically.
    /// Manual waypointObject assignments in the Inspector take priority (override).
    /// </summary>
    public class WaypointRegistry : MonoBehaviour
    {
        [Header("Drag parent transforms here (one per zone). Children are matched by name.")]
        [SerializeField] private Transform[] waypointRoots = new Transform[0];

        [SerializeField] private List<WaypointEntry> waypoints = new();

        private Dictionary<string, WaypointEntry> _lookup;

        // ─── Nested Types ─────────────────────────────────────────────────────

        [System.Serializable]
        public class WaypointEntry
        {
            public string     waypointId;           // "yard_bench_03"
            public GameObject waypointObject;        // drag the GameObject from the Scene here
            public string     zone;                 // "patio_exterior"
            public string     subZone;              // "taller", "lavanderia", etc.
            public bool       isExclusive;          // only 1 occupant at a time
            public int        maxOccupants = 1;     // card tables = 4
            public string[]   validPhases;          // phases where this WP is usable ("1","4"…)

            [HideInInspector] public int currentOccupants;

            public bool      IsFull     => currentOccupants >= maxOccupants;
            public bool      IsAvailable => !IsFull;
            public Transform Transform  => waypointObject != null ? waypointObject.transform : null;
            public Vector3   Position   => waypointObject != null ? waypointObject.transform.position : Vector3.zero;
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>Initialize lookup and auto-resolve waypoint objects from roots.</summary>
        public void Init()
        {
            _lookup = new Dictionary<string, WaypointEntry>(waypoints.Count);
            foreach (var entry in waypoints)
            {
                if (!string.IsNullOrEmpty(entry.waypointId))
                    _lookup[entry.waypointId] = entry;
            }

            // Auto-resolve: scan children of each root and match by name
            if (waypointRoots != null)
            {
                int resolved = 0;
                foreach (var root in waypointRoots)
                {
                    if (root == null) continue;
                    ResolveChildrenRecursive(root, ref resolved);
                }
                Debug.Log($"[WaypointRegistry] Auto-resolved {resolved}/{waypoints.Count} waypoints from {waypointRoots.Length} roots.");
            }

            // Warn about unresolved entries
            int missing = 0;
            foreach (var entry in waypoints)
            {
                if (entry.waypointObject == null)
                {
                    missing++;
                    if (missing <= 10)
                        Debug.LogWarning($"[WaypointRegistry] No GameObject found for waypoint '{entry.waypointId}'");
                }
            }
            if (missing > 10)
                Debug.LogWarning($"[WaypointRegistry] ... and {missing - 10} more unresolved waypoints.");
        }

        private void ResolveChildrenRecursive(Transform parent, ref int resolved)
        {
            foreach (Transform child in parent)
            {
                if (_lookup.TryGetValue(child.name, out var entry))
                {
                    // Manual override: skip if already assigned in Inspector
                    if (entry.waypointObject == null)
                    {
                        entry.waypointObject = child.gameObject;
                        resolved++;
                    }
                }
                // Recurse into nested children
                if (child.childCount > 0)
                    ResolveChildrenRecursive(child, ref resolved);
            }
        }

        /// <summary>Returns the entry for the given ID, or null if not found.</summary>
        public WaypointEntry Get(string id)
        {
            EnsureInit();
            return _lookup.TryGetValue(id, out var entry) ? entry : null;
        }

        /// <summary>Returns the world position for the given waypoint ID.</summary>
        public Vector3? GetPosition(string id)
        {
            var entry = Get(id);
            return entry?.waypointObject != null ? entry.waypointObject.transform.position : (Vector3?)null;
        }

        /// <summary>Returns all entries for the given zone.</summary>
        public List<WaypointEntry> GetByZone(string zone)
        {
            EnsureInit();
            var result = new List<WaypointEntry>();
            foreach (var e in waypoints)
                if (e.zone == zone) result.Add(e);
            return result;
        }

        /// <summary>Returns available (not full) entries for the given phase number.</summary>
        public List<WaypointEntry> GetAvailableForPhase(int phase)
        {
            EnsureInit();
            var phaseStr = phase.ToString();
            var result   = new List<WaypointEntry>();
            foreach (var e in waypoints)
            {
                if (e.IsFull) continue;
                if (e.validPhases == null || e.validPhases.Length == 0) { result.Add(e); continue; }
                foreach (var p in e.validPhases)
                    if (p == phaseStr) { result.Add(e); break; }
            }
            return result;
        }

        /// <summary>
        /// Tries to reserve a slot at the given waypoint.
        /// Returns false if the waypoint is full (caller should fallback to Idle).
        /// </summary>
        public bool Reserve(string id)
        {
            var entry = Get(id);
            if (entry == null || entry.IsFull) return false;
            entry.currentOccupants++;
            return true;
        }

        /// <summary>Releases one occupant slot at the given waypoint.</summary>
        public void Release(string id)
        {
            var entry = Get(id);
            if (entry == null) return;
            entry.currentOccupants = Mathf.Max(0, entry.currentOccupants - 1);
        }

        /// <summary>Resets all occupant counts (call on phase transition).</summary>
        public void ResetOccupants()
        {
            foreach (var e in waypoints)
                e.currentOccupants = 0;
        }

        // ─── Private ─────────────────────────────────────────────────────────

        private void EnsureInit()
        {
            if (_lookup == null) Init();
        }

        private void Awake()
        {
            Init();
        }
    }
}
