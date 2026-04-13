using UnityEngine;
using UnityEngine.InputSystem;

namespace Jailbreak.Player
{
    /// <summary>
    /// Reads WASD + Shift + C input (New Input System) and drives the CharacterController.
    /// PlayerNetworkSync reads CC velocity automatically — no coupling needed.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerInputController : MonoBehaviour
    {
        // ──────────────────────────── Speeds ────────────────────────────
        [Header("Movement Speeds (units/sec)")]
        public float walkSpeed        = 3.5f;
        public float sprintSpeed      = 5.5f;
        public float crouchWalkSpeed  = 1.75f;
        public float gravity          = -19.62f;

        // ──────────────────────────── Acceleration ──────────────────────
        [Header("Acceleration")]
        public float groundAcceleration = 50f;
        public float groundDeceleration = 40f;
        public float groundStickForce   = -5f;

        // ──────────────────────────── Body Rotation ────────────────────
        [Header("Body Rotation")]
        public float bodyTurnSpeed = 12f;

        // ──────────────────────────── Crouch ────────────────────────────
        [Header("Crouch")]
        public float standHeight  = 1.8f;
        public float crouchHeight = 1.08f;

        // ──────────────────────────── State ─────────────────────────────
        public enum MovementState { Idle, Walk, Sprint, Crouch, CrouchWalk }

        public MovementState CurrentState { get; private set; }
        public bool IsCrouching          { get; private set; }

        /// <summary>
        /// Raw movement input in local space this frame.
        /// x = horizontal strafe (-1 left … +1 right), y = forward/back (-1 … +1).
        /// Read by PlayerAnimationController to drive the Strafe blend parameter.
        /// </summary>
        public Vector2 MoveInput { get; private set; }

        /// <summary>
        /// Set to true by PlayerNetworkSync.TeleportToSpawn() once the character
        /// is properly positioned. Until then, NO movement or gravity runs.
        /// </summary>
        public bool InputEnabled { get; set; }

        // ──────────────────────────── Private ───────────────────────────
        private CharacterController _cc;
        private FPSCameraController _fpsCam;
        private float   _verticalVelocity;
        private bool    _crouchToggled;
        private Vector3 _horizontalVelocity;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _fpsCam = GetComponentInChildren<FPSCameraController>();
        }

        private void Update()
        {
            if (!InputEnabled) return;
            HandleCrouchInput();
            ApplyMovement();
        }

        private void HandleCrouchInput()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame)
                _crouchToggled = !_crouchToggled;

            IsCrouching = _crouchToggled;

            float targetHeight = IsCrouching ? crouchHeight : standHeight;
            _cc.height = Mathf.Lerp(_cc.height, targetHeight, 10f * Time.deltaTime);
            _cc.center = new Vector3(0, _cc.height / 2f, 0);
        }

        private void ApplyMovement()
        {
            float h = 0f, v = 0f;
            bool shifting = false;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  h -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  v -= 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    v += 1f;

                shifting = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }

            // Normalize diagonal input so it doesn't exceed magnitude 1
            Vector2 rawInput = new Vector2(h, v);
            if (rawInput.sqrMagnitude > 1f) rawInput.Normalize();

            bool hasInput = rawInput.sqrMagnitude > 0.001f;

            // Expose input for PlayerAnimationController
            MoveInput = rawInput;

            // Determine state and target speed
            float targetSpeed;
            if (!hasInput && IsCrouching)
            {
                CurrentState = MovementState.Crouch;
                targetSpeed = 0f;
            }
            else if (!hasInput)
            {
                CurrentState = MovementState.Idle;
                targetSpeed = 0f;
            }
            else if (IsCrouching)
            {
                CurrentState = MovementState.CrouchWalk;
                targetSpeed = crouchWalkSpeed;
            }
            else if (shifting)
            {
                CurrentState = MovementState.Sprint;
                targetSpeed = sprintSpeed;
            }
            else
            {
                CurrentState = MovementState.Walk;
                targetSpeed = walkSpeed;
            }

            // Build wish direction from CAMERA yaw (not body forward).
            // This way pressing W always goes where you're looking,
            // and body rotation is purely cosmetic.
            float camYaw = _fpsCam != null ? _fpsCam.Yaw : transform.eulerAngles.y;
            float yawRad = camYaw * Mathf.Deg2Rad;
            Vector3 fwd   = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3 wishDir = (fwd * rawInput.y + right * rawInput.x).normalized;


            // Accelerate / decelerate toward wish velocity
            Vector3 wishVelocity = wishDir * targetSpeed;
            float accelRate = hasInput ? groundAcceleration : groundDeceleration;
            _horizontalVelocity = Vector3.MoveTowards(
                _horizontalVelocity, wishVelocity, accelRate * Time.deltaTime);

            // Rotate body to face movement direction (in-place, no orbit).
            // When idle, body faces camera direction so the character
            // doesn't stand sideways when you stop.
            if (hasInput && _horizontalVelocity.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(_horizontalVelocity.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, bodyTurnSpeed * Time.deltaTime);
            }
            else
            {
                Quaternion camFacing = Quaternion.Euler(0f, camYaw, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, camFacing, bodyTurnSpeed * Time.deltaTime);
            }

            // Gravity — always applied, with ground-stick to prevent bouncing on slopes
            if (_cc.isGrounded)
                _verticalVelocity = groundStickForce;
            else
                _verticalVelocity += gravity * Time.deltaTime;

            Vector3 finalMove = _horizontalVelocity + Vector3.up * _verticalVelocity;
            _cc.Move(finalMove * Time.deltaTime);
        }
    }
}
