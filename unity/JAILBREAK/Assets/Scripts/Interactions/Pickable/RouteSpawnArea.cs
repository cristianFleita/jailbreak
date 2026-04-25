using UnityEngine;

namespace Jailbreak.Interactions.Pickable
{
    /// <summary>
    /// Marks a scene-authored spawn point where a route-critical item can appear.
    /// Discovered by <see cref="RouteItemRegistry"/> at GameScene load and sent to
    /// the backend via `route:register_spawn_areas`. The backend then picks one
    /// valid area per critical item and broadcasts the authoritative
    /// <c>item:state</c> with this spawnAreaId and position.
    ///
    /// IDs must be stable across scene re-saves — do NOT derive from hierarchy
    /// path. Set the ID once and keep it.
    ///
    /// Spec: design/gdd/ruta-1-ventilacion-industrial.md
    /// Plan: design/gdd/ruta-1-implementation-plan.md (Phase C-01)
    /// </summary>
    [DisallowMultipleComponent]
    public class RouteSpawnArea : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Stable unique id. Suggested format: <zone>_<item>_slot — e.g. laundry_cutters_slot")]
        public string spawnAreaId;

        [Tooltip("Zone key the area belongs to (e.g. laundry, workshop).")]
        public string zoneId;

        [Header("Allowed Items")]
        [Tooltip("Item IDs that may spawn here. Typical values: route1_cutters, route1_wrench.")]
        public string[] allowedItemIds = new[] { "route1_cutters" };

        [Header("Placement")]
        [Tooltip("Optional override transform. If null, the component's own transform is used.")]
        public Transform spawnPoint;

        public Vector3 GetWorldPosition()
        {
            return spawnPoint != null ? spawnPoint.position : transform.position;
        }

        public bool IsValid()
        {
            if (string.IsNullOrEmpty(spawnAreaId)) return false;
            if (string.IsNullOrEmpty(zoneId)) return false;
            if (allowedItemIds == null || allowedItemIds.Length == 0) return false;
            foreach (var id in allowedItemIds)
                if (!string.IsNullOrEmpty(id)) return true;
            return false;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.55f);
            var pos = GetWorldPosition();
            Gizmos.DrawSphere(pos, 0.25f);
            Gizmos.DrawLine(pos, pos + Vector3.up * 1.2f);
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(spawnAreaId))
                spawnAreaId = gameObject.name;
        }
    }
}
