using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

namespace Jailbreak.Audio
{
    /// <summary>
    /// Plays a single one-shot clip (or a random choice from a list) on demand.
    /// Designed to be called from UnityEvents (animation events, ProgressAction.onComplete,
    /// IInteractable callbacks) or from code via <see cref="Play"/>.
    ///
    /// Use for: prison door open, throwing food in trash, wrong-mark beep,
    /// mission-completed cue.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class OneShotSfx : MonoBehaviour
    {
        [Header("Clips (one chosen randomly)")]
        public AudioClip[] clips;

        [Header("Mix")]
        public AudioMixerGroup mixerGroup;
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0f, 0.5f)] public float pitchVariance = 0.05f;

        [Header("Spatial")]
        [Tooltip("0 = pure 2D (UI/music), 1 = full 3D positional.")]
        [Range(0f, 1f)] public float spatialBlend = 1f;
        public float minDistance = 2f;
        public float maxDistance = 20f;
        public AudioRolloffMode rolloff = AudioRolloffMode.Linear;

        private AudioSource _src;
        private Coroutine _timedRoutine;

        private void Awake()
        {
            _src = GetComponent<AudioSource>();
            ApplySettingsToSource();
        }

        private void OnValidate()
        {
            if (_src == null) _src = GetComponent<AudioSource>();
            if (_src != null) ApplySettingsToSource();
        }

        private void ApplySettingsToSource()
        {
            _src.playOnAwake = false;
            _src.loop = false;
            _src.outputAudioMixerGroup = mixerGroup;
            _src.spatialBlend = spatialBlend;
            _src.minDistance = minDistance;
            _src.maxDistance = maxDistance;
            _src.rolloffMode = rolloff;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Play a random clip from the list.</summary>
        public void Play()
        {
            if (clips == null || clips.Length == 0) return;
            int i = clips.Length == 1 ? 0 : Random.Range(0, clips.Length);
            PlayClip(clips[i]);
        }

        /// <summary>Play a specific clip by array index. Wire from UnityEvents.</summary>
        public void PlayIndex(int index)
        {
            if (clips == null || index < 0 || index >= clips.Length) return;
            PlayClip(clips[index]);
        }

        public void PlayClip(AudioClip clip)
        {
            if (clip == null || _src == null) return;
            if (_timedRoutine != null)
            {
                StopCoroutine(_timedRoutine);
                _timedRoutine = null;
            }
            if (_src.loop) _src.Stop();
            _src.loop = false;
            _src.pitch = 1f + Random.Range(-pitchVariance, pitchVariance);
            _src.PlayOneShot(clip, volume);
        }

        /// <summary>Play a selected clip as a temporary loop, then stop it.</summary>
        public void PlayLoopForSeconds(float seconds)
        {
            if (clips == null || clips.Length == 0) return;
            int i = clips.Length == 1 ? 0 : Random.Range(0, clips.Length);
            PlayLoopForSeconds(clips[i], seconds);
        }

        public void PlayLoopForSeconds(AudioClip clip, float seconds)
        {
            if (clip == null || _src == null) return;
            if (_timedRoutine != null) StopCoroutine(_timedRoutine);

            _src.Stop();
            _src.clip = clip;
            _src.loop = true;
            _src.volume = volume;
            _src.pitch = 1f + Random.Range(-pitchVariance, pitchVariance);
            _src.Play();
            _timedRoutine = StartCoroutine(StopAfter(Mathf.Max(0f, seconds)));
        }

        public void Stop()
        {
            if (_timedRoutine != null)
            {
                StopCoroutine(_timedRoutine);
                _timedRoutine = null;
            }
            if (_src == null) return;
            _src.Stop();
            _src.loop = false;
        }

        private IEnumerator StopAfter(float seconds)
        {
            if (seconds > 0f)
                yield return new WaitForSeconds(seconds);
            Stop();
        }

        /// <summary>
        /// Spawn a temporary AudioSource at a world position so the cue persists
        /// even if the source GameObject is destroyed (e.g. trashed food prefab).
        /// </summary>
        public static void PlayAt(AudioClip clip, Vector3 worldPos, float volume = 1f,
                                   AudioMixerGroup mixer = null, float min = 2f, float max = 20f)
        {
            if (clip == null) return;
            var go = new GameObject($"OneShot_{clip.name}");
            go.transform.position = worldPos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.spatialBlend = 1f;
            src.minDistance = min;
            src.maxDistance = max;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.outputAudioMixerGroup = mixer;
            src.Play();
            Object.Destroy(go, clip.length + 0.2f);
        }
    }
}
