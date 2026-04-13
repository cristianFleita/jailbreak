using UnityEngine;

namespace Jailbreak.Player
{
    /// <summary>
    /// Drives the Animator and procedural model transforms for both local and remote players.
    ///
    /// AAA FPS features implemented:
    ///   - Smooth body lean/rotation toward lateral movement direction
    ///   - Procedural head bob tied to movement state
    ///   - Animator strafe float (-1..1) for blend-tree strafing
    ///   - Landing impulse detection and animation trigger
    ///   - CrossFadeInFixedTime transitions (0.15 s, consistent regardless of clip length)
    ///
    /// Animator contract (parameters this script writes):
    ///   Float  "Strafe"    — -1 (left) to 1 (right), used by blend tree
    ///   Float  "Speed"     — 0..1 normalised speed, optional for additive layers
    ///   Trigger "Land"     — fired on landing after airborne state
    ///
    /// Animator states expected (match your Character.controller):
    ///   "Idle", "Walking", "Running", "Crouch", "CrouchWalk", "Airborne"
    /// </summary>
    public class PlayerAnimationController : MonoBehaviour
    {
        // ── Animator parameter hashes (cached to avoid string lookups every frame) ──
        private static readonly int HashStrafe  = Animator.StringToHash("Strafe");
        private static readonly int HashSpeed   = Animator.StringToHash("Speed");
        private static readonly int HashLand    = Animator.StringToHash("Land");

        // ── Inspector-tunable values (data-driven, never hardcoded) ──────────────
        [Header("Body Rotation (Lateral Lean)")]
        [Tooltip("How fast the mesh rotates to face the movement direction. 4-6 feels natural.")]
        [SerializeField] private float _bodyRotationSpeed = 5f;

        [Header("Animator Smoothing")]
        [Tooltip("Smoothing speed for the Strafe float parameter sent to the Animator.")]
        [SerializeField] private float _strafeBlendSpeed = 8f;

        [Tooltip("Fixed crossfade duration in seconds for state transitions.")]
        [SerializeField] private float _crossfadeDuration = 0.15f;

        [Header("Head Bob (Local Player Only)")]
        [SerializeField] private bool  _headBobEnabled    = true;
        [SerializeField] private float _walkBobFrequency  = 8f;
        [SerializeField] private float _runBobFrequency   = 14f;
        [SerializeField] private float _walkBobAmplitude  = 0.04f;
        [SerializeField] private float _runBobAmplitude   = 0.07f;
        [SerializeField] private float _bobReturnSpeed    = 6f;

        [Tooltip("Assign the camera root transform for head bob. If null, bob is skipped.")]
        [SerializeField] private Transform _cameraRoot;

        [Header("Landing")]
        [Tooltip("Minimum airborne time (seconds) before a landing animation is triggered.")]
        [SerializeField] private float _minAirTimeForLanding = 0.25f;

        // ── Private state ─────────────────────────────────────────────────────────
        private Animator              _animator;
        private PlayerInputController _input;
        private RemotePlayerSync      _remote;
        private CharacterController   _characterController;

        private string _currentAnimState  = "";
        private float  _currentStrafeBlend = 0f;
        private float  _bobTimer           = 0f;
        private Vector3 _camRootLocalOrigin;

        private bool  _wasGrounded        = true;
        private float _airborneTimer      = 0f;

        // Tracks which Animator parameters actually exist at runtime, to avoid warnings
        private bool _hasParamStrafe = false;
        private bool _hasParamSpeed  = false;
        private bool _hasParamLand   = false;

        // ── Unity lifecycle ───────────────────────────────────────────────────────
        private void Awake()
        {
            _animator            = GetComponentInChildren<Animator>();
            _input               = GetComponent<PlayerInputController>();
            _characterController = GetComponent<CharacterController>();

            if (_animator == null)
                Debug.LogWarning($"[AnimController] CRITICAL: No Animator found on '{gameObject.name}' or its children!");
            else
                Debug.Log($"[AnimController] Animator linked for '{gameObject.name}'");

            if (_cameraRoot != null)
                _camRootLocalOrigin = _cameraRoot.localPosition;

            // Cache which parameters actually exist in this Animator controller
            if (_animator != null)
            {
                foreach (AnimatorControllerParameter p in _animator.parameters)
                {
                    if (p.name == "Strafe") _hasParamStrafe = true;
                    if (p.name == "Speed")  _hasParamSpeed  = true;
                    if (p.name == "Land")   _hasParamLand   = true;
                }
            }
        }

        private void Update()
        {
            if (_animator == null) return;

            // Lazy-init for components added dynamically after Awake (e.g. RemotePlayerSync)
            if (_remote == null)
                _remote = GetComponent<RemotePlayerSync>();

            bool isLocal = _input != null;   // Unity's overloaded null check handles destroyed components

            // ── 1. Resolve movement state ─────────────────────────────────────────
            string rawState = ResolveRawMovementState(isLocal);

            // ── 2. Map to Animator state name ─────────────────────────────────────
            bool   isGrounded    = _characterController != null
                                       ? _characterController.isGrounded
                                       : true;
            string targetState   = MapToAnimatorState(rawState, isGrounded);

            // ── 3. Drive Animator float parameters ───────────────────────────────
            float targetStrafe = isLocal ? ResolveLocalStrafe() : 0f;
            _currentStrafeBlend = Mathf.Lerp(_currentStrafeBlend, targetStrafe,
                                             Time.deltaTime * _strafeBlendSpeed);
            if (_hasParamStrafe) _animator.SetFloat(HashStrafe, _currentStrafeBlend);

            float normalizedSpeed = rawState is "sprinting" or "sprint" ? 1f
                                  : rawState is "walking" or "walk"     ? 0.5f
                                  : 0f;
            if (_hasParamSpeed) _animator.SetFloat(HashSpeed, normalizedSpeed);

            // ── 4. State transition ───────────────────────────────────────────────
            if (_currentAnimState != targetState)
            {
                string playerTag = isLocal ? "LOCAL" : "REMOTE";
                Debug.Log($"[AnimController] {playerTag} '{gameObject.name}': {_currentAnimState} → {targetState} (raw: {rawState})");
                _animator.CrossFadeInFixedTime(targetState, _crossfadeDuration, 0);
                _currentAnimState = targetState;
            }

            // ── 5. Body rotation is now handled by PlayerInputController ──────────

            // ── 6. Procedural head bob (local only, camera root must be assigned) ──
            if (isLocal && _headBobEnabled && _cameraRoot != null)
                ApplyHeadBob(rawState);

            // ── 7. Landing detection ──────────────────────────────────────────────
            HandleLandingDetection(isGrounded);
        }

        // ── Movement state resolution ─────────────────────────────────────────────
        private string ResolveRawMovementState(bool isLocal)
        {
            if (isLocal)
                return _input.CurrentState.ToString().ToLower();

            return _remote != null
                ? _remote.MovementState?.ToLower() ?? "idle"
                : "idle";
        }

        // ── Animator state mapping ────────────────────────────────────────────────
        private static string MapToAnimatorState(string rawState, bool isGrounded)
        {
            if (!isGrounded)
                return "Airborne";

            return rawState switch
            {
                "walking"    => "Walking",
                "walk"       => "Walking",
                "crouchwalk" => "CrouchWalk",
                "crouch"     => "Crouch",
                "sprinting"  => "Running",
                "sprint"     => "Running",
                _            => "Idle"
            };
        }

        // ── Strafe detection ──────────────────────────────────────────────────────
        /// <summary>
        /// Returns a value in [-1, 1] representing lateral input relative to the camera.
        /// -1 = full left strafe, +1 = full right strafe.
        /// </summary>
        private float ResolveLocalStrafe()
        {
            // PlayerInputController is expected to expose a MoveInput Vector2 (x = horizontal, y = forward)
            // If your InputController uses a different API, adjust the property name here.
            if (_input == null) return 0f;

            Vector2 move = _input.MoveInput;   // (x = strafe, y = forward) in local space
            return Mathf.Clamp(move.x, -1f, 1f);
        }

        // ── Body rotation toward movement direction ───────────────────────────────
        /// <summary>
        /// Rotates the character root to face the actual world-space movement direction.
        /// Works like a real person walking: go right, face right. No orbit, no clamp.
        /// Camera stays fully decoupled; only the body/mesh turns.
        /// </summary>
        private void ApplyBodyLean(string rawState)
        {
            bool isMoving = rawState is "walking" or "walk" or
                                       "sprinting" or "sprint" or "crouchwalk";
            if (!isMoving) return;

            Vector2 move = _input.MoveInput;
            if (move.sqrMagnitude < 0.01f) return;

            // Project camera axes onto the horizontal plane so vertical look pitch is ignored
            Transform cam      = Camera.main != null ? Camera.main.transform : transform;
            Vector3 camForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 camRight   = Vector3.ProjectOnPlane(cam.right,   Vector3.up).normalized;

            // Only rotate when moving forward or laterally — never when moving purely backwards.
            // Backwards input (move.y < 0 with no lateral) would flip LookRotation 180°,
            // fighting the camera controller and causing the camera spin bug.
            bool isMovingBackwards = move.y < -0.1f && Mathf.Abs(move.x) < 0.3f;
            if (isMovingBackwards) return;

            // World-space direction the player is actually travelling toward
            // For diagonal-back (e.g. S+D), we zero out the backward component so the
            // body only turns sideways, which still feels natural.
            Vector2 forwardBiasedMove = new Vector2(move.x, Mathf.Max(move.y, 0f));
            if (forwardBiasedMove.sqrMagnitude < 0.01f) return;

            Vector3 moveDir = (camForward * forwardBiasedMove.y + camRight * forwardBiasedMove.x).normalized;
            if (moveDir == Vector3.zero) return;

            // Smoothly rotate the whole transform to face that direction
            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            transform.rotation   = Quaternion.Slerp(transform.rotation, targetRot,
                                                     Time.deltaTime * _bodyRotationSpeed);
        }

        // ── Procedural head bob ───────────────────────────────────────────────────
        /// <summary>
        /// Oscillates the camera root vertically and slightly horizontally while moving.
        /// Uses a sine wave driven by a private timer to stay frame-rate independent.
        /// </summary>
        private void ApplyHeadBob(string rawState)
        {
            bool isSprinting = rawState is "sprinting" or "sprint";
            bool isWalking   = rawState is "walking" or "walk" or "crouchwalk";
            bool isMoving    = isSprinting || isWalking;

            float targetFrequency = isSprinting ? _runBobFrequency  : _walkBobFrequency;
            float targetAmplitude = isSprinting ? _runBobAmplitude   : _walkBobAmplitude;

            if (isMoving)
            {
                _bobTimer += Time.deltaTime * targetFrequency;
                float vertical   =  Mathf.Sin(_bobTimer)      * targetAmplitude;
                float horizontal =  Mathf.Sin(_bobTimer * 0.5f) * (targetAmplitude * 0.4f);

                _cameraRoot.localPosition = _camRootLocalOrigin + new Vector3(horizontal, vertical, 0f);
            }
            else
            {
                // Smoothly return camera to neutral when stopping
                _cameraRoot.localPosition = Vector3.Lerp(_cameraRoot.localPosition,
                                                          _camRootLocalOrigin,
                                                          Time.deltaTime * _bobReturnSpeed);
                // Let timer drift to zero so next bob cycle starts cleanly
                _bobTimer = Mathf.Lerp(_bobTimer, 0f, Time.deltaTime * _bobReturnSpeed);
            }
        }

        // ── Landing detection ─────────────────────────────────────────────────────
        private void HandleLandingDetection(bool isGrounded)
        {
            if (!isGrounded)
            {
                _airborneTimer += Time.deltaTime;
                _wasGrounded    = false;
            }
            else
            {
                if (!_wasGrounded && _airborneTimer >= _minAirTimeForLanding)
                {
                    if (_hasParamLand) _animator.SetTrigger(HashLand);
                    Debug.Log($"[AnimController] '{gameObject.name}' landed after {_airborneTimer:F2}s airborne.");
                }

                _airborneTimer = 0f;
                _wasGrounded   = true;
            }
        }
    }
}