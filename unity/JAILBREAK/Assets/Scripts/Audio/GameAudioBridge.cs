using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Audio
{
    /// <summary>
    /// Subscribes to authoritative <see cref="NetworkManager"/> events and triggers
    /// the matching audio cues. Place exactly one in GameScene.
    ///
    /// Mapped events:
    ///   guard:catch (success=false)        → wrong-mark one-shot + start security alarm loop
    ///   riot:available                     → ambience switches to "riot" + alarm stays on
    ///   world:cue (server_wrong_alarm)     → wrong-server one-shot + start security alarm loop
    ///   game:end                           → mission-completed one-shot + alarm/ambience stop
    ///
    /// All references are optional — leave any field empty to skip that cue.
    /// </summary>
    public class GameAudioBridge : MonoBehaviour
    {
        [Header("Wrong Mark / Catch Error")]
        public OneShotSfx wrongMarkOneShot;

        [Header("Wrong Power Supply Server (optional override)")]
        [Tooltip("Played when a prisoner sabotages the WRONG transformer. " +
                 "Leave empty to reuse wrongMarkOneShot.")]
        public OneShotSfx wrongServerOneShot;

        [Header("Security Alarm (loop)")]
        [Tooltip("Looped alarm. Starts on first wrong mark or wrong-server, stops on game-end.")]
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

        [Header("Debug")]
        public bool debugLogs = false;

        private bool _alarmActive;
        private bool _subscribed;

        private void OnEnable() => SubscribeIfReady();
        private void Start()    => SubscribeIfReady();

        // Boot-order race: NetworkManager.Instance may not be live yet on
        // OnEnable/Start. Keep polling until we successfully subscribe so
        // we don't silently miss every event for the rest of the session.
        private void Update()
        {
            if (!_subscribed) SubscribeIfReady();
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            var net = NetworkManager.Instance;
            if (net != null)
            {
                net.OnGuardCatchResultEvent -= OnGuardCatchResult;
                net.OnRiotAvailableEvent    -= OnRiotAvailable;
                net.OnGameEndEvent          -= OnGameEnd;
                net.OnWorldCueEvent         -= OnWorldCue;
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
            net.OnWorldCueEvent         += OnWorldCue;
            _subscribed = true;

            if (debugLogs) Debug.Log("[GameAudioBridge] Subscribed to NetworkManager events.", this);
        }

        // ── Network handlers ──────────────────────────────────────────────────

        private void OnGuardCatchResult(GuardCatchPayload p)
        {
            if (p == null) return;

            if (debugLogs)
                Debug.Log($"[GameAudioBridge] guard:catch success={p.success} errorCount={p.guardErrorCount}", this);

            if (p.success) return;

            // Wrong target (NPC / innocent).
            if (wrongMarkOneShot != null) wrongMarkOneShot.Play();

            if (!_alarmActive
                && p.guardErrorCount >= alarmStartsAtErrorCount
                && securityAlarmLoop != null)
            {
                securityAlarmLoop.Play();
                _alarmActive = true;
            }
        }

        private void OnRiotAvailable(RiotAvailablePayload p)
        {
            if (debugLogs) Debug.Log("[GameAudioBridge] riot:available", this);

            if (ambience != null) ambience.SetRiot(true);
            if (!_alarmActive && securityAlarmLoop != null)
            {
                securityAlarmLoop.Play();
                _alarmActive = true;
            }
        }

        private void OnWorldCue(WorldCuePayload p)
        {
            if (p == null || string.IsNullOrEmpty(p.cue)) return;

            if (debugLogs) Debug.Log($"[GameAudioBridge] world:cue {p.cue} zone={p.zone}", this);

            // Wrong transformer sabotage → fires the same "you screwed up" SFX
            // as a wrong mark, plus starts the security alarm loop. The local
            // server prop's own SFX is handled separately by Route1WorldStateController.
            if (p.cue == "server_wrong_alarm")
            {
                var oneShot = wrongServerOneShot != null ? wrongServerOneShot : wrongMarkOneShot;
                if (oneShot != null) oneShot.Play();

                if (!_alarmActive && securityAlarmLoop != null)
                {
                    securityAlarmLoop.Play();
                    _alarmActive = true;
                }
            }
        }

        private void OnGameEnd(GameEndPayload p)
        {
            if (debugLogs) Debug.Log("[GameAudioBridge] game:end", this);

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
