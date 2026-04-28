using UnityEngine;
using UnityEngine.InputSystem;

namespace Jailbreak.Tutorial
{
    /// <summary>
    /// Tutorial-only guard capture. Does NOT use Physics.Raycast + LayerMask because
    /// tutorial NPCs may be on any layer. Instead it scans for TutorialCaptureTarget
    /// components within range, then uses a dot-product aim check against the FPS
    /// camera forward — identical strategy to GuardCaptureSystem but layer-agnostic.
    /// </summary>
    public class TutorialGuardCaptureController : MonoBehaviour
    {
        [SerializeField] private Camera fpsCamera;
        [SerializeField] private float captureRange = 2.0f;
        [SerializeField] private float captureFocusTime = 0.5f;
        [SerializeField] private float anomalyRange = 4.0f;
        [SerializeField] private int mistakeThreshold = 3;
        [SerializeField] private float routineObserveSeconds = 3f;
        [Tooltip("Min dot product between camera forward and direction to target. 0.5 = 60° cone, 0.85 = ~30° cone.")]
        [SerializeField] private float aimDotThreshold = 0.5f;

        private TutorialCaptureTarget _currentTarget;
        private float _focusTimer;
        private float _observeTimer;
        private int _mistakes;

        private void Awake()
        {
            if (fpsCamera == null) fpsCamera = ResolveCamera();
        }

        private void Update()
        {
            if (fpsCamera == null) fpsCamera = ResolveCamera();
            if (fpsCamera == null) return;

            TrackObservation();
            TrackAnomalyRange();
            TrackCaptureInput();
        }

        /// <summary>Called by TutorialSceneController right after AddComponent.</summary>
        public void Configure(Camera cameraOverride, LayerMask targets, float range, float focusTime)
        {
            if (cameraOverride != null) fpsCamera = cameraOverride;
            // LayerMask is intentionally ignored — detection uses FindObjectsByType<TutorialCaptureTarget>
            // so it works regardless of which physics layer the NPC prefab uses.
            captureRange = range > 0f ? range : captureRange;
            captureFocusTime = focusTime > 0f ? focusTime : captureFocusTime;
        }

        private Camera ResolveCamera()
        {
            // Prefer MainCamera-tagged camera among children (includes inactive objects)
            foreach (var cam in GetComponentsInChildren<Camera>(true))
                if (cam.CompareTag("MainCamera")) return cam;
            return GetComponentInChildren<Camera>(true);
        }

        // --- Detection -------------------------------------------------------

        private TutorialCaptureTarget[] AllTargets()
        {
#if UNITY_2023_1_OR_NEWER
            return FindObjectsByType<TutorialCaptureTarget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            return FindObjectsOfType<TutorialCaptureTarget>();
#endif
        }

        /// <summary>
        /// Returns the TutorialCaptureTarget most aligned with the camera forward
        /// that is within <paramref name="range"/> metres. Returns null if none passes
        /// the aim-dot threshold.
        /// </summary>
        private TutorialCaptureTarget AimedTarget(float range)
        {
            if (fpsCamera == null) return null;

            var camPos = fpsCamera.transform.position;
            var camFwd = fpsCamera.transform.forward;

            TutorialCaptureTarget best = null;
            float bestDot = aimDotThreshold;

            foreach (var t in AllTargets())
            {
                if (t == null) continue;
                // Skip own hierarchy (guard player avatar)
                if (t.transform.root == transform.root) continue;

                var toTarget = t.transform.position - camPos;
                float dist = toTarget.magnitude;
                if (dist > range) continue;

                float dot = Vector3.Dot(camFwd, toTarget / dist);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = t;
                }
            }

            return best;
        }

        // --- Mission tracking ------------------------------------------------

        private void TrackObservation()
        {
            var t = AimedTarget(12f);
            if (t != null && t.routineNpc)
            {
                _observeTimer += Time.deltaTime;
                if (_observeTimer >= routineObserveSeconds)
                    TutorialMissionEvents.Emit(TutorialMissionEvents.GuardObservedRoutine);
            }
            else
            {
                _observeTimer = 0f;
            }
        }

        private void TrackAnomalyRange()
        {
            foreach (var t in AllTargets())
            {
                if (t == null || !t.suspiciousTarget) continue;
                if (Vector3.Distance(transform.position, t.transform.position) <= anomalyRange)
                {
                    TutorialMissionEvents.Emit(TutorialMissionEvents.GuardNearAnomaly);
                    return;
                }
            }
        }

        private void TrackCaptureInput()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.isPressed)
            {
                ResetFocus();
                return;
            }

            var target = AimedTarget(captureRange);
            if (target == null)
            {
                ResetFocus();
                return;
            }

            if (_currentTarget != target)
            {
                _currentTarget = target;
                _focusTimer = 0f;
            }

            _focusTimer += Time.deltaTime;
            TutorialMissionEvents.SetGuardFocus(_focusTimer / captureFocusTime);

            if (_focusTimer >= captureFocusTime)
                CompleteCapture(target);
        }

        private void CompleteCapture(TutorialCaptureTarget target)
        {
            if (target.correctCaptureTarget)
            {
                TutorialMissionEvents.Emit(TutorialMissionEvents.GuardCaptureComplete);
                TutorialMissionEvents.Toast($"Captured {target.displayName}");
            }
            else
            {
                _mistakes = Mathf.Min(mistakeThreshold, _mistakes + 1);
                TutorialMissionEvents.SetGuardErrors(_mistakes, mistakeThreshold);
                TutorialMissionEvents.Emit(TutorialMissionEvents.GuardMistake);
                TutorialMissionEvents.Toast(_mistakes >= mistakeThreshold
                    ? "Practice defeat: 3 wrong accusations would trigger a riot."
                    : $"Wrong accusation. Mistakes {_mistakes}/{mistakeThreshold}");
            }

            ResetFocus();
        }

        private void ResetFocus()
        {
            _currentTarget = null;
            _focusTimer = 0f;
            TutorialMissionEvents.SetGuardFocus(0f);
        }

    }
}
