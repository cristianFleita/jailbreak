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
    ///   1. Single action: zoneId/seed + animTrigger + duration
    ///   2. Action sequence: ordered steps processed sequentially
    ///
    /// The backend does NOT stream positions. All movement is local NavMesh.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class NPCBehaviorController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NavMeshAgent  agent;
        [SerializeField] private Animator      animator;
        [SerializeField] private ZoneRegistry  zoneRegistry;

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
        private string _currentStepZoneId;

        // Pending reassign received while NPC is mid-LOOPING cycle
        private NPCAssignmentData _pendingAssignment;
        private float  _loopingGraceTimer;
        private const float LoopingGrace = 5f;

        // ─── Emergent behavior state ──────────────────────────────────────────
        private bool   _isPlayingEmergent;
        private float  _emergentTimer;
        private NPCAssignmentData _preEmergentState;

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
            if (_isPlayingEmergent)
            {
                _emergentTimer -= Time.deltaTime;
                if (_emergentTimer <= 0f) OnEmergentComplete();
                return;
            }

            if (_pendingAssignment != null)
            {
                _loopingGraceTimer -= Time.deltaTime;
                if (_loopingGraceTimer <= 0f)
                {
                    ApplyAssignment(_pendingAssignment);
                    _pendingAssignment = null;
                }
            }

            if (_sequenceSteps != null)
            {
                UpdateSequence();
                return;
            }

            if (_current == null) return;

            if (!_hasArrived && !agent.pathPending && agent.remainingDistance < arrivalThreshold)
            {
                _hasArrived = true;
                OnReachedDestination();
            }

            if (_hasArrived)
            {
                _actionTimer -= Time.deltaTime;
                if (_actionTimer <= 0f) OnActionComplete();
            }
        }

        // ─── Public API ───────────────────────────────────────────────────────

        public void AssignAction(NPCAssignmentData data, ZoneRegistry registry = null)
        {
            if (registry != null) zoneRegistry = registry;

            if (_isLooping && _current != null && data.actionSequence == null)
            {
                _pendingAssignment = data;
                _loopingGraceTimer = LoopingGrace;
                return;
            }

            ApplyAssignment(data);
        }

        public void PlayEmergentAction(string animTrigger, float duration)
        {
            if (_isPlayingEmergent) return;

            _preEmergentState = _current;
            _isPlayingEmergent = true;
            _emergentTimer = duration;

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
            if (_preEmergentState != null && _current != null)
            {
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
                else PlayAnimation("idle");
            }
            else PlayAnimation("idle");
            _preEmergentState = null;
        }

        public void ApplyMoodHint(string animHint)
        {
            if (!IsNavigating && !_isPlayingEmergent && (_current == null || _hasArrived))
            {
                PlayAnimation(animHint);
            }
        }

        // ─── Private: Apply ───────────────────────────────────────────────────

        private void ApplyAssignment(NPCAssignmentData data)
        {
            CleanupCurrent();

            if (data.walkSpeedMult > 0 && agent != null)
                agent.speed = 3.5f * data.walkSpeedMult; // base speed * mult

            Debug.Log($"[NPC-CTRL] {name} ApplyAssignment action={data.actionId} seq={(data.actionSequence != null ? data.actionSequence.Length : 0)}steps agent.onNavMesh={(agent != null && agent.isOnNavMesh)} zoneRegistry={(zoneRegistry != null ? "set" : "NULL")}");

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
            _currentStepZoneId = null;
            _current = null;
            _sequenceSteps = null;
            _isLooping = false;
        }

        // ─── Single Action Mode ───────────────────────────────────────────────

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
                if (agent == null || !agent.isOnNavMesh) return;
                agent.SetDestination(destination.Value);
                PlayAnimation("walk");
            }
            else
            {
                _hasArrived = true;
                PlayAnimation(data.animTrigger);
            }
        }

        // ─── Sequence Mode ───────────────────────────────────────────────────

        private void StartSequence(NPCAssignmentData data)
        {
            _sequenceSteps = data.actionSequence;
            _sequenceIndex = 0;
            _current = data;
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

            if (!_stepArrived)
            {
                if (!agent.pathPending && agent.remainingDistance < arrivalThreshold)
                {
                    _stepArrived = true;
                    OnStepArrived(step);
                }
                return;
            }

            if (step.duration > 0)
            {
                _stepTimer -= Time.deltaTime;
                if (_stepTimer <= 0f) AdvanceSequence();
            }
        }

        private void BeginStep(NPCActionStepData step)
        {
            _stepArrived = false;
            _stepTimer = step.duration;
            _currentStepZoneId = step.zoneId;

            Vector3? destination = null;
            string destSrc = "none";

            if (!string.IsNullOrEmpty(step.zoneId) && zoneRegistry != null)
            {
                var point = zoneRegistry.GetDeterministicPoint(step.zoneId, step.seed);
                if (point.HasValue)
                {
                    if (NavMesh.SamplePosition(point.Value, out var hit, 5f, NavMesh.AllAreas))
                    { destination = hit.position; destSrc = "zone+navmesh"; }
                    else { destination = point.Value; destSrc = "zone(no-navmesh)"; }
                }
                else destSrc = $"zone '{step.zoneId}' not registered";
            }

            if (!destination.HasValue && !string.IsNullOrEmpty(step.socialPartnerId) && _resolvePartnerTransform != null)
            {
                var partnerTransform = _resolvePartnerTransform(step.socialPartnerId);
                if (partnerTransform != null) { destination = partnerTransform.position; destSrc = "partner"; }
            }

            Debug.Log($"[NPC-CTRL] {name} BeginStep {_sequenceIndex + 1}/{_sequenceSteps.Length} action={step.actionId} anim={step.animTrigger} zone={step.zoneId ?? "-"} dur={step.duration:F1}s dest={destSrc}");

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

            _stepArrived = true;
            OnStepArrived(step);
        }

        private void OnStepArrived(NPCActionStepData step)
        {
            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }

            if (step.duration > 0)
            {
                PlayAnimation(step.animTrigger);
            }
            else AdvanceSequence();
        }

        private void AdvanceSequence()
        {
            _sequenceIndex++;
            if (_sequenceIndex >= _sequenceSteps.Length) OnSequenceComplete();
            else BeginStep(_sequenceSteps[_sequenceIndex]);
        }

        private void OnSequenceComplete()
        {
            _currentStepZoneId = null;
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

        private void OnReachedDestination()
        {
            if (_current == null) return;

            if (_isLooping && _current.seedChain != null && _current.seedChain.Length > 1)
            {
                _chainIndex = (_chainIndex + 1) % _current.seedChain.Length;
                var nextSeed = _current.seedChain[_chainIndex];
                
                var point = zoneRegistry?.GetDeterministicPoint(_current.zoneId, nextSeed);
                if (point.HasValue)
                {
                    if (NavMesh.SamplePosition(point.Value, out var hit, 5f, NavMesh.AllAreas))
                    {
                        agent.SetDestination(hit.position);
                        _hasArrived = false;
                        PlayAnimation("walk");
                        return;
                    }
                }
            }

            agent.ResetPath();
            agent.velocity = Vector3.zero;
            PlayAnimation(_current.animTrigger);
        }

        private void OnActionComplete()
        {
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
            if (zoneRegistry == null) return null;

            if (data.seedChain != null && data.seedChain.Length > 0)
            {
                var pt = zoneRegistry.GetDeterministicPoint(data.zoneId, data.seedChain[0]);
                if (pt.HasValue && NavMesh.SamplePosition(pt.Value, out var hit, 5f, NavMesh.AllAreas)) return hit.position;
            }

            if (!string.IsNullOrEmpty(data.zoneId))
            {
                var pt = zoneRegistry.GetDeterministicPoint(data.zoneId, data.seed);
                if (pt.HasValue && NavMesh.SamplePosition(pt.Value, out var hit, 5f, NavMesh.AllAreas)) return hit.position;
            }

            if (!string.IsNullOrEmpty(data.socialPartnerId) && _resolvePartnerTransform != null)
            {
                var partnerTransform = _resolvePartnerTransform(data.socialPartnerId);
                if (partnerTransform != null) return partnerTransform.position;
            }

            return null;
        }

        // ─── Animator Map ───────────────────────────────────────────────────────
        private void PlayAnimation(string backendTrigger)
        {
            if (animator == null)
            {
                Debug.LogWarning($"[NPC-CTRL] {name} PlayAnimation('{backendTrigger}') skipped — animator is NULL");
                return;
            }
            if (string.IsNullOrEmpty(backendTrigger)) return;
            string stateName = MapTriggerToStateName(backendTrigger);

            if (animator.HasState(0, Animator.StringToHash(stateName)))
            {
                try { animator.CrossFade(stateName, 0.25f); }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[NPC-CTRL] {name} CrossFade('{stateName}') failed: {ex.Message}");
                }
            }
            else
            {
                // Comment out to reduce console spam, or keep as trace log
                // Debug.Log($"[NPC-CTRL] Animator state '{stateName}' missing. Skipping.");
            }
        }

        private string MapTriggerToStateName(string trigger)
        {
            return trigger switch
            {
                "idle"             => "Idle",
                "walk"             => "Walking",
                "walk_slow"        => "Walking",
                "Walking"          => "Walking",
                "Salute"           => "Salute",
                "talk_standing"    => "Talking",
                "talk_seated"      => "SittingTalking",
                "whisper_seated"   => "TellingSecret",
                "whisper"          => "TellingSecret",
                "argue"            => "Angry",
                "nod"              => "Salute",
                "fist_bump"        => "Salute",
                "stretch"          => "Idle",
                "yawn"             => "Idle",
                "sigh"             => "Idle",
                "fidget"           => "Idle",
                "look_around"      => "Idle",
                "lean_think"       => "Idle",
                "crack_knuckles"   => "Idle",
                "pace"             => "Walking",
                "idle_window"      => "Idle",
                "idle_queue"       => "Idle",
                "idle_check"       => "Idle",
                "lean_wall"        => "Idle",
                "sit_eat"          => "Sitting",
                "sit_eat_talk"     => "SittingTalking",
                "sit_bench"        => "SeatedIdle",
                "sit_cards"        => "Sitting",
                "sit_idle"         => "Sitting",
                "sit_bed_edge"     => "Sitting",
                "sit_floor"        => "Sitting",
                "read_book"        => "SeatedIdle",
                "serve_self"       => "Rummaging",
                "deposit_tray"     => "Opening",
                "carry_tray"       => "Walking",
                "carry_box"        => "Walking",
                "carry_basket"     => "Walking",
                "work_bench"       => "ButtonPushing",
                "inspect"          => "Rummaging",
                "load_machine"     => "Opening",
                "fold_clothes"     => "Idle",
                "exercise"         => "PushUp",
                "PushUp"           => "PushUp",
                "shadowbox"        => "Punching",
                "shadow_punch"     => "Punching",
                "kick"             => "Attack",
                "kick_wall"        => "Attack",
                "fight_stance"     => "Punching",
                "block_stance"     => "Idle",
                "taunt"            => "Angry",
                "yell"             => "Angry",
                "lie_down"         => "LyingDown",
                "sleep"            => "LayingPose",
                "toss_turn"        => "LyingDown",
                _                  => "Idle"
            };
        }
    }
}
