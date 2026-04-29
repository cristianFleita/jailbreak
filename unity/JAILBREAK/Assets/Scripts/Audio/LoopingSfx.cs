using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

namespace Jailbreak.Audio
{
    /// <summary>
    /// Drop-in looping AudioSource with Play/Stop API and optional fade in/out.
    /// Use for: ventilation, washer, worktable, search drawers, security alarm,
    /// cell ambience layers, intro music.
    ///
    /// 2D vs 3D: configured via <see cref="spatial"/>. For props in the world,
    /// keep 3D and let Unity attenuate by distance. For UI/music/global ambience,
    /// switch to 2D so the listener position does not matter.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class LoopingSfx : MonoBehaviour
    {
        public enum Spatial { Two_D = 0, Three_D = 1 }

        [Header("Clip")]
        public AudioClip clip;

        [Header("Mix")]
        public AudioMixerGroup mixerGroup;
        [Range(0f, 1f)] public float volume = 1f;
        [Range(-3f, 3f)] public float pitch = 1f;
        public Spatial spatial = Spatial.Three_D;

        [Header("3D Falloff (only used when Spatial = Three_D)")]
        public float minDistance = 2f;
        public float maxDistance = 20f;
        public AudioRolloffMode rolloff = AudioRolloffMode.Linear;

        [Header("Behavior")]
        [Tooltip("Start playing automatically when the GameObject is enabled.")]
        public bool playOnEnable = false;
        [Tooltip("Seconds to fade in when Play() is called. 0 = instant.")]
        public float fadeInSeconds = 0.25f;
        [Tooltip("Seconds to fade out when Stop() is called. 0 = instant.")]
        public float fadeOutSeconds = 0.4f;

        public bool IsPlaying => _src != null && _src.isPlaying;
        public AudioSource Source => _src;

        private AudioSource _src;
        private Coroutine _fadeRoutine;

        private void Awake()
        {
            _src = GetComponent<AudioSource>();
            ApplySettingsToSource();
        }

        private void OnEnable()
        {
            if (playOnEnable) Play();
        }

        private void OnDisable()
        {
            if (_fadeRoutine != null) { StopCoroutine(_fadeRoutine); _fadeRoutine = null; }
            if (_src != null) _src.Stop();
        }

        private void OnValidate()
        {
            if (_src == null) _src = GetComponent<AudioSource>();
            if (_src != null) ApplySettingsToSource();
        }

        private void ApplySettingsToSource()
        {
            _src.clip = clip;
            _src.loop = true;
            _src.playOnAwake = false;
            _src.outputAudioMixerGroup = mixerGroup;
            _src.pitch = pitch;
            _src.spatialBlend = spatial == Spatial.Three_D ? 1f : 0f;
            _src.minDistance = minDistance;
            _src.maxDistance = maxDistance;
            _src.rolloffMode = rolloff;
            // Volume is intentionally NOT set here — fades own it at runtime.
            if (!Application.isPlaying) _src.volume = volume;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Play()
        {
            if (_src == null || clip == null) return;
            if (_src.clip != clip) _src.clip = clip;

            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);

            if (!_src.isPlaying)
            {
                _src.volume = fadeInSeconds > 0f ? 0f : volume;
                _src.Play();
            }

            if (fadeInSeconds > 0f)
                _fadeRoutine = StartCoroutine(FadeTo(volume, fadeInSeconds, false));
            else
                _src.volume = volume;
        }

        public void Stop()
        {
            if (_src == null || !_src.isPlaying) return;

            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);

            if (fadeOutSeconds > 0f)
                _fadeRoutine = StartCoroutine(FadeTo(0f, fadeOutSeconds, true));
            else
                _src.Stop();
        }

        /// <summary>Smoothly set the runtime volume (overrides inspector value until next Play).</summary>
        public void SetVolume(float v, float fadeSeconds = 0.25f)
        {
            volume = Mathf.Clamp01(v);
            if (!IsPlaying) return;

            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            if (fadeSeconds <= 0f) { _src.volume = volume; return; }
            _fadeRoutine = StartCoroutine(FadeTo(volume, fadeSeconds, false));
        }

        private IEnumerator FadeTo(float target, float seconds, bool stopAtEnd)
        {
            float start = _src.volume;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                _src.volume = Mathf.Lerp(start, target, t / seconds);
                yield return null;
            }
            _src.volume = target;
            if (stopAtEnd) _src.Stop();
            _fadeRoutine = null;
        }
    }
}
