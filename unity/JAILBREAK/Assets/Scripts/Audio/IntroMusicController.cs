using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Jailbreak.Audio
{
    /// <summary>
    /// Persistent intro music that survives the menu flow
    /// (StartScene → LobbyScene → RoomScene) and fades out when a scene outside
    /// <see cref="persistInScenes"/> loads (e.g. Tutorial / GameScene).
    ///
    /// On WebGL the first <c>Play()</c> is deferred until the user makes any
    /// input — browsers keep the AudioContext suspended until a user gesture,
    /// so calling Play() at scene start would play silently.
    /// </summary>
    [RequireComponent(typeof(LoopingSfx))]
    public class IntroMusicController : MonoBehaviour
    {
        [Tooltip("Scenes where the intro music keeps playing. Loading any scene " +
                 "not in this list fades the loop out and destroys this object.")]
        public List<string> persistInScenes = new List<string>
        {
            "StartScene",
            "LobbyScene",
            "RoomScene",
        };

        [Tooltip("Seconds to fade out when leaving the persistent scene set.")]
        public float fadeOutOnExitSeconds = 1.0f;

        private static IntroMusicController _instance;

        private LoopingSfx _loop;
        private bool _waitingForUserGesture;

        private void Awake()
        {
            // Singleton: if StartScene reloads we keep the original and discard the dup.
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            // Detach from any parent so DontDestroyOnLoad applies cleanly.
            if (transform.parent != null) transform.SetParent(null, worldPositionStays: false);
            DontDestroyOnLoad(gameObject);

            _loop = GetComponent<LoopingSfx>();
            _loop.spatial = LoopingSfx.Spatial.Two_D;
        }

        private void OnEnable()
        {
            if (_instance != this) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            BeginPlayback();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void BeginPlayback()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browser autoplay policy: AudioContext is suspended until first gesture.
            _waitingForUserGesture = true;
#else
            _loop.Play();
#endif
        }

        private void Update()
        {
            if (!_waitingForUserGesture) return;
            if (DetectAnyUserGesture())
            {
                _waitingForUserGesture = false;
                _loop.Play();
            }
        }

        private static bool DetectAnyUserGesture()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;

            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame
                                || mouse.rightButton.wasPressedThisFrame
                                || mouse.middleButton.wasPressedThisFrame))
                return true;

            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) return true;

            return false;
#else
            return Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.touchCount > 0;
#endif
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            if (persistInScenes.Contains(scene.name)) return;

            _loop.fadeOutSeconds = fadeOutOnExitSeconds;
            _loop.Stop();
            // Destroy after the fade tail so OnDisable's hard-stop doesn't cut it.
            Destroy(gameObject, fadeOutOnExitSeconds + 0.1f);
        }
    }
}
