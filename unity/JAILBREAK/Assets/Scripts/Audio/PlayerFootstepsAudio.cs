using Jailbreak.Player;
using UnityEngine;
using UnityEngine.Audio;

namespace Jailbreak.Audio
{
    /// <summary>
    /// Footstep loop attached to the player prefab. Works for both local and
    /// remote players from the same component:
    ///   • Local  → reads <see cref="PlayerInputController.CurrentState"/>.
    ///   • Remote → reads <see cref="RemotePlayerSync.MovementState"/> string.
    ///
    /// "Pasos cuando corre" — by default only the Sprint state plays.
    /// Toggle <see cref="alsoPlayWhileWalking"/> to extend to walk/crouch-walk.
    ///
    /// Implemented as a LOOP (not per-step one-shots) because the design brief
    /// describes "pasos 3D loop"; rate is controlled by clip authoring + pitch.
    /// </summary>
    public class PlayerFootstepsAudio : MonoBehaviour
    {
        [Header("Clip")]
        public AudioClip runLoopClip;
        [Tooltip("Optional separate loop for walk. Falls back to runLoopClip pitched down.")]
        public AudioClip walkLoopClip;

        [Header("Mix")]
        public AudioMixerGroup mixerGroup;
        [Range(0f, 1f)] public float runVolume = 0.9f;
        [Range(0f, 1f)] public float walkVolume = 0.6f;
        [Range(0.1f, 3f)] public float runPitch = 1.0f;
        [Range(0.1f, 3f)] public float walkPitch = 0.85f;

        [Header("3D Falloff")]
        public float minDistance = 1.5f;
        public float maxDistance = 12f;
        public AudioRolloffMode rolloff = AudioRolloffMode.Linear;

        [Header("State")]
        public bool alsoPlayWhileWalking = false;
        [Tooltip("Seconds for fade-in / fade-out when state changes.")]
        public float fadeSeconds = 0.08f;

        // ── Internal ──────────────────────────────────────────────────────────
        private AudioSource _src;
        private PlayerInputController _localInput;
        private RemotePlayerSync _remote;

        private enum Mode { Silent, Walk, Run }
        private Mode _currentMode = Mode.Silent;
        private float _targetVolume;

        private void Awake()
        {
            _src = gameObject.GetComponent<AudioSource>();
            if (_src == null) _src = gameObject.AddComponent<AudioSource>();
            _src.loop = true;
            _src.playOnAwake = false;
            _src.spatialBlend = 1f;
            _src.minDistance = minDistance;
            _src.maxDistance = maxDistance;
            _src.rolloffMode = rolloff;
            _src.outputAudioMixerGroup = mixerGroup;
            _src.volume = 0f;

            _localInput = GetComponent<PlayerInputController>();
            _remote = GetComponent<RemotePlayerSync>();
        }

        private void Update()
        {
            // Late-attached components: lazily resolve so this works on both
            // local and remote players regardless of spawn order.
            if (_localInput == null) _localInput = GetComponent<PlayerInputController>();
            if (_remote == null) _remote = GetComponent<RemotePlayerSync>();

            Mode desired = ResolveMode();

            if (desired != _currentMode)
                ApplyMode(desired);

            // Smooth volume toward target (handles fade in/out without a coroutine).
            if (!Mathf.Approximately(_src.volume, _targetVolume))
            {
                float step = fadeSeconds <= 0f ? 1f : Time.deltaTime / fadeSeconds;
                _src.volume = Mathf.MoveTowards(_src.volume, _targetVolume, step);
                if (_targetVolume <= 0f && _src.volume <= 0.001f && _src.isPlaying)
                    _src.Stop();
            }
        }

        private Mode ResolveMode()
        {
            // Local player path (authoritative state)
            if (_localInput != null)
            {
                switch (_localInput.CurrentState)
                {
                    case PlayerInputController.MovementState.Sprint:
                        return Mode.Run;
                    case PlayerInputController.MovementState.Walk:
                    case PlayerInputController.MovementState.CrouchWalk:
                        return alsoPlayWhileWalking ? Mode.Walk : Mode.Silent;
                    default:
                        return Mode.Silent;
                }
            }

            // Remote player path — the network broadcasts a lowercase string.
            if (_remote != null)
            {
                string s = _remote.MovementState?.ToLowerInvariant();
                if (s == "sprint" || s == "sprinting") return Mode.Run;
                if (alsoPlayWhileWalking && (s == "walk" || s == "walking" || s == "crouchwalk"))
                    return Mode.Walk;
            }

            return Mode.Silent;
        }

        private void ApplyMode(Mode mode)
        {
            _currentMode = mode;

            switch (mode)
            {
                case Mode.Run:
                    PlayWith(runLoopClip != null ? runLoopClip : walkLoopClip, runPitch, runVolume);
                    break;
                case Mode.Walk:
                    PlayWith(walkLoopClip != null ? walkLoopClip : runLoopClip, walkPitch, walkVolume);
                    break;
                default:
                    _targetVolume = 0f;
                    break;
            }
        }

        private void PlayWith(AudioClip clip, float pitch, float volume)
        {
            if (clip == null) { _targetVolume = 0f; return; }
            if (_src.clip != clip) _src.clip = clip;
            _src.pitch = pitch;
            _targetVolume = volume;
            if (!_src.isPlaying) _src.Play();
        }
    }
}
