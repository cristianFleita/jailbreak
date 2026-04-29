using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Audio
{
    /// <summary>
    /// Subscribes to authoritative <see cref="NetworkManager"/> events and triggers
    /// the matching audio cues. Place exactly one in GameScene.
    ///
    /// Mapped events:
    ///   guard:catch (success=false) → wrong-mark one-shot + start security alarm loop
    ///   riot:available              → ambience switches to "riot" + alarm stays on
    ///   game:end                    → mission-completed one-shot + alarm/ambience stop
    ///
    /// All references are optional — leave any field empty to skip that cue.
    /// </summary>
    public class GameAudioBridge : MonoBehaviour
    {
        [Header("Wrong Mark / Catch Error")]
        public OneShotSfx wrongMarkOneShot;

        [Header("Security Alarm (loop)")]
        [Tooltip("Looped alarm. Starts on first wrong mark, stops on game-end.")]
        public LoopingSfx securityAlarmLoop;

        [Tooltip("Trigger alarm only after at least N guard errors (1 = first error).")]
        [Range(1, 5)] public int alarmStartsAtErrorCount = 1;

        [Header("Riot")]
        public CellAmbienceController ambience;

        [Header("Mission Completed")]
        public OneShotSfx missionCompleteOneShot;

        [Tooltip("Play mission-complete cue when game:end arrives. " +
                 "Disable if you want to fire it manually from a UnityEvent.")]
        public bool playMissionCompleteOnGameEnd = true;

        private bool _alarmActive;
        private bool _subscribed;

        private void OnEnable() => SubscribeIfReady();
        private void Start()    => SubscribeIfReady();

        private void OnDisable()
        {
            if (!_subscribed) return;
            var net = NetworkManager.Instance;
            if (net != null)
            {
                net.OnGuardCatchResultEvent -= OnGuardCatchResult;
                net.OnRiotAvailableEvent    -= OnRiotAvailable;
                net.OnGameEndEvent          -= OnGameEnd;
            }
            _subscribed = false;
        }

        private void SubscribeIfReady()
        {
            if (_subscribed) return;
            var net = NetworkManager.Instance;
            if (net == null) return;

            net.OnGuardCatchResultEvent += OnGuardCatchResult;
            net.OnRiotAvailableEvent    += OnRiotAvailable;
            net.OnGameEndEvent          += OnGameEnd;
            _subscribed = true;
        }

        // ── Network handlers ──────────────────────────────────────────────────

        private void OnGuardCatchResult(GuardCatchPayload p)
        {
            if (p == null) return;

            // success=false means the guard caught the wrong target (an NPC or innocent).
            if (!p.success)
            {
                if (wrongMarkOneShot != null) wrongMarkOneShot.Play();

                if (!_alarmActive && p.guardErrorCount >= alarmStartsAtErrorCount && securityAlarmLoop != null)
                {
                    securityAlarmLoop.Play();
                    _alarmActive = true;
                }
            }
        }

        private void OnRiotAvailable(RiotAvailablePayload p)
        {
            if (ambience != null) ambience.SetRiot(true);
            if (!_alarmActive && securityAlarmLoop != null)
            {
                securityAlarmLoop.Play();
                _alarmActive = true;
            }
        }

        private void OnGameEnd(GameEndPayload p)
        {
            if (_alarmActive && securityAlarmLoop != null)
            {
                securityAlarmLoop.Stop();
                _alarmActive = false;
            }
            if (ambience != null) ambience.SetRiot(false);

            if (playMissionCompleteOnGameEnd && missionCompleteOneShot != null)
                missionCompleteOneShot.Play();
        }

        // ── Public hooks (wire from UnityEvents) ──────────────────────────────

        public void PlayMissionComplete()
        {
            if (missionCompleteOneShot != null) missionCompleteOneShot.Play();
        }

        public void StopAlarm()
        {
            if (securityAlarmLoop != null) securityAlarmLoop.Stop();
            _alarmActive = false;
        }
    }
}
