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

        [Header("Laundry Grab Clothes (used when this NPC rummages a pile)")]
        [Tooltip("Hand bone the clothes bundle is parented to. Mirrors CarryClothesInteraction.handAttachPoint on the player. Optional — the carry bool still flips without it. Defaults to npcHandAttachPoint if left empty.")]
        [SerializeField] private Transform  npcClothesHandAttachPoint;
        [Tooltip("Clothes bundle prefab spawned in the NPC's hand when the grab loop completes. Optional — animation still plays without it.")]
        [SerializeField] private GameObject npcClothesPrefab;
        [Tooltip("Local-space offset applied to the clothes prefab once parented to the hand.")]
        [SerializeField] private Vector3    npcClothesLocalPosition = new Vector3(-0.324f, 0f, 0.148f);
        [SerializeField] private Vector3    npcClothesLocalEuler    = new Vector3(90f, -22.58f, 0f);
        [SerializeField] private Vector3    npcClothesLocalScale    = new Vector3(30f, 30f, 30f);

        [Header("Laundry Load Washer (used when this NPC loads the washing machine)")]
        [Tooltip("Folded clothes bundle prefab spawned in the NPC's hand when the washer loop completes. Optional.")]
        [SerializeField] private GameObject npcFoldedClothesPrefab;
        [Tooltip("Local-space offset applied to the folded clothes prefab.")]
        [SerializeField] private Vector3    npcFoldedLocalPosition = new Vector3(0.043f, -0.063f, 0.04f); // Typical offsets
        [SerializeField] private Vector3    npcFoldedLocalEuler    = new Vector3(-132.86f, -107.57f, 62.47f);
        [SerializeField] private Vector3    npcFoldedLocalScale    = new Vector3(28f, 28f, 28f);

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

        // Desk reserved for the current work step. Set at destination resolution
        // (BeginStep / ResolveFirstDestination) so a second NPC resolving the
        // same zone+seed gets the NEXT free desk. Released on work-stop,
        // reassignment, or destroy.
        private WorkTableInteractable _reservedWorkTable;

        // Cabinet reserved for the current inspect step. Mirrors _reservedWorkTable:
        // set at destination resolution so a second NPC resolving the same zone+seed
        // gets the NEXT free cabinet. Released on inspect-stop, reassignment, or destroy.
        private WorkInspectCabinetsInteractable _reservedInspectCabinet;

        // Laundry pile reserved for the current grab-clothes step. Same reservation
        // pattern as the work-table / inspect-cabinet slots above.
        private LaundryGrabClothesInteractable _reservedLaundryGrab;

        // Washing machine reserved for the current load-washer step.
        private LaundryLoadWasherInteractable _reservedLaundryWasher;

        // Shelf reserved for the current store-clothes step.
        private LaundryStoreClothesInteractable _reservedLaundryStore;

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

            if (!_hasArrived
                && agent != null && agent.enabled && agent.isOnNavMesh
                && !agent.pathPending && agent.remainingDistance < arrivalThreshold)
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

            // If a new assignment comes in while we're walking to / working
            // at a desk, stop + release the reservation unless the new step
            // continues at the same desk zone. Covers both the "mid-work"
            // and the "mid-walk (desk reserved, not yet working)" cases.
            bool continuingSameWork = IsWorkTableAction(newAnim)
                                      && newZone == (_currentStepZoneId ?? _current?.zoneId);
            if (!continuingSameWork) StopWorkTableIfActive();

            bool continuingSameInspect = IsInspectCabinetAction(newAnim)
                                         && newZone == (_currentStepZoneId ?? _current?.zoneId);
            if (!continuingSameInspect) StopInspectCabinetIfActive();

            // Release the pile reservation whenever we switch to a different zone,
            // so the next NPC assigned to zone_laundry_pile can claim this slot.
            // CarryClothesInteraction runs its own coroutine — no need to stop it.
            string newActionId = data.actionSequence != null && data.actionSequence.Length > 0
                ? data.actionSequence[0].actionId
                : data.actionId;
            bool continuingSameGrab = IsLaundryGrabClothesAction(newActionId)
                                      && newZone == (_currentStepZoneId ?? _current?.zoneId);
            if (!continuingSameGrab) ReleaseLaundryGrabClothes();

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

                if (!destination.HasValue && IsWorkTableAction(step.animTrigger))
                {
                    var workTable = ReserveWorkTable(step.zoneId, step.seed);
                    if (workTable != null)
                    {
                        destination = ResolveWorkTableDestination(workTable);
                        destSrc = "work_table";
                    }
                    else
                    {
                        destSrc = "work_table_all_busy_fallback";
                    }
                }

                if (!destination.HasValue && IsInspectCabinetAction(step.animTrigger))
                {
                    var cabinet = ReserveInspectCabinet(step.zoneId, step.seed);
                    if (cabinet != null)
                    {
                        destination = ResolveInspectCabinetDestination(cabinet);
                        destSrc = "inspect_cabinet";
                    }
                    else
                    {
                        destSrc = "inspect_cabinet_all_busy_fallback";
                    }
                }

                if (!destination.HasValue && IsLaundryGrabClothesAction(step.actionId))
                {
                    var pile = ReserveLaundryGrabClothes(step.zoneId, step.seed);
                    if (pile != null)
                    {
                        destination = ResolveLaundryGrabClothesDestination(pile);
                        destSrc = "laundry_pile";
                    }
                    else
                    {
                        destSrc = "laundry_pile_all_busy_fallback";
                    }
                }

                if (!destination.HasValue && IsLaundryLoadWasherAction(step.actionId))
                {
                    var washer = ReserveLaundryWasher(step.zoneId, step.seed);
                    if (washer != null)
                    {
                        destination = ResolveLaundryWasherDestination(washer);
                        destSrc = "laundry_washer";
                    }
                    else
                    {
                        destSrc = "laundry_washer_all_busy_fallback";
                    }
                }

                if (!destination.HasValue && IsLaundryStoreClothesAction(step.actionId))
                {
                    var shelf = ReserveLaundryStore(step.zoneId, step.seed);
                    if (shelf != null)
                    {
                        destination = ResolveLaundryStoreDestination(shelf);
                        destSrc = "laundry_shelf";
                    }
                    else
                    {
                        destSrc = "laundry_shelf_all_busy_fallback";
                    }
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
                HandleArrivalAnimation(step.animTrigger, step.zoneId, step.seed, step.actionId);
            }
            else AdvanceSequence();
        }

        private void AdvanceSequence()
        {
            // If we were working / reserved on a desk during this step, stop
            // the loop + release the reservation so the NavMeshAgent comes
            // back online and another NPC can claim the desk. Skip when the
            // next step is another work action at the same zone — we keep
            // looping + reservation in place.
            {
                int peekIndex = _sequenceIndex + 1;
                bool nextIsWorkSameZone = peekIndex < _sequenceSteps.Length
                                          && IsWorkTableAction(_sequenceSteps[peekIndex].animTrigger)
                                          && !string.IsNullOrEmpty(_sequenceSteps[peekIndex].zoneId)
                                          && _sequenceSteps[peekIndex].zoneId == _currentStepZoneId;
                if (!nextIsWorkSameZone) StopWorkTableIfActive();

                bool nextIsInspectSameZone = peekIndex < _sequenceSteps.Length
                                             && IsInspectCabinetAction(_sequenceSteps[peekIndex].animTrigger)
                                             && !string.IsNullOrEmpty(_sequenceSteps[peekIndex].zoneId)
                                             && _sequenceSteps[peekIndex].zoneId == _currentStepZoneId;
                if (!nextIsInspectSameZone) StopInspectCabinetIfActive();

                bool nextIsWasherSameZone = peekIndex < _sequenceSteps.Length
                                             && IsLaundryLoadWasherAction(_sequenceSteps[peekIndex].actionId)
                                             && !string.IsNullOrEmpty(_sequenceSteps[peekIndex].zoneId)
                                             && _sequenceSteps[peekIndex].zoneId == _currentStepZoneId;
                if (!nextIsWasherSameZone) StopLaundryWasherIfActive();

                bool nextIsStoreSameZone = peekIndex < _sequenceSteps.Length
                                             && IsLaundryStoreClothesAction(_sequenceSteps[peekIndex].actionId)
                                             && !string.IsNullOrEmpty(_sequenceSteps[peekIndex].zoneId)
                                             && _sequenceSteps[peekIndex].zoneId == _currentStepZoneId;
                if (!nextIsStoreSameZone) StopLaundryStoreIfActive();

                // CarryClothesInteraction's grab routine self-terminates when its
                // own duration elapses (isSearching=false → bundle attached +
                // isCarrying=true). Nothing to stop here. Release the pile slot
                // so another NPC can pick it up on the next routine.
                ReleaseLaundryGrabClothes();
            }

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

            StopWorkTableIfActive();
            StopInspectCabinetIfActive();
            StopLaundryWasherIfActive();
            StopLaundryStoreIfActive();
            ReleaseLaundryGrabClothes();
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
            HandleArrivalAnimation(_current.animTrigger, _current.zoneId, _current.seed, _current.actionId);
        }

        private void OnActionComplete()
        {
            StopWorkTableIfActive();
            StopLaundryWasherIfActive();
            StopLaundryStoreIfActive();
            ReleaseLaundryGrabClothes();

            _isLooping = false;
            _current   = null;
            if (agent != null && agent.isOnNavMesh)
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

            if (IsWorkTableAction(data.animTrigger) && !string.IsNullOrEmpty(data.zoneId))
            {
                var workTable = ReserveWorkTable(data.zoneId, data.seed);
                if (workTable != null) return ResolveWorkTableDestination(workTable);
                // No desk free → fall through to zone point lookups, so the NPC
                // still walks SOMEWHERE in the workshop and the arrival handler
                // can apply the talk/idle fallback on arrival.
            }

            if (IsInspectCabinetAction(data.animTrigger) && !string.IsNullOrEmpty(data.zoneId))
            {
                var cabinet = ReserveInspectCabinet(data.zoneId, data.seed);
                if (cabinet != null) return ResolveInspectCabinetDestination(cabinet);
                // No cabinet free → fall through to zone point lookups.
            }

            if (IsLaundryGrabClothesAction(data.actionId) && !string.IsNullOrEmpty(data.zoneId))
            {
                var pile = ReserveLaundryGrabClothes(data.zoneId, data.seed);
                if (pile != null) return ResolveLaundryGrabClothesDestination(pile);
                // No pile free → fall through to zone point lookups.
            }

            if (IsLaundryLoadWasherAction(data.actionId) && !string.IsNullOrEmpty(data.zoneId))
            {
                var washer = ReserveLaundryWasher(data.zoneId, data.seed);
                if (washer != null) return ResolveLaundryWasherDestination(washer);
                // No washer free → fall through to zone point lookups.
            }

            if (IsLaundryStoreClothesAction(data.actionId) && !string.IsNullOrEmpty(data.zoneId))
            {
                var shelf = ReserveLaundryStore(data.zoneId, data.seed);
                if (shelf != null) return ResolveLaundryStoreDestination(shelf);
                // No shelf free → fall through to zone point lookups.
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

        private bool IsWorkTableAction(string trigger)
        {
            return trigger == "work_bench";
        }

        private bool IsInspectCabinetAction(string trigger)
        {
            return trigger == "inspect";
        }

        // Keyed on actionId (not animTrigger) because the laundry pile shares
        // the "rummaging" animTrigger with serve_self and other inspect-style actions.
        private bool IsLaundryGrabClothesAction(string actionId)
        {
            return actionId == "laundry_grab_clothes";
        }

        private bool IsLaundryLoadWasherAction(string actionId)
        {
            return actionId == "laundry_load_washer";
        }

        private bool IsLaundryStoreClothesAction(string actionId)
        {
            return actionId == "laundry_store_clothes";
        }

        // Desks use the ProgressPointAction.actionPoint as the stand-at spot.
        // Prefer that Transform so the walk destination lines up exactly with
        // where the NPC will be snapped when starting the work loop.
        private Vector3 ResolveWorkTableDestination(WorkTableInteractable workTable)
        {
            var pp = workTable.GetComponent<ProgressPointAction>();
            var anchor = (pp != null && pp.actionPoint != null) ? pp.actionPoint : workTable.transform;
            return anchor.position;
        }

        // Cabinets use the same ProgressPointAction.actionPoint pattern as desks.
        private Vector3 ResolveInspectCabinetDestination(WorkInspectCabinetsInteractable cabinet)
        {
            var pp = cabinet.GetComponent<ProgressPointAction>();
            var anchor = (pp != null && pp.actionPoint != null) ? pp.actionPoint : cabinet.transform;
            return anchor.position;
        }

        private void HandleArrivalAnimation(string animTrigger, string zoneId, uint seed, string actionId = null)
        {
            if (IsLaundryGrabClothesAction(actionId) && !string.IsNullOrEmpty(zoneId) && zoneRegistry != null)
            {
                // Only play the grab if we actually reserved a pile during destination
                // resolution. The fallback path (all piles busy, or no pile in zone)
                // lands the NPC on a random zone point — without this gate those NPCs
                // would rummage mid-air far from any pile.
                if (_reservedLaundryGrab == null)
                {
                    Debug.Log($"[NPC-CTRL] {name} laundry grab without reserved pile in '{zoneId}' — falling back to look_around");
                    PlayAnimation("look_around");
                    return;
                }

                // NavMeshAgent stops at the nearest navmesh point to actionPoint, which
                // can sit 0.3–1m short of the pile. Warp exactly onto actionPoint
                // (xz) and face its yaw so the NPC rummages at the pile, not near it.
                // Warp keeps the agent enabled — no snap/lock like the worktable path.
                var pp = _reservedLaundryGrab.GetComponent<ProgressPointAction>();
                var anchor = (pp != null && pp.actionPoint != null)
                    ? pp.actionPoint
                    : _reservedLaundryGrab.transform;

                if (agent != null && agent.isOnNavMesh)
                {
                    Vector3 warpTarget = new Vector3(anchor.position.x, transform.position.y, anchor.position.z);
                    agent.Warp(warpTarget);
                }
                transform.rotation = Quaternion.Euler(0f, anchor.eulerAngles.y, 0f);

                float grabDuration = (_sequenceSteps != null && _sequenceIndex < _sequenceSteps.Length)
                    ? _sequenceSteps[_sequenceIndex].duration
                    : (_current != null ? _current.duration : 3f);

                var clothes = EnsureCarryClothesInteraction();
                if (clothes != null && !clothes.IsCarrying)
                {
                    clothes.TryPickUpWithGrabAnimation(grabDuration);
                    return;
                }
            }

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

            if (IsWorkTableAction(animTrigger) && !string.IsNullOrEmpty(zoneId) && zoneRegistry != null)
            {
                // Prefer the desk we already reserved during destination
                // resolution; only re-reserve if for some reason we don't
                // have one yet (e.g., single-action path). This prevents
                // picking a *different* desk than the one we walked to.
                var workTable = _reservedWorkTable ?? ReserveWorkTable(zoneId, seed);
                if (workTable != null)
                {
                    var pp = workTable.GetComponent<ProgressPointAction>();
                    var anchor = (pp != null && pp.actionPoint != null) ? pp.actionPoint : workTable.transform;

                    var work = EnsureWorkTableInteraction();
                    work.TryStartWork(anchor);
                    return;
                }

                // Every desk in this zone is taken → fallback so the NPC
                // doesn't just stand silent: play a standing-talk idle in
                // place for the remainder of the step.
                Debug.Log($"[NPC-CTRL] {name} all desks in '{zoneId}' occupied — falling back to talk_standing");
                PlayAnimation("talk_standing");
                return;
            }

            if (IsLaundryLoadWasherAction(actionId) && !string.IsNullOrEmpty(zoneId) && zoneRegistry != null)
            {
                var washer = _reservedLaundryWasher ?? ReserveLaundryWasher(zoneId, seed);
                if (washer != null)
                {
                    var pp = washer.GetComponent<ProgressPointAction>();
                    var anchor = (pp != null && pp.actionPoint != null) ? pp.actionPoint : washer.transform;

                    var load = EnsureLaundryLoadWasherInteraction();
                    load.TryStartWork(anchor);
                    return;
                }

                Debug.Log($"[NPC-CTRL] {name} all washers in '{zoneId}' occupied — falling back to look_around");
                PlayAnimation("look_around");
                return;
            }

            if (IsLaundryStoreClothesAction(actionId) && !string.IsNullOrEmpty(zoneId) && zoneRegistry != null)
            {
                var shelf = _reservedLaundryStore ?? ReserveLaundryStore(zoneId, seed);
                if (shelf != null)
                {
                    var pp = shelf.GetComponent<ProgressPointAction>();
                    var anchor = (pp != null && pp.actionPoint != null) ? pp.actionPoint : shelf.transform;

                    var store = EnsureLaundryStoreClothesInteraction();
                    store.TryStartWork(anchor);
                    return;
                }

                Debug.Log($"[NPC-CTRL] {name} all shelves in '{zoneId}' occupied — falling back to look_around");
                PlayAnimation("look_around");
                return;
            }

            if (IsInspectCabinetAction(animTrigger) && !string.IsNullOrEmpty(zoneId) && zoneRegistry != null)
            {
                var cabinet = _reservedInspectCabinet ?? ReserveInspectCabinet(zoneId, seed);
                if (cabinet != null)
                {
                    var pp = cabinet.GetComponent<ProgressPointAction>();
                    var anchor = (pp != null && pp.actionPoint != null) ? pp.actionPoint : cabinet.transform;

                    var inspect = EnsureWorkInspectCabinetsInteraction();
                    inspect.TryStartInspect(anchor);
                    return;
                }

                Debug.Log($"[NPC-CTRL] {name} all cabinets in '{zoneId}' occupied — falling back to look_around");
                PlayAnimation("look_around");
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

        // ─── Work Table Setup ─────────────────────────────────────────────────
        // Auto-add a WorkTableInteraction on the NPC the first time it reaches
        // a workshop desk, mirroring how SitInteraction is added on demand.
        // The character-side component owns the snap + animator bool + agent
        // toggle. It intentionally does NOT drive the progress bar or
        // broadcast player:action events — those belong to the local player's
        // WorkTableInteractable flow. NPCs are synced via NPCNetworkSync.
        private WorkTableInteraction EnsureWorkTableInteraction()
        {
            var work = GetComponent<WorkTableInteraction>();
            if (work == null)
                work = gameObject.AddComponent<WorkTableInteraction>();

            if (work.animator == null)     work.animator     = animator;
            if (work.navMeshAgent == null) work.navMeshAgent = agent;

            return work;
        }

        private void StopWorkTableIfActive()
        {
            if (TryGetComponent<WorkTableInteraction>(out var work) && work.IsWorking)
                work.TryStopWork();

            ReleaseWorkTable();
        }

        /// <summary>
        /// Reserves a free desk inside the given zone for this NPC. Marks it
        /// occupied immediately so other NPCs resolving the same zone+seed
        /// will walk to a different desk. Idempotent: if we already have a
        /// reservation, return it.
        /// </summary>
        private WorkTableInteractable ReserveWorkTable(string zoneId, uint seed)
        {
            if (_reservedWorkTable != null) return _reservedWorkTable;
            if (zoneRegistry == null) return null;

            var table = zoneRegistry.GetDeterministicWorkTable(zoneId, seed);
            if (table == null) return null;

            table.isOccupied = true;
            _reservedWorkTable = table;
            return table;
        }

        private void ReleaseWorkTable()
        {
            if (_reservedWorkTable != null)
            {
                _reservedWorkTable.isOccupied = false;
                _reservedWorkTable = null;
            }
        }

        // ─── Inspect Cabinet Setup ───────────────────────────────────────────
        // Auto-add a WorkInspectCabinetsInteraction on the NPC the first time
        // it reaches a cabinet, mirroring the worktable / sit on-demand pattern.

        private WorkInspectCabinetsInteraction EnsureWorkInspectCabinetsInteraction()
        {
            var inspect = GetComponent<WorkInspectCabinetsInteraction>();
            if (inspect == null)
                inspect = gameObject.AddComponent<WorkInspectCabinetsInteraction>();

            if (inspect.animator == null)     inspect.animator     = animator;
            if (inspect.navMeshAgent == null) inspect.navMeshAgent = agent;

            return inspect;
        }

        private void StopInspectCabinetIfActive()
        {
            if (TryGetComponent<WorkInspectCabinetsInteraction>(out var inspect) && inspect.IsInspecting)
                inspect.TryStopInspect();

            ReleaseInspectCabinet();
        }

        /// <summary>
        /// Reserves a free cabinet inside the given zone for this NPC. Marks it
        /// occupied immediately so other NPCs resolving the same zone+seed
        /// walk to a different cabinet. Idempotent.
        /// </summary>
        private WorkInspectCabinetsInteractable ReserveInspectCabinet(string zoneId, uint seed)
        {
            if (_reservedInspectCabinet != null) return _reservedInspectCabinet;
            if (zoneRegistry == null) return null;

            var cabinet = zoneRegistry.GetDeterministicInspectCabinet(zoneId, seed);
            if (cabinet == null) return null;

            cabinet.isOccupied = true;
            _reservedInspectCabinet = cabinet;
            return cabinet;
        }

        private void ReleaseInspectCabinet()
        {
            if (_reservedInspectCabinet != null)
            {
                _reservedInspectCabinet.isOccupied = false;
                _reservedInspectCabinet = null;
            }
        }

        // ─── Laundry Grab Clothes Setup ──────────────────────────────────────
        // No character-side helper component — the NPC plays the rummaging
        // animation in place via CarryClothesInteraction.TryPickUpWithGrabAnimation,
        // which mirrors CarryFoodInteraction.TryPickUp (trigger+delay, no snap /
        // agent disable). The pile reservation below is still useful so multiple
        // NPCs routed to the same zone+seed pick different piles.

        private LaundryGrabClothesInteractable ReserveLaundryGrabClothes(string zoneId, uint seed)
        {
            if (_reservedLaundryGrab != null) return _reservedLaundryGrab;
            if (zoneRegistry == null) return null;

            var pile = zoneRegistry.GetDeterministicLaundryGrabClothes(zoneId, seed);
            if (pile == null) return null;

            pile.isOccupied = true;
            _reservedLaundryGrab = pile;
            return pile;
        }

        private void ReleaseLaundryGrabClothes()
        {
            if (_reservedLaundryGrab != null)
            {
                _reservedLaundryGrab.isOccupied = false;
                _reservedLaundryGrab = null;
            }
        }

        private Vector3 ResolveLaundryGrabClothesDestination(LaundryGrabClothesInteractable pile)
        {
            var pp = pile.GetComponent<ProgressPointAction>();
            var anchor = (pp != null && pp.actionPoint != null) ? pp.actionPoint : pile.transform;
            return anchor.position;
        }

        // ─── Laundry Load Washer Setup ───────────────────────────────────────

        private LaundryLoadWasherInteraction EnsureLaundryLoadWasherInteraction()
        {
            var load = GetComponent<LaundryLoadWasherInteraction>();
            if (load == null)
                load = gameObject.AddComponent<LaundryLoadWasherInteraction>();

            if (load.animator == null)     load.animator     = animator;
            if (load.navMeshAgent == null) load.navMeshAgent = agent;

            // Also ensure we have CarryFoldedClothesInteraction so we can attach it when done
            EnsureCarryFoldedClothesInteraction();

            return load;
        }

        private void StopLaundryWasherIfActive()
        {
            if (TryGetComponent<LaundryLoadWasherInteraction>(out var load) && load.IsWorking)
                load.TryStopWork();

            ReleaseLaundryWasher();
        }

        private LaundryLoadWasherInteractable ReserveLaundryWasher(string zoneId, uint seed)
        {
            if (_reservedLaundryWasher != null) return _reservedLaundryWasher;
            if (zoneRegistry == null) return null;

            var washer = zoneRegistry.GetDeterministicLaundryLoadWasher(zoneId, seed);
            if (washer == null) return null;

            washer.isOccupied = true;
            _reservedLaundryWasher = washer;
            return washer;
        }

        private void ReleaseLaundryWasher()
        {
            if (_reservedLaundryWasher != null)
            {
                _reservedLaundryWasher.isOccupied = false;
                _reservedLaundryWasher = null;
            }
        }

        private Vector3 ResolveLaundryWasherDestination(LaundryLoadWasherInteractable washer)
        {
            var pp = washer.GetComponent<ProgressPointAction>();
            var anchor = (pp != null && pp.actionPoint != null) ? pp.actionPoint : washer.transform;
            return anchor.position;
        }

        // ─── Laundry Store Clothes Setup ─────────────────────────────────────

        private LaundryStoreClothesInteraction EnsureLaundryStoreClothesInteraction()
        {
            var store = GetComponent<LaundryStoreClothesInteraction>();
            if (store == null)
                store = gameObject.AddComponent<LaundryStoreClothesInteraction>();

            if (store.animator == null)     store.animator     = animator;
            if (store.navMeshAgent == null) store.navMeshAgent = agent;

            // Ensure CarryFoldedClothesInteraction exists so StopWork can ForceReset it
            EnsureCarryFoldedClothesInteraction();

            return store;
        }

        private void StopLaundryStoreIfActive()
        {
            if (TryGetComponent<LaundryStoreClothesInteraction>(out var store) && store.IsWorking)
                store.TryStopWork();

            ReleaseLaundryStore();
        }

        private LaundryStoreClothesInteractable ReserveLaundryStore(string zoneId, uint seed)
        {
            if (_reservedLaundryStore != null) return _reservedLaundryStore;
            if (zoneRegistry == null) return null;

            var shelf = zoneRegistry.GetDeterministicLaundryStoreClothes(zoneId, seed);
            if (shelf == null) return null;

            shelf.isOccupied = true;
            _reservedLaundryStore = shelf;
            return shelf;
        }

        private void ReleaseLaundryStore()
        {
            if (_reservedLaundryStore != null)
            {
                _reservedLaundryStore.isOccupied = false;
                _reservedLaundryStore = null;
            }
        }

        private Vector3 ResolveLaundryStoreDestination(LaundryStoreClothesInteractable shelf)
        {
            var pp = shelf.GetComponent<ProgressPointAction>();
            var anchor = (pp != null && pp.actionPoint != null) ? pp.actionPoint : shelf.transform;
            return anchor.position;
        }

        // ─── Clothes Carry Setup ─────────────────────────────────────────────
        // Auto-add a CarryClothesInteraction to the NPC the first time it grabs
        // a bundle, mirroring the food-carry on-demand setup. The clothes visual
        // only appears if npcClothesPrefab + npcClothesHandAttachPoint (or the
        // shared npcHandAttachPoint as a fallback) are wired in the prefab
        // inspector — without them the carry bool flips but no prop is spawned
        // (CarryClothesInteraction handles this gracefully).
        private CarryClothesInteraction EnsureCarryClothesInteraction()
        {
            var carry = GetComponent<CarryClothesInteraction>();
            if (carry == null)
            {
                carry = gameObject.AddComponent<CarryClothesInteraction>();
            }

            var hand = npcClothesHandAttachPoint != null ? npcClothesHandAttachPoint : npcHandAttachPoint;
            if (carry.handAttachPoint == null && hand != null)
                carry.handAttachPoint = hand;
            if (carry.clothesPrefab == null && npcClothesPrefab != null)
                carry.clothesPrefab = npcClothesPrefab;

            // Apply the prefab-inspector offsets only if no inspector-level
            // CarryClothesInteraction already set them (defaults are Vector3.zero).
            if (carry.clothesLocalPosition == Vector3.zero) carry.clothesLocalPosition = npcClothesLocalPosition;
            if (carry.clothesLocalEuler    == Vector3.zero) carry.clothesLocalEuler    = npcClothesLocalEuler;
            if (carry.clothesLocalScale    == Vector3.one)  carry.clothesLocalScale    = npcClothesLocalScale;

            return carry;
        }

        private CarryFoldedClothesInteraction EnsureCarryFoldedClothesInteraction()
        {
            var carry = GetComponent<CarryFoldedClothesInteraction>();
            if (carry == null)
            {
                carry = gameObject.AddComponent<CarryFoldedClothesInteraction>();
            }

            var hand = npcClothesHandAttachPoint != null ? npcClothesHandAttachPoint : npcHandAttachPoint;
            if (carry.handAttachPoint == null && hand != null)
                carry.handAttachPoint = hand;
            if (carry.foldedClothesPrefab == null && npcFoldedClothesPrefab != null)
                carry.foldedClothesPrefab = npcFoldedClothesPrefab;

            // Apply the prefab-inspector offsets only if no inspector-level
            // CarryFoldedClothesInteraction already set them (defaults are Vector3.zero).
            if (carry.foldedLocalPosition == Vector3.zero) carry.foldedLocalPosition = npcFoldedLocalPosition;
            if (carry.foldedLocalEuler    == Vector3.zero) carry.foldedLocalEuler    = npcFoldedLocalEuler;
            if (carry.foldedLocalScale    == Vector3.one)  carry.foldedLocalScale    = npcFoldedLocalScale;

            return carry;
        }

        private void OnDestroy()
        {
            // Release any reservation so despawned/disconnected NPCs don't
            // leave the desk / cabinet / pile / washer permanently marked as busy.
            ReleaseWorkTable();
            ReleaseInspectCabinet();
            ReleaseLaundryGrabClothes();
            ReleaseLaundryWasher();
            ReleaseLaundryStore();
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
                "rummaging"        => "Rummaging",
                "load_machine"     => "Opening",
                "store_clothes"    => "Opening",
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
