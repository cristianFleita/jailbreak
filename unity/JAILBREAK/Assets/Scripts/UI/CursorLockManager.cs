using System.Collections.Generic;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Jailbreak.UI
{
    /// <summary>
    /// Refcount gate for cursor lock state. Anything that wants to free the
    /// cursor (pause menu, chat, settings, end-game UI) calls
    /// <see cref="RequestUnlock"/> with a unique token and later releases it.
    ///
    /// FPSCameraController consults <see cref="ShouldBeLocked"/> every frame
    /// instead of unconditionally re-locking, which is what made the pause
    /// menu feel like the cursor "snapped back" before this was added.
    ///
    /// On WebGL, the browser's Pointer Lock API is sticky — `Cursor.lockState`
    /// is a request, not a command. We additionally call
    /// `document.exitPointerLock()` via a jslib bridge AND keep re-applying
    /// for a short window after the lock state flips, to defeat the race
    /// where pointer lock survives a scene transition and traps the cursor
    /// in the lobby.
    /// </summary>
    public static class CursorLockManager
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void JbReleasePointerLock();
#endif

        private static readonly HashSet<object> _unlockTokens = new();

        // Number of frames the runner keeps re-asserting the desired state
        // after every change. Two-digit count is enough for WebGL pointer lock
        // to actually settle even with browser-side queuing.
        private const int EnforceFrames = 30;
        private static int _framesRemaining;

        /// <summary>True when no consumer is currently asking for the cursor to be free.</summary>
        public static bool ShouldBeLocked => _unlockTokens.Count == 0;

        /// <summary>
        /// Reserve the cursor as visible/free for as long as <paramref name="token"/>
        /// holds the slot. The token is typically `this` — pass the same instance
        /// to <see cref="ReleaseUnlock"/>. Re-requesting with the same token is a no-op.
        /// </summary>
        public static void RequestUnlock(object token)
        {
            if (token == null) return;
            if (_unlockTokens.Add(token))
            {
                ApplyState();
                StartEnforcement();
            }
        }

        /// <summary>
        /// Drop a previously requested unlock. The cursor relocks once every
        /// outstanding token has been released.
        /// </summary>
        public static void ReleaseUnlock(object token)
        {
            if (token == null) return;
            if (_unlockTokens.Remove(token))
            {
                ApplyState();
                StartEnforcement();
            }
        }

        /// <summary>
        /// Push the chosen lock state to Unity now. FPSCameraController also
        /// re-applies during LateUpdate, but immediate apply prevents the
        /// 1-frame "still locked" flicker when a pause menu opens.
        /// </summary>
        public static void ApplyState()
        {
            if (ShouldBeLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                ForceBrowserPointerUnlock();
            }
        }

        /// <summary>
        /// Browser-level pointer lock release. No-op outside WebGL builds.
        /// Safe to call every frame — the JS side guards against double calls.
        /// </summary>
        public static void ForceBrowserPointerUnlock()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { JbReleasePointerLock(); }
            catch { /* bridge missing in editor / older builds */ }
#endif
        }

        private static void StartEnforcement()
        {
            _framesRemaining = EnforceFrames;
            EnforcerRunner.Ensure();
        }

        // Hidden DontDestroyOnLoad MonoBehaviour that re-asserts the chosen
        // lock state every frame for a short window after a change. Survives
        // scene transitions, which is the whole point — the lobby's
        // `RequestUnlock` would otherwise be undone by a stale browser
        // pointer lock the very next frame.
        private sealed class EnforcerRunner : MonoBehaviour
        {
            private static EnforcerRunner _instance;

            public static void Ensure()
            {
                if (_instance != null) return;
                var go = new GameObject("[CursorLockEnforcer]") { hideFlags = HideFlags.HideAndDontSave };
                _instance = go.AddComponent<EnforcerRunner>();
                DontDestroyOnLoad(go);
            }

            private void LateUpdate()
            {
                if (_framesRemaining <= 0) return;
                _framesRemaining--;
                ApplyState();
            }
        }
    }
}
