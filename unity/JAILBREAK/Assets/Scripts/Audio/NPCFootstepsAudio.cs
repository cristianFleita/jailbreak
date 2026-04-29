using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Audio;

namespace Jailbreak.Audio
{
    /// <summary>
    /// Footstep loop attached to an NPC prefab. Drives walk / run audio from
    /// the <see cref="NavMeshAgent"/>'s actual planar velocity, so it
    /// automatically goes silent when the NPC is sitting, sleeping, idle,
    /// arriving at a destination, or paused between sequence steps —
    /// regardless of which animation state is currently active.
    ///
    /// Mirrors <see cref="PlayerFootstepsAudio"/>'s shape (run/walk loops,
    /// mix, falloff, fades) so the same clips can be reused on NPCs.
    /// </summary>
    public class NPCFootstepsAudio : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("Optional. Auto-resolved from this GameObject if left empty.")]
        public NavMeshAgent agent;

        [Header("Clips")]
        public AudioClip runLoopClip;
        [Tooltip("Optional separate loop for walk. Falls back to runLoopClip pitched down.")]
        public AudioClip walkLoopClip;

        [Header("Mix")]
        public AudioMixerGroup mixerGroup;
        [Range(0f, 1f)] public float runVolume = 0.8f;
        [Range(0f, 1f)] public float walkVolume = 0.5f;
        [Range(0.1f, 3f)] public float runPitch = 1.0f;
        [Range(0.1f, 3f)] public float walkPitch = 0.85f;

        [Header("3D Falloff")]
        public float minDistance = 1.5f;
        public float maxDistance = 12f;
        public AudioRolloffMode rolloff = AudioRolloffMode.Linear;

        [Header("Speed Thresholds (m/s)")]
        [Tooltip("Below this planar speed the NPC is treated as stopped — no footsteps.")]
        public float silentSpeed = 0.4f;
        [Tooltip("At or above this planar speed the NPC is running. " +
                 "NPCBehaviorController uses ~3.5 * walkMult * variance for walk, ~1.8x that for run, " +
                 "so a threshold around 4.5–5.0 cleanly separates the two.")]
        public float runSpeed = 4.5f;

        [Header("Behavior")]
        [Tooltip("If false, only the running NPCs play footsteps (matches the player default 'pasos cuando corre').")]
        public bool alsoPlayWhileWalking = true;
        [Tooltip("Seconds for fade-in / fade-out when state changes.")]
        public float fadeSeconds = 0.08f;

        // ── Internal ──────────────────────────────────────────────────────────
        private AudioSource _src;

        private enum Mode { Silent, Walk, Run }
        private Mode _currentMode = Mode.Silent;
        private float _targetVolume;

        private void Awake()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();

            _src = GetComponent<AudioSource>();
            if (_src == null) _src = gameObject.AddComponent<AudioSource>();
            _src.loop = true;
            _src.playOnAwake = false;
            _src.spatialBlend = 1f;
            _src.minDistance = minDistance;
            _src.maxDistance = maxDistance;
            _src.rolloffMode = rolloff;
            _src.outputAudioMixerGroup = mixerGroup;
            _src.volume = 0f;
        }

        private void Update()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();

            Mode desired = ResolveMode();

            if (desired != _currentMode)
                ApplyMode(desired);

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
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return Mode.Silent;

            // Planar speed only — vertical motion (warp/teleport snap) shouldn't count.
            Vector3 v = agent.velocity; v.y = 0f;
            float speed = v.magnitude;

            if (speed < silentSpeed) return Mode.Silent;
            if (speed >= runSpeed)   return Mode.Run;
            return alsoPlayWhileWalking ? Mode.Walk : Mode.Silent;
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

        private void OnDisable()
        {
            // Hard-stop on disable so a despawning NPC doesn't leave its loop bleeding.
            if (_src != null)
            {
                _src.volume = 0f;
                _targetVolume = 0f;
                if (_src.isPlaying) _src.Stop();
            }
            _currentMode = Mode.Silent;
        }
    }
}
