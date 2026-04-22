using UnityEngine;
using UnityEngine.InputSystem;

namespace Jailbreak.Player
{
    /// <summary>
    /// First-person camera controller (New Input System).
    /// - Mouse X/Y tracked as independent yaw/pitch floats.
    /// - Camera uses WORLD rotation so the body can rotate freely
    ///   to face movement direction without dragging the camera.
    /// - Eye height interpolates between stand (1.6m) and crouch (0.8m).
    /// </summary>
    public class FPSCameraController : MonoBehaviour
    {
        // ──────────────────────────── Settings ──────────────────────────
        [Header("Mouse")]
        public float sensitivity = 0.15f;  // units per pixel (raw delta)

        [Header("Eye Heights")]
        public float standEyeHeight  = 1.6f;
        public float crouchEyeHeight = 0.8f;

        [Header("FOV")]
        public float guardFOV    = 80f;
        public float prisonerFOV = 70f;

        [Header("Head Bob")]
        public bool  headBobEnabled         = false;
        public float bobFrequency           = Mathf.PI * 2f;
        public float walkBobAmplitude       = 0.02f;
        public float sprintBobAmplitude     = 0.04f;
        public float crouchWalkBobAmplitude = 0.01f;

        // ──────────────────────────── Public state ──────────────────────
        /// <summary>
        /// Camera yaw in degrees. Used by PlayerInputController to compute
        /// movement direction relative to where the player is looking.
        /// </summary>
        public float Yaw => _yaw;

        // ──────────────────────────── Private ───────────────────────────
        private Camera _cam;
        private PlayerInputController _input;
        private float _pitch;
        private float _yaw;
        private float _bobTimer;

        private void Awake()
        {
            _cam   = GetComponent<Camera>();
            _input = GetComponentInParent<PlayerInputController>();

            // Ensure the camera starts centered so it doesn't orbit
            transform.localPosition = new Vector3(0f, standEyeHeight, 0f);
        }

        private void Start()
        {
            SetFOVForRole();
            // Initialize yaw from the body's current facing so camera doesn't snap
            if (_input != null)
                _yaw = _input.transform.eulerAngles.y;
        }

        private void LateUpdate()
        {
            // Don't process mouse/camera until input is enabled (player is positioned)
            if (_input != null && !_input.InputEnabled) return;

            // Lock cursor on first active frame
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible   = false;
            }

            ApplyMouseLook();
            ApplyEyeHeight();
            if (headBobEnabled) ApplyHeadBob();
        }

        private void SetFOVForRole()
        {
            var gsm = Network.GameStateManager.Instance;
            if (gsm == null || _cam == null) return;
            _cam.fieldOfView = gsm.LocalRole == "guard" ? guardFOV : prisonerFOV;
        }

        private void ApplyMouseLook()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 delta = mouse.delta.ReadValue();
            float mouseX =  delta.x * sensitivity;
            float mouseY =  delta.y * sensitivity;

            // Yaw and pitch are tracked as independent floats.
            // The camera uses WORLD rotation so the body can rotate freely
            // to face the movement direction without dragging the camera.
            _yaw  += mouseX;
            _pitch -= mouseY;
            _pitch  = Mathf.Clamp(_pitch, -80f, 80f);

            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void ApplyEyeHeight()
        {
            if (_input == null) return;

            float targetY = _input.IsCrouching ? crouchEyeHeight : standEyeHeight;

            // Position the camera at the player's feet + eye height.
            // Since we use world rotation, local position still tracks correctly
            // because the offset is purely vertical (no X/Z offset to orbit).
            float currentY = Mathf.Lerp(transform.localPosition.y, targetY, 10f * Time.deltaTime);
            transform.localPosition = new Vector3(0f, currentY, 0f);
        }

        private void ApplyHeadBob()
        {
            if (_input == null) return;

            float amp = GetBobAmplitude(_input.CurrentState);
            if (amp > 0f)
            {
                _bobTimer += Time.deltaTime * bobFrequency;
                Vector3 lp = transform.localPosition;
                lp.y += Mathf.Sin(_bobTimer) * amp;
                transform.localPosition = lp;
            }
            else
            {
                _bobTimer = 0f;
            }
        }

        private float GetBobAmplitude(PlayerInputController.MovementState state) => state switch
        {
            PlayerInputController.MovementState.Walk        => walkBobAmplitude,
            PlayerInputController.MovementState.Sprint      => sprintBobAmplitude,
            PlayerInputController.MovementState.CrouchWalk  => crouchWalkBobAmplitude,
            _                                               => 0f,
        };
    }
}
