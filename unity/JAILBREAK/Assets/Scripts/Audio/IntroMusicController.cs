using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jailbreak.Audio
{
    /// <summary>
    /// Plays the intro music loop on StartScene. Fades out automatically when
    /// another scene loads (e.g. transition to LobbyScene/GameScene).
    /// Drop on the same GameObject as a <see cref="LoopingSfx"/>.
    /// </summary>
    [RequireComponent(typeof(LoopingSfx))]
    public class IntroMusicController : MonoBehaviour
    {
        [Tooltip("Seconds to fade out when leaving this scene.")]
        public float fadeOutOnSceneChangeSeconds = 1.0f;

        private LoopingSfx _loop;

        private void Awake()
        {
            _loop = GetComponent<LoopingSfx>();
            // Force 2D so it doesn't get attenuated by the start-screen camera position.
            _loop.spatial = LoopingSfx.Spatial.Two_D;
        }

        private void OnEnable()
        {
            _loop.Play();
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            _loop.Stop();
        }

        private void OnSceneUnloaded(Scene s)
        {
            _loop.fadeOutSeconds = fadeOutOnSceneChangeSeconds;
            _loop.Stop();
        }
    }
}
