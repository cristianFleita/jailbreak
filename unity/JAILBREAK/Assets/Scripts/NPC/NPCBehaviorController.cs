using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Jailbreak.Network;

namespace Jailbreak.NPC
{
    /// <summary>
    /// Drives a single NPC's movement and animation based on assignments from
    /// the backend (via JailRoutineManager).
    ///
    /// The backend assigns:
    ///   - waypointId or waypointChain  → NavMeshAgent destination(s)
    ///   - animTrigger                  → Animator trigger on arrival
    ///   - duration                     → how long to perform the action
    ///   - loop                         → cycle through chain indefinitely
    ///
    /// The backend does NOT stream positions. All movement is local NavMesh.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class NPCBehaviorController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NavMeshAgent  agent;
        [SerializeField] private Animator      animator;
        [SerializeField] private WaypointRegistry waypointRegistry;

        [Header("Tuning")]
        [SerializeField] private float arrivalThreshold = 0.3f;  // meters
        [SerializeField] private float idleFallbackDelay = 3f;   // wait before idle if no reassign

        // ─── Current action state ─────────────────────────────────────────────
        private NPCAssignmentData _current;
        private float  _actionTimer;
        private bool   _hasArrived;
        private int    _chainIndex;
        private bool   _isLooping;

        // Pending reassign received while NPC is mid-LOOPING cycle
        private NPCAssignmentData _pendingAssignment;
        private float  _loopingGraceTimer;
        private const float LoopingGrace = 5f;

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            if (agent    == null) agent    = GetComponent<NavMeshAgent>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        private void Update()
        {
            // Flush pending assignment once LOOPING grace expires
            if (_pendingAssignment != null)
            {
                _loopingGraceTimer -= Time.deltaTime;
                if (_loopingGraceTimer <= 0f)
                {
                    ApplyAssignment(_pendingAssignment);
                    _pendingAssignment = null;
                }
            }

            if (_current == null) return;

            // Check arrival
            if (!_hasArrived && !agent.pathPending && agent.remainingDistance < arrivalThreshold)
            {
                _hasArrived = true;
                OnReachedWaypoint();
            }

            // Count down action duration
            if (_hasArrived)
            {
                _actionTimer -= Time.deltaTime;
                if (_actionTimer <= 0f)
                    OnActionComplete();
            }
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Assign a new action from a phase:start or npc:reassign event.
        /// If the NPC is currently LOOPING, honours a short grace period.
        /// </summary>
        public void AssignAction(NPCAssignmentData data, WaypointRegistry registry = null)
        {
            if (registry != null) waypointRegistry = registry;

            // Edge case: reassign while mid-LOOPING → grace period
            if (_isLooping && _current != null)
            {
                _pendingAssignment = data;
                _loopingGraceTimer = LoopingGrace;
                return;
            }

            ApplyAssignment(data);
        }

        // ─── Private: Apply ───────────────────────────────────────────────────

        private void ApplyAssignment(NPCAssignmentData data)
        {
            if (_current != null)
            {
                if (!string.IsNullOrEmpty(_current.waypointId))
                    waypointRegistry?.Release(_current.waypointId);
                if (_current.waypointChain != null)
                    foreach (var wp in _current.waypointChain)
                        waypointRegistry?.Release(wp);
            }

            _current      = data;
            _actionTimer  = data.duration;
            _hasArrived   = false;
            _chainIndex   = 0;
            _isLooping    = data.loop;

            // Navigate to first waypoint
            var destination = ResolveFirstDestination(data);
            if (destination.HasValue)
            {
                // Try to reserve slot; if full → idle in place
                var wpId = data.waypointChain != null && data.waypointChain.Length > 0
                    ? data.waypointChain[0] : data.waypointId;

                if (waypointRegistry != null && !string.IsNullOrEmpty(wpId))
                {
                    bool reserved = waypointRegistry.Reserve(wpId);
                    if (!reserved)
                    {
                        // Waypoint full — fall back to idle
                        PlayAnimation("idle");
                        _current = null;
                        return;
                    }
                }

                agent.SetDestination(destination.Value);
                PlayAnimation("walk"); // Move to waypoint!
            }
            else
            {
                // No waypoint resolved → idle immediately
                _hasArrived  = true;
                PlayAnimation("idle");
            }
        }

        // ─── Private: Arrival & Chain Logic ──────────────────────────────────

        private void OnReachedWaypoint()
        {
            if (_current == null) return;

            PlayAnimation(_current.animTrigger);

            // LOOPING chain: advance to next waypoint
            if (_isLooping && _current.waypointChain != null && _current.waypointChain.Length > 1)
            {
                // Release current chain slot before advancing
                waypointRegistry?.Release(_current.waypointChain[_chainIndex]);

                _chainIndex = (_chainIndex + 1) % _current.waypointChain.Length;
                var nextId  = _current.waypointChain[_chainIndex];
                var nextPos = waypointRegistry?.GetPosition(nextId);

                if (nextPos.HasValue)
                {
                    waypointRegistry?.Reserve(nextId);
                    agent.SetDestination(nextPos.Value);
                    _hasArrived = false;
                    PlayAnimation("walk");
                }
            }
        }

        private void OnActionComplete()
        {
            _isLooping = false;
            _current   = null;
            PlayAnimation("idle");
        }

        private Vector3? ResolveFirstDestination(NPCAssignmentData data)
        {
            if (waypointRegistry == null) return null;
            if (data.waypointChain != null && data.waypointChain.Length > 0)
                return waypointRegistry.GetPosition(data.waypointChain[0]);
            if (!string.IsNullOrEmpty(data.waypointId))
                return waypointRegistry.GetPosition(data.waypointId);
            return null;
        }

        // ─── Animator Map ───────────────────────────────────────────────────────

        private void PlayAnimation(string backendTrigger)
        {
            if (animator == null || string.IsNullOrEmpty(backendTrigger)) return;
            
            // Map the Node.js trigger string directly to the Unity State Name
            string stateName = MapTriggerToStateName(backendTrigger);
            
            try 
            { 
                // Safely crossfade into the raw state name without needing parameters
                animator.CrossFade(stateName, 0.25f); 
            }
            catch { /* Ignore if animator state is missing */ }
        }

        private string MapTriggerToStateName(string trigger)
        {
            return trigger switch
            {
                "idle" => "Idle",
                "talk_standing" => "Talking",
                "stretch" => "Idle",
                "yawn" => "Idle",
                "walk_slow" => "Walking",
                "sit_eat" => "Sitting",
                "walk" => "Walking",
                "idle_queue" => "Idle",
                "talk_seated" => "SittingTalking",
                "carry_tray" => "Walking",
                "work_bench" => "ButtonPushing",
                "carry_box" => "Walking",
                "inspect" => "Rummaging",
                "load_machine" => "Opening",
                "fold_clothes" => "Idle",
                "carry_basket" => "Walking",
                "idle_check" => "Idle",
                "sit_bench" => "SeatedIdle",
                "exercise" => "PushUp",
                "sit_cards" => "Sitting",
                "lean_wall" => "Idle",
                "shadowbox" => "Punching",
                "kick" => "Attack",
                "sit_idle" => "Sitting",
                "lie_down" => "LyingDown",
                "sit_bed_edge" => "Sitting",
                "read_book" => "SeatedIdle",
                "idle_window" => "Idle",
                "whisper_seated" => "TellingSecret",
                "sleep" => "LayingPose",
                "toss_turn" => "LyingDown",
                _ => "Idle"
            };
        }
    }
}