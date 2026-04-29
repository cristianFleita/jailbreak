using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Audio
{
    /// <summary>
    /// Subscribes to authoritative <see cref="NetworkManager"/> events and triggers
    /// the matching audio cues. Place exactly one in GameScene.
    ///
    /// Mapped events:
    ///   guard:catch (success=true,isPlayer=false) → angry NPC one-shot
    ///   riot:available                     → ambience switches to "riot" + alarm stays on
    ///   world:cue (server_wrong_alarm)     → wrong-server alarm for a short fixed duration
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
        [Tooltip("Optional looped alarm. If empty, wrongServerOneShot is looped temporarily instead.")]
        public LoopingSfx securityAlarmLoop;

        [Tooltip("How long the wrong power-supply alarm should be audible.")]
        [Min(0.1f)] public float wrongPowerSupplyAlarmSeconds = 4f;

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
        private Coroutine _alarmRoutine;

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
            StopAlarm();
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

            if (p.success && !p.isPlayer && wrongMarkOneShot != null)
                wrongMarkOneShot.Play();
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

            // Wrong transformer sabotage: guard hears a short alarm, not the
            // angry-NPC cue used for false captures.
            if (p.cue == "server_wrong_alarm")
            {
                PlayWrongPowerSupplyAlarm();
            }
        }

        private void OnGameEnd(GameEndPayload p)
        {
            if (debugLogs) Debug.Log("[GameAudioBridge] game:end", this);

            StopAlarm();
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
            if (_alarmRoutine != null)
            {
                StopCoroutine(_alarmRoutine);
                _alarmRoutine = null;
            }
            if (securityAlarmLoop != null) securityAlarmLoop.Stop();
            if (wrongServerOneShot != null) wrongServerOneShot.Stop();
            _alarmActive = false;
        }

        private void PlayWrongPowerSupplyAlarm()
        {
            if (securityAlarmLoop != null)
            {
                securityAlarmLoop.PlayForSeconds(wrongPowerSupplyAlarmSeconds);
                RestartAlarmTimer();
                return;
            }

            if (wrongServerOneShot != null)
            {
                wrongServerOneShot.PlayLoopForSeconds(wrongPowerSupplyAlarmSeconds);
                RestartAlarmTimer();
            }
        }

        private void RestartAlarmTimer()
        {
            _alarmActive = true;
            if (_alarmRoutine != null) StopCoroutine(_alarmRoutine);
            _alarmRoutine = StartCoroutine(ClearAlarmAfter(wrongPowerSupplyAlarmSeconds));
        }

        private System.Collections.IEnumerator ClearAlarmAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _alarmRoutine = null;
            _alarmActive = false;
        }
    }
}
