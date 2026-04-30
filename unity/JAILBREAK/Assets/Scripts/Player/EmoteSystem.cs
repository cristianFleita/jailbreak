using System;
using System.Collections.Generic;
using Jailbreak.Network;
using Jailbreak.UI;
using UnityEngine;

namespace Jailbreak.Player
{
    /// <summary>
    /// Emote system that allows prisoners to perform NPC-like animations to
    /// blend in. Holds a catalogue of emotes mapped to Animator state names.
    ///
    /// Usage flow:
    ///   1. Player holds B → EmotePanel opens, cursor unlocks.
    ///   2. Player clicks an emote → animation plays, cursor re-locks.
    ///   3. Animation plays for its configured duration (movement is frozen).
    ///   4. After duration or early cancel (any WASD/Shift), player returns to Idle.
    ///
    /// Network sync:
    ///   - Local player broadcasts emote via PlayerNetworkSync.movementState
    ///     (e.g. "emote_Talking") which remote clients read and crossfade.
    ///   - When emote ends, movementState returns to normal locomotion.
    /// </summary>
    public class EmoteSystem : MonoBehaviour
    {
        // ── Emote catalogue ──────────────────────────────────────────────────
        // Animator state names below MUST match real states inside
        // Character.controller, AND must also be reachable from at least one
        // backend NPC animTrigger (see backend/src/game/systems/npc-animations.ts).
        // That overlap is what makes disguising as an NPC visually identical.
        public enum EmoteCategory { Social, Work, Exercise, Fun }

        [Serializable]
        public class EmoteEntry
        {
            public string id;
            public string displayName;
            public string animatorState;
            public string icon;
            public float  duration;
            public bool   loops;
            public EmoteCategory category;
        }

        public static readonly EmoteEntry[] Catalogue = new[]
        {
            // Social — mirrors NPC_ANIM.TALK_STANDING / WHISPER / SALUTE / DISMISS / SURPRISE / ARGUE
            new EmoteEntry { id = "talking",        displayName = "Talk",      animatorState = "Talking",       icon = "...", duration = 4f,   loops = true,  category = EmoteCategory.Social   },
            new EmoteEntry { id = "telling_secret", displayName = "Whisper",   animatorState = "TellingSecret", icon = "Shh", duration = 3f,   loops = true,  category = EmoteCategory.Social   },
            new EmoteEntry { id = "dismissing",     displayName = "Dismiss",   animatorState = "Dismissing",    icon = "Bye", duration = 2f,   loops = false, category = EmoteCategory.Social   },
            new EmoteEntry { id = "surprised",      displayName = "Surprised", animatorState = "Surprised",     icon = "!?",  duration = 2f,   loops = false, category = EmoteCategory.Social   },
            new EmoteEntry { id = "angry",          displayName = "Angry",     animatorState = "Angry",         icon = "Grr", duration = 3f,   loops = true,  category = EmoteCategory.Social   },

            // Exercise — mirrors yard_exercise / yard_shadow_box and adds variety
            new EmoteEntry { id = "pushup",         displayName = "Push-ups",  animatorState = "PushUp",        icon = "Up",  duration = 5f,   loops = true,  category = EmoteCategory.Exercise },
            new EmoteEntry { id = "situps",         displayName = "Sit-ups",   animatorState = "Situps",        icon = "Sit", duration = 5f,   loops = true,  category = EmoteCategory.Exercise },
            new EmoteEntry { id = "crunch",         displayName = "Crunches",  animatorState = "BicycleCrunch", icon = "Abs", duration = 5f,   loops = true,  category = EmoteCategory.Exercise },
            new EmoteEntry { id = "shadowbox",      displayName = "Shadowbox", animatorState = "Punching",      icon = "Box", duration = 4f,   loops = true,  category = EmoteCategory.Exercise },

            // Fun — yard_dance equivalent
            new EmoteEntry { id = "dance",          displayName = "Dance",     animatorState = "SillyDancing",  icon = "Dnc", duration = 6f,   loops = true,  category = EmoteCategory.Fun      },
        };

        // ── State ────────────────────────────────────────────────────────────
        public bool IsEmoting       { get; private set; }
        public bool IsPanelOpen     { get; private set; }
        public string CurrentEmoteId { get; private set; }

        /// <summary>Raised when the panel opens/closes (for UI binding).</summary>
        public event Action<bool> OnPanelToggled;
        /// <summary>Raised when an emote starts (id) or ends (null).</summary>
        public event Action<string> OnEmoteChanged;

        private Animator _animator;
        private PlayerInputController _input;
        private float _emoteEndTime;
        private bool _hasUnlockToken;
        private EmoteEntry _activeEmote;
        private string _prevAnimState;

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            _input = GetComponent<PlayerInputController>();
        }

        private void Update()
        {
            if (_input == null || !_input.InputEnabled) return;

            // ── Emote early cancel: any movement input cancels ──────────────
            if (IsEmoting && _input.MoveInput.sqrMagnitude > 0.01f)
            {
                StopEmote();
                return;
            }

            // ── Duration-based auto stop (non-looping) ──────────────────────
            if (IsEmoting && !_activeEmote.loops && Time.time >= _emoteEndTime)
            {
                StopEmote();
                return;
            }
        }

        // ── Public API ───────────────────────────────────────────────────────

        public void OpenPanel()
        {
            if (IsPanelOpen || IsEmoting) return;
            IsPanelOpen = true;
            CursorLockManager.RequestUnlock(this);
            _hasUnlockToken = true;
            OnPanelToggled?.Invoke(true);
        }

        public void ClosePanel()
        {
            if (!IsPanelOpen) return;
            IsPanelOpen = false;
            if (_hasUnlockToken)
            {
                CursorLockManager.ReleaseUnlock(this);
                _hasUnlockToken = false;
            }
            OnPanelToggled?.Invoke(false);
        }

        /// <summary>Start playing an emote by its catalogue id.</summary>
        public void PlayEmote(string emoteId)
        {
            var entry = FindEntry(emoteId);
            if (entry == null)
            {
                Debug.LogWarning($"[EmoteSystem] Unknown emote id: {emoteId}");
                return;
            }

            // Close the panel first (re-locks cursor)
            ClosePanel();

            if (IsEmoting) StopEmote();

            _activeEmote = entry;
            CurrentEmoteId = emoteId;
            IsEmoting = true;
            _emoteEndTime = entry.duration > 0f ? Time.time + entry.duration : float.MaxValue;

            // Play the animation via CrossFadeInFixedTime
            if (_animator != null)
            {
                _animator.CrossFadeInFixedTime(entry.animatorState, 0.15f, 0);
                Debug.Log($"[EmoteSystem] Playing emote '{entry.displayName}' → {entry.animatorState}");
            }

            // Broadcast to network so remote players see it
            BroadcastEmoteState(entry.animatorState);

            OnEmoteChanged?.Invoke(emoteId);
        }

        public void StopEmote()
        {
            if (!IsEmoting) return;

            IsEmoting = false;
            CurrentEmoteId = null;
            _activeEmote = null;

            // Return to Idle
            if (_animator != null)
                _animator.CrossFadeInFixedTime("Idle", 0.25f, 0);

            // Broadcast stop
            BroadcastEmoteState(null);

            OnEmoteChanged?.Invoke(null);
        }

        /// <summary>
        /// Called by PlayerAnimationController to know if it should skip
        /// overriding the animation state while an emote is active.
        /// </summary>
        public bool ShouldBlockLocomotionAnim() => IsEmoting;

        /// <summary>
        /// Returns the emote animator state name if currently emoting,
        /// used by PlayerNetworkSync to send as movementState.
        /// </summary>
        public string GetEmoteMovementState()
        {
            if (!IsEmoting || _activeEmote == null) return null;
            return $"emote_{_activeEmote.animatorState}";
        }

        // ── Internals ────────────────────────────────────────────────────────

        private void BroadcastEmoteState(string animState)
        {
            // We use SendPlayerAction with a synthetic objectId to broadcast emotes.
            // The server forwards this as player:action, and remote clients replay it.
            var net = NetworkManager.Instance;
            if (net == null) return;

            if (!string.IsNullOrEmpty(animState))
                net.SendPlayerAction("emote_system", $"emote_start:{animState}");
            else
                net.SendPlayerAction("emote_system", "emote_stop");
        }

        private static EmoteEntry FindEntry(string id)
        {
            foreach (var e in Catalogue)
                if (e.id == id) return e;
            return null;
        }

        private void OnDisable()
        {
            // Clean up cursor lock token if we're destroyed while panel is open
            if (_hasUnlockToken)
            {
                CursorLockManager.ReleaseUnlock(this);
                _hasUnlockToken = false;
            }
        }
    }
}
