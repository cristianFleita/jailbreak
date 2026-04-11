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
        
        private string _currentAnimState = "";

        private void Awake()
        {
            // Try to find animator on the child model
            _animator = GetComponentInChildren<Animator>();
            _input = GetComponent<PlayerInputController>();
            
            // We DO NOT get RemotePlayerSync here because GameStateManager adds it 
            // dynamically AFTER the prefab is instantiated and Awake has already run.

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

            // Dynamically grab the remote sync component if we haven't found it yet
            if (_remote == null)
            {
                _remote = GetComponent<RemotePlayerSync>();
            }

            string moveState = "idle";
            
            // Unity overloads '!= null'. If GameStateManager destroyed _input, this evaluates to false.
            bool isLocal = _input != null; 

            // 1. Determine state
            // Local player dictates its own state via Keyboard
            if (isLocal)
            {
                moveState = _input.CurrentState.ToString().ToLower();
            }
            // Remote player receives its state from the server payload
            else if (_remote != null)
            {
                moveState = _remote.MovementState?.ToLower() ?? "idle";
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

            // 2. Handle Animation CrossFading
            if (_currentAnimState != targetAnimState)
            {
                string playerType = isLocal ? "LOCAL" : "REMOTE";
                Debug.Log($"[AnimController] {playerType} ({gameObject.name}) animating: {_currentAnimState} -> {targetAnimState} (Raw Input: {moveState})");

                // Use CrossFadeInFixedTime! 
                // Standard CrossFade uses a percentage of the animation length. If an Idle animation is long,
                // the transition takes forever. FixedTime ensures it always takes exactly 0.15 seconds,
                // which fixes the bug where tapping 'W' very quickly didn't play the animation.
                _animator.CrossFadeInFixedTime(targetAnimState, 0.15f, 0);
                
                _currentAnimState = targetAnimState;
            }
        }
    }
}