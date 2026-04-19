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

        [Header("Food Counter (used when this NPC picks up a plate)")]
        [Tooltip("Right-hand bone the food plate is parented to. Mirrors CarryFoodInteraction.handAttachPoint on the player. Optional — the carry bool still flips without it.")]
        [SerializeField] private Transform  npcHandAttachPoint;
        [Tooltip("Plate prefab spawned in the NPC's hand at the cafeteria counter. Optional — animation still plays without it.")]
        [SerializeField] private GameObject npcPlatePrefab;

        // ─── Current action state ─────────────────────────────────────────────
        public bool IsNavigating => _current != null || _sequenceSteps != null;
        public bool IsEmergent => _isPlayingEmergent;

        // True once this NPC has received at least one assignment. From that
        // point on, NavMesh is the single source of truth for its position —
        // NPCNetworkSync should stop lerping toward stale backend targets.
        public bool IsBehaviorDriven => _hasEverReceivedAssignment;

        // True while a SitInteraction on this NPC is active (settled OR standing up).
        public bool IsSitting
        {
            get
            {
                return TryGetComponent<SitInteraction>(out var sit) && sit.IsSitting;
            }
        }

        private bool _hasEverReceivedAssignment;

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

        // True while we are holding the next step because the NPC still needs
        // to finish the stand-up animation before walking anywhere else.
        private bool   _waitingForStandUp;

        // True while we are holding the next step because the NavMeshAgent
        // is not yet (re)registered on the NavMesh. Happens the frame after
        // SitInteraction.StandUpRoutine clears isSitting but before it
        // re-enables the agent — if BeginStep fires in that window, the
        // destination would silently short-circuit to "already arrived".
        private bool   _waitingForAgent;

        // Pending reassign received while NPC is mid-LOOPING cycle
        private NPCAssignmentData _pendingAssignment;
        private float  _loopingGraceTimer;
        private const float LoopingGrace = 5f;

        // True if the current assignment was determined to be a run action
        private bool _isRunning;
        private int _runWalkTransitionMode; // 0=None, 1=Walk->Run, 2=Run->Walk
        private float _initialDistance;
        private float _baseSpeedVariance;
        private float _baseWalkMult;

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

        private void UpdateMovementTransition()
        {
            if (_runWalkTransitionMode != 0 && agent != null && agent.isOnNavMesh && !agent.pathPending && agent.hasPath)
            {
                if (_initialDistance <= 0f)
                {
                    _initialDistance = agent.remainingDistance;
                }
                else if (agent.remainingDistance > 0.1f && agent.remainingDistance < _initialDistance * 0.5f)
                {
                    // We crossed the halfway mark
                    _isRunning = (_runWalkTransitionMode == 1);
                    _runWalkTransitionMode = 0; // Transition complete
                    UpdateSpeedMultiplier();
                    PlayAnimation(_isRunning ? "run" : "walk");
                }
            }
        }

        private void Update()
        {
            UpdateMovementTransition();

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
            _hasEverReceivedAssignment = true;

            if (_isLooping && _current != null && data.actionSequence == null)
            {
                _pendingAssignment = data;
                _loopingGraceTimer = LoopingGrace;
                return;
            }

            ApplyAssignment(data);
        }

        public void ApplyMoodHint(string animHint)
        {
            if (!IsNavigating && (_current == null || _hasArrived))
            {
                if (TryGetComponent<SitInteraction>(out var sit) && sit.IsSitting) return;
                PlayAnimation(animHint);
            }
        }

        // ─── Private: Apply ───────────────────────────────────────────────────

        private void ApplyAssignment(NPCAssignmentData data)
        {
            string newAnim = data.actionSequence != null && data.actionSequence.Length > 0 ? data.actionSequence[0].animTrigger : data.animTrigger;
            string newZone = data.actionSequence != null && data.actionSequence.Length > 0 ? data.actionSequence[0].zoneId : data.zoneId;

            if (TryGetComponent<SitInteraction>(out var sit) && sit.IsSitting)
            {
                string currZone = _currentStepZoneId ?? _current?.zoneId;
                if (!IsSitAction(newAnim) || newZone != currZone)
                {
                    sit.ForceReset();
                }
            }

            CleanupCurrent();

            if (agent != null)
            {
                _baseWalkMult = data.walkSpeedMult > 0 ? data.walkSpeedMult : 1.0f;
                
                int uniqueSeed = (int)data.seed ^ gameObject.name.GetHashCode();
                System.Random rnd = new System.Random(uniqueSeed);
                
                double choice = rnd.NextDouble();
                if (choice < 0.6) {
                    _isRunning = false; 
                    _runWalkTransitionMode = 0; // 60% just walk
                } else if (choice < 0.8) {
                    _isRunning = true;
                    _runWalkTransitionMode = 0; // 20% just run
                } else if (choice < 0.9) {
                    _isRunning = false;
                    _runWalkTransitionMode = 1; // 10% walk then run
                } else {
                    _isRunning = true;
                    _runWalkTransitionMode = 2; // 10% run then walk
                }
                
                _baseSpeedVariance = 0.85f + (float)rnd.NextDouble() * 0.30f;
                UpdateSpeedMultiplier();
            }

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
            _waitingForStandUp = false;
            _waitingForAgent = false;
            _isRunning = false;
            _runWalkTransitionMode = 0;
            _initialDistance = 0f;
        }

        private void UpdateSpeedMultiplier()
        {
            if (agent != null)
            {
                float runMultiplier = _isRunning ? 1.8f : 1.0f;
                agent.speed = 3.5f * _baseWalkMult * _baseSpeedVariance * runMultiplier;
            }
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
                _initialDistance = 0f;
                PlayAnimation(_isRunning ? "run" : "walk");
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

            // If we are still seated entering a new sequence (e.g., ApplyAssignment
            // decided to keep us seated for a sit-continuation), only stand up
            // when the first step is actually a non-sit action.
            var first = _sequenceSteps[0];
            if (TryGetComponent<SitInteraction>(out var sit) && sit.IsSitting && !IsSitAction(first.animTrigger))
            {
                sit.TryStandUp();
                _waitingForStandUp = true;
                return;
            }

            BeginStep(first);
        }

        private void UpdateSequence()
        {
            if (_sequenceSteps == null || _sequenceIndex >= _sequenceSteps.Length)
            {
                OnSequenceComplete();
                return;
            }

            var step = _sequenceSteps[_sequenceIndex];

            // Holding the step until the stand-up animation fully finishes
            // so the NavMeshAgent is back online before we try to move.
            if (_waitingForStandUp)
            {
                bool stillSitting = TryGetComponent<SitInteraction>(out var sit) && sit.IsSitting;
                if (stillSitting) return;

                _waitingForStandUp = false;
                BeginStep(step);
                return;
            }

            // Holding the step until the NavMeshAgent is registered on the
            // NavMesh again (covers the 1-frame gap after sit→stand re-enables it).
            if (_waitingForAgent)
            {
                if (agent == null || !agent.isOnNavMesh) return;
                _waitingForAgent = false;
                BeginStep(step);
                return;
            }

            if (!_stepArrived)
            {
                if (agent != null && agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance < arrivalThreshold)
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
                if (IsSitAction(step.animTrigger) || IsWalkToSeatAction(step.actionId))
                {
                    var sitPoint = zoneRegistry.GetDeterministicSitPoint(step.zoneId, step.seed);
                    if (sitPoint != null) { destination = sitPoint.transform.position; destSrc = "sit_point"; }
                }

                if (!destination.HasValue && (IsTakeFoodAction(step.animTrigger) || IsWalkToCounterAction(step.actionId)))
                {
                    var counter = zoneRegistry.GetDeterministicFoodCounter(step.zoneId, step.seed);
                    if (counter != null) { destination = counter.transform.position; destSrc = "food_counter"; }
                }

                if (!destination.HasValue && (IsLeaveFoodAction(step.animTrigger) || IsWalkToSinkAction(step.actionId)))
                {
                    var sink = zoneRegistry.GetDeterministicSink(step.zoneId, step.seed);
                    if (sink != null) { destination = sink.transform.position; destSrc = "sink"; }
                }

                if (!destination.HasValue)
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
            }

            if (!destination.HasValue && !string.IsNullOrEmpty(step.socialPartnerId) && _resolvePartnerTransform != null)
            {
                var partnerTransform = _resolvePartnerTransform(step.socialPartnerId);
                if (partnerTransform != null) { destination = partnerTransform.position; destSrc = "partner"; }
            }

            Debug.Log($"[NPC-CTRL] {name} BeginStep {_sequenceIndex + 1}/{_sequenceSteps.Length} action={step.actionId} anim={step.animTrigger} zone={step.zoneId ?? "-"} dur={step.duration:F1}s dest={destSrc}");

            if (destination.HasValue)
            {
                if (agent != null && agent.isOnNavMesh)
                {
                    float dist = Vector3.Distance(transform.position, destination.Value);
                    if (dist > arrivalThreshold)
                    {
                        agent.SetDestination(destination.Value);
                        _initialDistance = 0f;
                        PlayAnimation(_isRunning ? "run" : "walk");
                        return;
                    }
                }
                else
                {
                    // Destination is known but the agent is not ready yet
                    // (e.g., NavMeshAgent still being re-enabled after stand-up).
                    // Hold the step; UpdateSequence will retry next frame.
                    _waitingForAgent = true;
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
                HandleArrivalAnimation(step.animTrigger, step.zoneId, step.seed);
            }
            else AdvanceSequence();
        }

        private void AdvanceSequence()
        {
            _sequenceIndex++;
            if (_sequenceIndex >= _sequenceSteps.Length)
            {
                HandleEndOfSequenceStandUp();
                OnSequenceComplete();
                return;
            }

            var nextStep = _sequenceSteps[_sequenceIndex];

            // If the NPC is still sitting from the previous step, make sure it
            // finishes the stand-up animation BEFORE we kick off the next step.
            // Skip only when the next step is another sit at the exact same zone
            // (same chair) — in that case the animator just cross-fades in place.
            if (TryGetComponent<SitInteraction>(out var sit) && sit.IsSitting)
            {
                bool nextIsSitSameZone = IsSitAction(nextStep.animTrigger)
                                         && !string.IsNullOrEmpty(nextStep.zoneId)
                                         && nextStep.zoneId == _currentStepZoneId;

                if (!nextIsSitSameZone)
                {
                    sit.TryStandUp();
                    _waitingForStandUp = true;
                    return;
                }
            }

            BeginStep(nextStep);
        }

        private void HandleEndOfSequenceStandUp()
        {
            if (TryGetComponent<SitInteraction>(out var sit) && sit.IsSitting)
                sit.TryStandUp();
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
                        _initialDistance = 0f;
                        PlayAnimation(_isRunning ? "run" : "walk");
                        return;
                    }
                }
            }

            agent.ResetPath();
            agent.velocity = Vector3.zero;
            HandleArrivalAnimation(_current.animTrigger, _current.zoneId, _current.seed);
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

            if ((IsSitAction(data.animTrigger) || IsWalkToSeatAction(data.actionId)) && !string.IsNullOrEmpty(data.zoneId))
            {
                var sitPoint = zoneRegistry.GetDeterministicSitPoint(data.zoneId, data.seed);
                if (sitPoint != null) return sitPoint.transform.position;
            }

            if ((IsTakeFoodAction(data.animTrigger) || IsWalkToCounterAction(data.actionId)) && !string.IsNullOrEmpty(data.zoneId))
            {
                var counter = zoneRegistry.GetDeterministicFoodCounter(data.zoneId, data.seed);
                if (counter != null) return counter.transform.position;
            }

            if ((IsLeaveFoodAction(data.animTrigger) || IsWalkToSinkAction(data.actionId)) && !string.IsNullOrEmpty(data.zoneId))
            {
                var sink = zoneRegistry.GetDeterministicSink(data.zoneId, data.seed);
                if (sink != null) return sink.transform.position;
            }

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

        private bool IsSitAction(string trigger)
        {
            if (string.IsNullOrEmpty(trigger)) return false;
            return trigger.StartsWith("sit_") || trigger == "read_book";
        }

        private bool IsWalkToSeatAction(string actionId)
        {
            return actionId != null && actionId.Contains("walk_to_seat");
        }

        private bool IsTakeFoodAction(string trigger)
        {
            return trigger == "serve_self";
        }

        private bool IsLeaveFoodAction(string trigger)
        {
            return trigger == "deposit_tray";
        }

        private bool IsWalkToCounterAction(string actionId)
        {
            return actionId != null && (actionId.Contains("walk_to_counter") || actionId == "cafe_grab_food");
        }

        private bool IsWalkToSinkAction(string actionId)
        {
            return actionId != null && (actionId.Contains("walk_to_trash") || actionId == "cafe_clear_tray");
        }

        private void HandleArrivalAnimation(string animTrigger, string zoneId, uint seed)
        {
            if (IsTakeFoodAction(animTrigger))
            {
                var carry = EnsureCarryFoodInteraction();
                if (carry != null && !carry.IsCarrying)
                {
                    carry.TryPickUp();
                    return;
                }
            }

            if (IsLeaveFoodAction(animTrigger))
            {
                var carry = GetComponent<CarryFoodInteraction>();
                if (carry != null && carry.IsCarrying)
                    carry.TryDrop();
                PlayAnimation(animTrigger);
                return;
            }

            if (IsSitAction(animTrigger) && !string.IsNullOrEmpty(zoneId) && zoneRegistry != null)
            {
                var sitPoint = zoneRegistry.GetDeterministicSitPoint(zoneId, seed);
                if (sitPoint != null)
                {
                    if (TryGetComponent<SitInteraction>(out var sit) && sit.IsSitting)
                    {
                        sit.stateSitDown = MapTriggerToStateName(animTrigger);
                        if (sit.animator != null && sit.animator.HasState(0, Animator.StringToHash(sit.stateSitDown)))
                            sit.animator.CrossFade(sit.stateSitDown, 0.25f);
                        return;
                    }

                    if (sit == null)
                    {
                        sit = gameObject.AddComponent<SitInteraction>();
                        sit.animator = animator;
                        sit.navMeshAgent = agent;
                        sit.stateSitDown = MapTriggerToStateName(animTrigger);
                        sit.stateStandUp = "Idle";
                    }
                    else
                    {
                        sit.stateSitDown = MapTriggerToStateName(animTrigger);
                    }
                    
                    sit.TrySitDown(sitPoint);
                    return;
                }
            }

            PlayAnimation(animTrigger);
        }

        // ─── Food Carry Setup ──────────────────────────────────────────────────
        // Auto-add a CarryFoodInteraction to the NPC the first time it reaches
        // the cafeteria counter, mirroring how SitInteraction is added on demand.
        // The plate visual still shows up only if npcHandAttachPoint + npcPlatePrefab
        // are wired in the prefab inspector — without them the carry bool flips
        // but no plate prop is spawned (CarryFoodInteraction handles this gracefully).
        private CarryFoodInteraction EnsureCarryFoodInteraction()
        {
            var carry = GetComponent<CarryFoodInteraction>();
            if (carry == null)
            {
                carry = gameObject.AddComponent<CarryFoodInteraction>();
            }

            if (carry.handAttachPoint == null && npcHandAttachPoint != null)
                carry.handAttachPoint = npcHandAttachPoint;
            if (carry.platePrefab == null && npcPlatePrefab != null)
                carry.platePrefab = npcPlatePrefab;

            return carry;
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
                "run"              => "Running",
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
