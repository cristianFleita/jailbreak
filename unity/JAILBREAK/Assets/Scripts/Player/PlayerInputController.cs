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
        public float gravity          = -9.81f;

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
        private float _verticalVelocity;
        private bool  _crouchToggled;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
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

            bool hasInput = Mathf.Abs(h) > 0.01f || Mathf.Abs(v) > 0.01f;

            // Expose raw input so PlayerAnimationController can read strafe direction
            MoveInput = hasInput ? new Vector2(h, v).normalized : Vector2.zero;

            // Determine state and speed
            float speed;
            if (!hasInput && IsCrouching)
            {
                CurrentState = MovementState.Crouch;
                speed = 0f;
            }
            else if (!hasInput)
            {
                CurrentState = MovementState.Idle;
                speed = 0f;
            }
            else if (IsCrouching)
            {
                CurrentState = MovementState.CrouchWalk;
                speed = crouchWalkSpeed;
            }
            else if (shifting)
            {
                CurrentState = MovementState.Sprint;
                speed = sprintSpeed;
            }
            else
            {
                CurrentState = MovementState.Walk;
                speed = walkSpeed;
            }

            // No input → freeze completely. No gravity, no cc.Move(), nothing.
            // This prevents sliding on sloped geometry and falling through floors.
            // Gravity resumes the moment the player presses a movement key.
            if (!hasInput)
            {
                _verticalVelocity = 0f;
                return;
            }

            // Movement direction: body yaw handled by FPSCameraController
            Vector3 fwd   = new Vector3(transform.forward.x, 0, transform.forward.z).normalized;
            Vector3 right = new Vector3(transform.right.x,   0, transform.right.z  ).normalized;
            Vector3 dir   = (fwd * v + right * h).normalized;

            // Gravity
            if (_cc.isGrounded && _verticalVelocity < 0)
                _verticalVelocity = -2f;
            _verticalVelocity += gravity * Time.deltaTime;

            _cc.Move((dir * speed + Vector3.up * _verticalVelocity) * Time.deltaTime);
        }
    }
}