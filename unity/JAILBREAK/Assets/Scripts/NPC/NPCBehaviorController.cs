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
    /// Supports two modes:
    ///   1. Single action: waypointId/waypointChain + animTrigger + duration (legacy)
    ///   2. Action sequence: ordered steps processed sequentially (cafeteria flow, Phase 1 chain)
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
        [SerializeField] private float arrivalThreshold = 0.3f;
        [SerializeField] private float idleFallbackDelay = 3f;

        // ─── Current action state ─────────────────────────────────────────────
        public bool IsNavigating => _current != null || _sequenceSteps != null;
        public bool IsEmergent => _isPlayingEmergent;

        private NPCAssignmentData _current;
        private float  _actionTimer;
        private bool   _hasArrived;
        private int    _chainIndex;
        private bool   _isLooping;

        // ─── Sequence state (ordered multi-step flows) ────────────────────────
        private NPCActionStepData[] _sequenceSteps;
        private int    _sequenceIndex;
        private float  _stepTimer;
        private bool   _stepArrived;
        private string _currentStepWaypointId;

        // Pending reassign received while NPC is mid-LOOPING cycle
        private NPCAssignmentData _pendingAssignment;
        private float  _loopingGraceTimer;
        private const float LoopingGrace = 5f;

        // ─── Emergent behavior state ──────────────────────────────────────────
        private bool   _isPlayingEmergent;
        private float  _emergentTimer;
        private NPCAssignmentData _preEmergentState; // saved state to resume after

        // ─── Social partner lookup (set by JailRoutineManager) ────────────────
        private System.Func<string, Transform> _resolvePartnerTransform;

        public void SetPartnerResolver(System.Func<string, Transform> resolver)
        {
            _resolvePartnerTransform = resolver;
        }

        // ─── Unity Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            if (agent    == null) agent    = GetComponent<NavMeshAgent>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        private void Update()
        {
            // Emergent behavior: plays a short animation then resumes previous state
            if (_isPlayingEmergent)
            {
                _emergentTimer -= Time.deltaTime;
                if (_emergentTimer <= 0f)
                {
                    OnEmergentComplete();
                }
                return;
            }

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

            // Sequence mode: process ordered steps
            if (_sequenceSteps != null)
            {
                UpdateSequence();
                return;
            }

            // Single action mode (legacy)
            if (_current == null) return;

            if (!_hasArrived && !agent.pathPending && agent.remainingDistance < arrivalThreshold)
            {
                _hasArrived = true;
                OnReachedWaypoint();
            }

            if (_hasArrived)
            {
                _actionTimer -= Time.deltaTime;
                if (_actionTimer <= 0f)
                    OnActionComplete();
            }
        }

        // ─── Public API ───────────────────────────────────────────────────────

        public void AssignAction(NPCAssignmentData data, WaypointRegistry registry = null)
        {
            Debug.Log($"[NPC-DEBUG] {gameObject.name} AssignAction: action={data.actionId} " +
                      $"seq={(data.actionSequence != null ? data.actionSequence.Length + " steps" : "none")}");

            if (registry != null) waypointRegistry = registry;

            // Edge case: reassign while mid-LOOPING → grace period
            if (_isLooping && _current != null && data.actionSequence == null)
            {
                Debug.Log($"[NPC-DEBUG] {gameObject.name} is mid-LOOPING — queuing assignment, grace={LoopingGrace}s");
                _pendingAssignment = data;
                _loopingGraceTimer = LoopingGrace;
                return;
            }

            ApplyAssignment(data);
        }

        /// <summary>
        /// Triggers an emergent/spontaneous behavior: NPC interrupts current action,
        /// plays a short animation, then resumes where it left off.
        /// Called by JailRoutineManager when backend sends npc:emergent event.
        /// </summary>
        public void PlayEmergentAction(string animTrigger, float duration)
        {
            if (_isPlayingEmergent) return; // don't stack emergent actions

            Debug.Log($"[NPC-EMERGENT] {gameObject.name} emergent: {animTrigger} for {duration:F1}s");

            // Save current state
            _preEmergentState = _current;
            _isPlayingEmergent = true;
            _emergentTimer = duration;

            // Stop movement during emergent action
            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }

            PlayAnimation(animTrigger);
        }

        private void OnEmergentComplete()
        {
            _isPlayingEmergent = false;
            Debug.Log($"[NPC-EMERGENT] {gameObject.name} emergent complete — resuming");

            // Resume previous navigation if we had a destination
            if (_preEmergentState != null && _current != null)
            {
                // Re-set destination if we were navigating
                var dest = ResolveFirstDestination(_current);
                if (dest.HasValue && !_hasArrived && agent != null && agent.isOnNavMesh)
                {
                    agent.SetDestination(dest.Value);
                    PlayAnimation("walk");
                }
                else if (_hasArrived && _current != null)
                {
                    PlayAnimation(_current.animTrigger);
                }
                else
                {
                    PlayAnimation("idle");
                }
            }
            else
            {
                PlayAnimation("idle");
            }

            _preEmergentState = null;
        }

        /// <summary>
        /// Updates NPC idle animation based on mood shift from backend.
        /// This creates subtle visual cues (fidgeting, pacing) without
        /// interrupting the current action flow.
        /// </summary>
        public void ApplyMoodHint(string animHint)
        {
            // Only apply mood hint if NPC is currently idle (not navigating or acting)
            if (!IsNavigating && !_isPlayingEmergent && (_current == null || _hasArrived))
            {
                PlayAnimation(animHint);
            }
        }

        // ─── Private: Apply ───────────────────────────────────────────────────

        private void ApplyAssignment(NPCAssignmentData data)
        {
            // Clean up previous state
            CleanupCurrent();

            // Route to sequence mode or single action mode
            if (data.actionSequence != null && data.actionSequence.Length > 0)
            {
                StartSequence(data);
            }
            else
            {
                StartSingleAction(data);
            }
        }

        private void CleanupCurrent()
        {
            if (_current != null)
            {
                if (!string.IsNullOrEmpty(_current.waypointId))
                    waypointRegistry?.Release(_current.waypointId);
                if (_current.waypointChain != null)
                    foreach (var wp in _current.waypointChain)
                        waypointRegistry?.Release(wp);
            }

            if (!string.IsNullOrEmpty(_currentStepWaypointId))
            {
                waypointRegistry?.Release(_currentStepWaypointId);
                _currentStepWaypointId = null;
            }

            _current = null;
            _sequenceSteps = null;
            _isLooping = false;
        }

        // ─── Single Action Mode (legacy) ──────────────────────────────────────

        private void StartSingleAction(NPCAssignmentData data)
        {
            _current      = data;
            _actionTimer  = data.duration;
            _hasArrived   = false;
            _chainIndex   = 0;
            _isLooping    = data.loop;

            var destination = ResolveFirstDestination(data);
            if (destination.HasValue)
            {
                var wpId = data.waypointChain != null && data.waypointChain.Length > 0
                    ? data.waypointChain[0] : data.waypointId;

                if (waypointRegistry != null && !string.IsNullOrEmpty(wpId))
                {
                    bool reserved = waypointRegistry.Reserve(wpId);
                    if (!reserved)
                    {
                        Debug.LogWarning($"[NPC-DEBUG] {gameObject.name} waypoint '{wpId}' FULL — falling back to idle");
                        PlayAnimation("idle");
                        _current = null;
                        return;
                    }
                }

                if (agent == null || !agent.isOnNavMesh)
                {
                    Debug.LogError($"[NPC-DEBUG] {gameObject.name} NavMeshAgent issue — cannot navigate");
                    return;
                }

                agent.SetDestination(destination.Value);
                PlayAnimation("walk");
            }
            else
            {
                _hasArrived = true;
                PlayAnimation(data.animTrigger);
            }
        }

        // ─── Sequence Mode (cafeteria flow, Phase 1 chain) ───────────────────

        private void StartSequence(NPCAssignmentData data)
        {
            _sequenceSteps = data.actionSequence;
            _sequenceIndex = 0;
            _current = data; // keep reference for cleanup

            Debug.Log($"[NPC-SEQ] {gameObject.name} starting sequence: {_sequenceSteps.Length} steps");

            BeginStep(_sequenceSteps[0]);
        }

        private void UpdateSequence()
        {
            if (_sequenceSteps == null || _sequenceIndex >= _sequenceSteps.Length)
            {
                OnSequenceComplete();
                return;
            }

            var step = _sequenceSteps[_sequenceIndex];

            // Phase 1: Navigate to destination
            if (!_stepArrived)
            {
                if (!agent.pathPending && agent.remainingDistance < arrivalThreshold)
                {
                    _stepArrived = true;
                    OnStepArrived(step);
                }
                return;
            }

            // Phase 2: Perform action at destination (duration > 0)
            if (step.duration > 0)
            {
                _stepTimer -= Time.deltaTime;
                if (_stepTimer <= 0f)
                {
                    AdvanceSequence();
                }
            }
            // duration == 0 means walk-only — advance immediately on arrival
        }

        private void BeginStep(NPCActionStepData step)
        {
            _stepArrived = false;
            _stepTimer = step.duration;

            // Release previous step's waypoint
            if (!string.IsNullOrEmpty(_currentStepWaypointId))
            {
                waypointRegistry?.Release(_currentStepWaypointId);
                _currentStepWaypointId = null;
            }

            Debug.Log($"[NPC-SEQ] {gameObject.name} step {_sequenceIndex}/{_sequenceSteps.Length}: " +
                      $"action={step.actionId} wp={step.waypointId ?? "none"} dur={step.duration:F1}s " +
                      $"partner={step.socialPartnerId ?? "none"}");

            // Resolve destination
            Vector3? destination = null;

            if (!string.IsNullOrEmpty(step.waypointId) && waypointRegistry != null)
            {
                var pos = waypointRegistry.GetPosition(step.waypointId);
                if (pos.HasValue)
                {
                    waypointRegistry.Reserve(step.waypointId);
                    _currentStepWaypointId = step.waypointId;
                    destination = pos.Value;
                }
            }

            // Social steps without waypoint: navigate to partner's position
            if (!destination.HasValue && !string.IsNullOrEmpty(step.socialPartnerId) && _resolvePartnerTransform != null)
            {
                var partnerTransform = _resolvePartnerTransform(step.socialPartnerId);
                if (partnerTransform != null)
                {
                    destination = partnerTransform.position;
                }
            }

            if (destination.HasValue && agent != null && agent.isOnNavMesh)
            {
                float dist = Vector3.Distance(transform.position, destination.Value);
                if (dist > arrivalThreshold)
                {
                    agent.SetDestination(destination.Value);
                    PlayAnimation("walk");
                    return;
                }
            }

            // Already at destination (or no destination) — skip straight to arrived
            _stepArrived = true;
            OnStepArrived(step);
        }

        private void OnStepArrived(NPCActionStepData step)
        {
            // Stop NavMeshAgent movement
            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }

            if (step.duration > 0)
            {
                // Play action animation and wait for duration
                PlayAnimation(step.animTrigger);
                Debug.Log($"[NPC-SEQ] {gameObject.name} arrived at step {_sequenceIndex}, playing '{step.animTrigger}' for {step.duration:F1}s");
            }
            else
            {
                // Walk-only step (duration == 0) — advance immediately
                Debug.Log($"[NPC-SEQ] {gameObject.name} walk-only step {_sequenceIndex} complete, advancing");
                AdvanceSequence();
            }
        }

        private void AdvanceSequence()
        {
            _sequenceIndex++;

            if (_sequenceIndex >= _sequenceSteps.Length)
            {
                OnSequenceComplete();
                return;
            }

            BeginStep(_sequenceSteps[_sequenceIndex]);
        }

        private void OnSequenceComplete()
        {
            Debug.Log($"[NPC-SEQ] {gameObject.name} sequence complete — idle");

            if (!string.IsNullOrEmpty(_currentStepWaypointId))
            {
                waypointRegistry?.Release(_currentStepWaypointId);
                _currentStepWaypointId = null;
            }

            _sequenceSteps = null;
            _current = null;

            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
            PlayAnimation("idle");
        }

        // ─── Private: Single Action — Arrival & Chain Logic ──────────────────

        private void OnReachedWaypoint()
        {
            if (_current == null) return;

            Debug.Log($"[NPC-DEBUG] {gameObject.name} ARRIVED at waypoint! action={_current.actionId} playing anim='{_current.animTrigger}' timer={_actionTimer:F1}s");

            // LOOPING chain: advance to next waypoint
            if (_isLooping && _current.waypointChain != null && _current.waypointChain.Length > 1)
            {
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
                    return;
                }
            }

            agent.ResetPath();
            agent.velocity = Vector3.zero;
            PlayAnimation(_current.animTrigger);
        }

        private void OnActionComplete()
        {
            Debug.Log($"[NPC-DEBUG] {gameObject.name} action complete — returning to idle");
            _isLooping = false;
            _current   = null;
            if (agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
            PlayAnimation("idle");
        }

        private Vector3? ResolveFirstDestination(NPCAssignmentData data)
        {
            if (waypointRegistry == null) return null;

            if (data.waypointChain != null && data.waypointChain.Length > 0)
                return waypointRegistry.GetPosition(data.waypointChain[0]);

            if (!string.IsNullOrEmpty(data.waypointId))
                return waypointRegistry.GetPosition(data.waypointId);

            // Social action without waypoint: try partner position
            if (!string.IsNullOrEmpty(data.socialPartnerId) && _resolvePartnerTransform != null)
            {
                var partnerTransform = _resolvePartnerTransform(data.socialPartnerId);
                if (partnerTransform != null)
                    return partnerTransform.position;
            }

            return null;
        }

        // ─── Animator Map ───────────────────────────────────────────────────────

        private void PlayAnimation(string backendTrigger)
        {
            if (animator == null || string.IsNullOrEmpty(backendTrigger)) return;

            string stateName = MapTriggerToStateName(backendTrigger);

            try
            {
                animator.CrossFade(stateName, 0.25f);
            }
            catch { /* Ignore if animator state is missing */ }
        }

        private string MapTriggerToStateName(string trigger)
        {
            return trigger switch
            {
                // ─── Core locomotion & idle ──────────────────────────────
                "idle"             => "Idle",
                "walk"             => "Walking",
                "walk_slow"        => "Walking",
                "Walking"          => "Walking",

                // ─── Social ─────────────────────────────────────────────
                "Salute"           => "Salute",
                "talk_standing"    => "Talking",
                "talk_seated"      => "SittingTalking",
                "whisper_seated"   => "TellingSecret",
                "whisper"          => "TellingSecret",
                "argue"            => "Talking",          // reuse Talking with intensity
                "nod"              => "Salute",
                "fist_bump"        => "Salute",

                // ─── Idle expressions ───────────────────────────────────
                "stretch"          => "Idle",
                "yawn"             => "Idle",
                "sigh"             => "Idle",
                "fidget"           => "Idle",
                "look_around"      => "Idle",
                "lean_think"       => "Idle",
                "crack_knuckles"   => "Idle",
                "pace"             => "Walking",          // pacing = slow walking loop
                "idle_window"      => "Idle",
                "idle_queue"       => "Idle",
                "idle_check"       => "Idle",
                "lean_wall"        => "Idle",

                // ─── Sitting ────────────────────────────────────────────
                "sit_eat"          => "Sitting",
                "sit_eat_talk"     => "SittingTalking",
                "sit_bench"        => "SeatedIdle",
                "sit_cards"        => "Sitting",
                "sit_idle"         => "Sitting",
                "sit_bed_edge"     => "Sitting",
                "sit_floor"        => "Sitting",
                "read_book"        => "SeatedIdle",

                // ─── Work / Interact ────────────────────────────────────
                "serve_self"       => "Rummaging",
                "deposit_tray"     => "Opening",
                "carry_tray"       => "Walking",
                "carry_box"        => "Walking",
                "carry_basket"     => "Walking",
                "work_bench"       => "ButtonPushing",
                "inspect"          => "Rummaging",
                "load_machine"     => "Opening",
                "fold_clothes"     => "Idle",

                // ─── Athletic / Combat ──────────────────────────────────
                "exercise"         => "PushUp",
                "PushUp"           => "PushUp",
                "shadowbox"        => "Punching",
                "shadow_punch"     => "Punching",
                "kick"             => "Attack",
                "kick_wall"        => "Attack",
                "fight_stance"     => "Punching",

                // ─── Anti-guard ─────────────────────────────────────────
                "block_stance"     => "Idle",             // TODO: custom block anim
                "taunt"            => "Talking",          // reuse talking with attitude
                "yell"             => "Talking",

                // ─── Lying / Sleep ──────────────────────────────────────
                "lie_down"         => "LyingDown",
                "sleep"            => "LayingPose",
                "toss_turn"        => "LyingDown",

                _                  => "Idle"
            };
        }
    }
}
