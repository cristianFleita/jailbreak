using UnityEngine;
using UnityEngine.InputSystem;

namespace Jailbreak.Player
{
    /// <summary>
    /// First-person camera controller (New Input System).
    /// - Mouse X yaws the player body (capsule).
    /// - Mouse Y pitches only this camera (clamped ±80°).
    /// - Eye height interpolates between stand (1.6m) and crouch (0.8m).
    /// - Forces local X and Z to 0 so the camera never orbits/swings.
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

        // ──────────────────────────── Private ───────────────────────────
        private Camera _cam;
        private PlayerInputController _input;
        private float _pitch;
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

            // Yaw: Rotate the entire player body (capsule) left and right
            if (_input != null)
            {
                _input.transform.Rotate(0f, mouseX, 0f, Space.World);
            }

            // Pitch: Rotate ONLY the camera up and down (the neck)
            _pitch -= mouseY;
            _pitch  = Mathf.Clamp(_pitch, -80f, 80f);
            transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void ApplyEyeHeight()
        {
            if (_input == null) return;
            
            float targetY = _input.IsCrouching ? crouchEyeHeight : standEyeHeight;
            
            // We lock X and Z strictly to 0f. 
            // This guarantees the camera sits exactly in the center of the capsule
            // like human eyes, completely eliminating the "orbiting" feeling.
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
