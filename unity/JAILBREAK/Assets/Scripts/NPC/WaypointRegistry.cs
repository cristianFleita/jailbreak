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
    ///   2. In the Inspector, drag each scene GameObject onto its "Waypoint Object" slot
    ///   3. Assign this component to JailRoutineManager.waypointRegistry
    /// </summary>
    public class WaypointRegistry : MonoBehaviour
    {
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

        /// <summary>Initialize lookup. Call before first use (or rely on auto-init).</summary>
        public void Init()
        {
            _lookup = new Dictionary<string, WaypointEntry>(waypoints.Count);
            foreach (var entry in waypoints)
            {
                if (!string.IsNullOrEmpty(entry.waypointId))
                    _lookup[entry.waypointId] = entry;
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
