using UnityEngine;

namespace Jailbreak.Player
{
    /// <summary>
    /// Reads the movement state from either the local PlayerInputController
    /// or the RemotePlayerSync component, and drives the Animator.
    /// Attach to the root player prefab alongside PlayerInputController.
    /// </summary>
    public class PlayerAnimationController : MonoBehaviour
    {
        private Animator _animator;
        private PlayerInputController _input;
        private RemotePlayerSync _remote;
        private CharacterController _cc;
        
        private string _currentAnimState = "";
        private Vector3 _lastPos;

        private void Awake()
        {
            // Try to find animator on the child model
            _animator = GetComponentInChildren<Animator>();
            _input = GetComponent<PlayerInputController>();
            _remote = GetComponent<RemotePlayerSync>();
            _cc = GetComponent<CharacterController>(); // Local player uses CC for velocity

            if (_animator == null)
            {
                Debug.LogWarning($"[AnimController] CRITICAL: No Animator found on {gameObject.name} or its children! Animations will not play.");
            }
            else
            {
                Debug.Log($"[AnimController] Animator successfully linked for {gameObject.name}");
            }
        }

        private void Update()
        {
            if (_animator == null) return;

            string moveState = "idle";
            Vector3 worldVelocity = Vector3.zero;
            bool isLocal = _input != null;

            // 1. Determine state and calculate velocity
            // Local player dictates its own state via Keyboard
            if (isLocal)
            {
                moveState = _input.CurrentState.ToString().ToLower();
                if (_cc != null) worldVelocity = _cc.velocity;
            }
            // Remote player receives its state from the server payload
            else if (_remote != null)
            {
                moveState = _remote.MovementState?.ToLower() ?? "idle";
                
                // Calculate manual velocity for remote players by tracking their positional changes
                if (Time.deltaTime > 0)
                {
                    worldVelocity = (transform.position - _lastPos) / Time.deltaTime;
                }
                _lastPos = transform.position;
            }

            // Map movement state to actual Animator state names found in Character.controller
            string targetAnimState = moveState switch
            {
                "walking" => "Walking",
                "walk" => "Walking",
                "crouchwalk" => "Walking", 
                "sprinting" => "Running",
                "sprint" => "Running",
                _ => "Idle"
            };

            // CrossFade smoothly to the new animation if it changed
            if (_currentAnimState != targetAnimState)
            {
                string playerType = isLocal ? "LOCAL" : "REMOTE";
                Debug.Log($"[AnimController] {playerType} ({gameObject.name}) animating: {_currentAnimState} -> {targetAnimState} (Raw Input: {moveState})");


                // Safely crossfade. The '0' specifically targets the Base Layer, which prevents Unity from ignoring it
                _animator.CrossFadeInFixedTime(targetAnimState, 0.15f, 0);

                
                _currentAnimState = targetAnimState;
            }
        }
    }
}