using System;
using System.Collections.Generic;
using Jailbreak.NPC;
using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Tutorial
{
    /// <summary>
    /// Spawns the tutorial's stationary capture-practice NPCs.
    ///
    /// One NPC per backend assignment from `tutorial:start.npcAssignments`,
    /// looked up by <see cref="NpcSpawn.actionId"/>. NPCs do not move and do
    /// not run a routine — they exist solely as targets for the guard's catch
    /// drill (innocent vs suspicious).
    /// </summary>
    public class TutorialNpcRoutineController : MonoBehaviour
    {
        [Serializable]
        public class NpcSpawn
        {
            /// <summary>Backend assignment.actionId this template responds to
            /// (e.g. "tutorial_innocent_walker"). Must match the backend
            /// template id exactly.</summary>
            public string actionId;
            public string displayName = "NPC";
            public Transform spawnPoint;
            /// <summary>Off-routine target — guard reads this as suspect.</summary>
            public bool suspiciousTarget;
            /// <summary>Catching this counts as a valid capture (G3).</summary>
            public bool correctCaptureTarget;
            /// <summary>Optional idle animation state name to crossfade into on
            /// spawn. Empty leaves the animator on its default state.</summary>
            public string idleAnimState = "Idle";
        }

        [SerializeField] private GameObject npcPrefab;
        [SerializeField] private NpcSpawn[] npcs;

        private readonly List<GameObject> _spawned = new();

        private void Start()
        {
            // If a tutorial:start was already cached, the assignments path
            // owns spawning. Otherwise (manual preview, no backend) just spawn
            // straight from the inspector list so designers can iterate.
            var net = NetworkManager.Instance;
            var cached = net != null ? net.CachedTutorialStart : null;
            if (cached != null && cached.npcAssignments != null && cached.npcAssignments.Length > 0)
            {
                ApplyAssignments(cached.npcAssignments);
                return;
            }

            SpawnFromInspector();
        }

        private void OnDestroy()
        {
            ClearSpawned();
        }

        /// <summary>
        /// Spawn one NPC per backend assignment, matching by
        /// <see cref="NpcSpawn.actionId"/>. Idempotent — safe to call again
        /// when a fresh tutorial:start arrives.
        /// </summary>
        public void ApplyAssignments(NPCAssignmentData[] assignments)
        {
            if (assignments == null || assignments.Length == 0) return;

            ClearSpawned();

            foreach (var assignment in assignments)
            {
                if (assignment == null || string.IsNullOrEmpty(assignment.actionId)) continue;

                var template = FindTemplate(assignment.actionId);
                if (template == null)
                {
                    Debug.LogWarning(
                        $"[TutorialNpc] No template for actionId '{assignment.actionId}' " +
                        $"(npcId={assignment.npcId}). Add an NpcSpawn entry on the controller.",
                        this);
                    continue;
                }

                SpawnOne(template, assignment.npcId);
            }
        }

        private void SpawnFromInspector()
        {
            if (npcs == null || npcs.Length == 0) return;
            for (int i = 0; i < npcs.Length; i++)
            {
                var entry = npcs[i];
                if (entry == null) continue;
                SpawnOne(entry, $"tutorial_npc_{i + 1:00}");
            }
        }

        private NpcSpawn FindTemplate(string actionId)
        {
            if (npcs == null) return null;
            foreach (var t in npcs)
                if (t != null && t.actionId == actionId) return t;
            return null;
        }

        private void SpawnOne(NpcSpawn entry, string npcId)
        {
            if (entry == null) return;
            if (string.IsNullOrEmpty(npcId)) npcId = entry.actionId ?? "tutorial_npc";

            var origin = entry.spawnPoint != null ? entry.spawnPoint.position : transform.position;
            var rotation = entry.spawnPoint != null ? entry.spawnPoint.rotation : transform.rotation;
            var go = CreateNpc(npcId, origin, rotation);

            var target = go.GetComponent<TutorialCaptureTarget>();
            if (target == null) target = go.AddComponent<TutorialCaptureTarget>();
            target.targetId = npcId;
            target.displayName = string.IsNullOrEmpty(entry.displayName) ? npcId : entry.displayName;
            target.suspiciousTarget = entry.suspiciousTarget;
            target.correctCaptureTarget = entry.correctCaptureTarget;
            target.routineNpc = true;

            var identity = go.GetComponent<NPCIdentity>();
            if (identity == null) identity = go.AddComponent<NPCIdentity>();
            identity.NpcId = npcId;
            identity.NpcType = "tutorial_prisoner";

            PlayIdle(go, entry.idleAnimState);
            _spawned.Add(go);
        }

        private GameObject CreateNpc(string id, Vector3 position, Quaternion rotation)
        {
            GameObject go;
            if (npcPrefab != null)
            {
                go = Instantiate(npcPrefab, position, rotation, transform);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.transform.SetParent(transform);
                go.transform.SetPositionAndRotation(position, rotation);
                go.transform.localScale = new Vector3(0.55f, 1.05f, 0.55f);

                var renderer = go.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetColor("_BaseColor", new Color(0.22f, 0.48f, 0.72f, 1f));
                    mpb.SetColor("_Color", new Color(0.22f, 0.48f, 0.72f, 1f));
                    renderer.SetPropertyBlock(mpb);
                }
            }

            // Stationary NPCs — no NavMeshAgent, no movement actor. Anything
            // else on the prefab (animator, colliders) is left untouched.
            go.name = $"TutorialNPC_{id}";
            return go;
        }

        private static void PlayIdle(GameObject go, string idleStateName)
        {
            if (string.IsNullOrEmpty(idleStateName)) return;
            var animator = go.GetComponentInChildren<Animator>();
            if (animator == null) return;
            if (!animator.HasState(0, Animator.StringToHash(idleStateName))) return;
            animator.CrossFade(idleStateName, 0.1f);
        }

        private void ClearSpawned()
        {
            foreach (var go in _spawned)
                if (go != null) Destroy(go);
            _spawned.Clear();
        }
    }
}
