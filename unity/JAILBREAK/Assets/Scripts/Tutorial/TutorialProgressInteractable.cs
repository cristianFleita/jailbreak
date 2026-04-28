using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Tutorial
{
    /// <summary>
    /// Tutorial-only progress interactable. Press E once to start filling a
    /// timed bar; the bar fills as long as the player keeps E held AND stays
    /// inside the trigger that hosts this component. Releasing E or leaving
    /// the area cancels and resets the bar.
    ///
    /// Designed for the desk-search and power-supply missions, which need a
    /// visible 2–6s commitment without the brittleness of the main game's
    /// ProgressPointAction (no animator coupling, no CharacterController
    /// freeze, no actionPoint warping). On completion the configured signal
    /// is emitted exactly once.
    /// </summary>
    public class TutorialProgressInteractable : MonoBehaviour, IInteractable
    {
        [Header("Prompt")]
        [SerializeField] private KeyCode interactKey = KeyCode.E;
        [SerializeField] private string startLabel = "Search";
        [SerializeField] private string activeLabel = "Searching...";
        [SerializeField] private int priority = 20;

        [Header("Role Gate")]
        [Tooltip("'prisoner', 'guard', or 'any'. Defaults to prisoner.")]
        [SerializeField] private string requiredRole = "prisoner";

        [Header("Progress")]
        [Tooltip("Seconds the player must stay engaged for the action to complete.")]
        [SerializeField] private float durationSeconds = 3f;
        [Tooltip("Max distance the player can be from this object before the bar cancels.")]
        [SerializeField] private float cancelDistance = 2.5f;
        [SerializeField] private ProgressBar progressBar;
        [SerializeField] private string progressLabel = "Working...";
        [SerializeField] private bool oneShot = true;

        [Header("Mission Signals")]
        [SerializeField] private string signalOnComplete;
        [SerializeField] private string toastOnComplete;
        [SerializeField] private string worldCueLabel;
        [SerializeField] private bool emitWorldCueOnComplete = true;

        [Header("Scene Changes")]
        [SerializeField] private GameObject[] enableOnComplete;
        [SerializeField] private GameObject[] disableOnComplete;

        public KeyCode InteractKey => interactKey;
        public string ActionLabel => _isActive ? activeLabel : startLabel;
        public int Priority => priority;
        public Transform Transform => transform;
        // We allow the prompt to keep showing while the action is active so
        // releasing E reads as "stop" via TrackProgress, but the player isn't
        // forced to press again — they just keep holding.
        public bool CanInteract => !_completed && IsRoleAllowed();
        public string[] AllowedInStates => null;

        private bool _isActive;
        private bool _completed;
        private float _progress;
        private InteractionManager _activeManager;

        private void Update()
        {
            TrackProgress();
        }

        public void OnInteract(Collider source)
        {
            if (_completed || _isActive) return;
            if (source == null) return;
            if (!IsRoleAllowed()) return;

            _activeManager = source.transform.root.GetComponentInChildren<InteractionManager>();
            BeginProgress();
        }

        private void BeginProgress()
        {
            _isActive = true;
            _progress = 0f;
            progressBar?.Show(progressLabel, 0f);
        }

        // Accumulates progress while the player keeps E held AND stays close.
        // Releasing the key, or walking outside `cancelDistance`, cancels and
        // resets the bar. This avoids the previous bug where the bar kept
        // filling silently after the player wandered off because the original
        // implementation never checked range.
        private void TrackProgress()
        {
            if (!_isActive) return;

            var keyHeld = InputSystemKey.IsPressed(interactKey);
            var inRange = _activeManager == null
                || Vector3.Distance(_activeManager.transform.position, transform.position) <= cancelDistance;

            if (!keyHeld || !inRange)
            {
                CancelProgress();
                return;
            }

            _progress += Time.deltaTime / Mathf.Max(0.1f, durationSeconds);
            _progress = Mathf.Clamp01(_progress);
            progressBar?.UpdateProgress(_progress);

            if (_progress >= 1f)
                CompleteProgress();
        }

        private void CancelProgress()
        {
            _isActive = false;
            _progress = 0f;
            progressBar?.Hide();
        }

        private void CompleteProgress()
        {
            _isActive = false;
            progressBar?.Hide();

            if (oneShot) _completed = true;

            ApplySceneChanges();

            if (!string.IsNullOrEmpty(signalOnComplete))
                TutorialMissionEvents.Emit(signalOnComplete);

            if (emitWorldCueOnComplete)
                TutorialMissionEvents.EmitWorldCue(transform.position, worldCueLabel);

            if (!string.IsNullOrEmpty(toastOnComplete))
                TutorialMissionEvents.Toast(toastOnComplete);

            _progress = 0f;
        }

        private bool IsRoleAllowed()
        {
            if (string.IsNullOrEmpty(requiredRole) || requiredRole == "any") return true;
            var start = NetworkManager.Instance != null ? NetworkManager.Instance.CachedTutorialStart : null;
            if (start == null || string.IsNullOrEmpty(start.role)) return true;
            return string.Equals(start.role, requiredRole, System.StringComparison.OrdinalIgnoreCase);
        }

        private void ApplySceneChanges()
        {
            if (enableOnComplete != null)
                foreach (var go in enableOnComplete)
                    if (go != null) go.SetActive(true);

            if (disableOnComplete != null)
                foreach (var go in disableOnComplete)
                    if (go != null) go.SetActive(false);
        }
    }
}
