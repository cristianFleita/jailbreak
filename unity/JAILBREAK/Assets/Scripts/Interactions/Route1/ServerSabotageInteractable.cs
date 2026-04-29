using Jailbreak.Network;
using Jailbreak.Audio;
using UnityEngine;

namespace Jailbreak.Interactions.Route1
{
    public class ServerSabotageInteractable : Route1ProgressInteractable
    {
        protected override string StartAction => "route1.disable_server.start";
        protected override string StopAction => "route1.disable_server.stop";
        protected override string StateAction => "route1.disable_server";
        protected override string DefaultProgressLabel => "Sabotaging server...";
        protected override string DefaultStartLabel => "Sabotage";
        protected override float DefaultDurationSeconds => 15f;

        protected override bool IsAvailable(EscapeRoute1StatePayload state)
        {
            return state == null || !state.serverDisabled;
        }

        protected override string UnavailableMessage(EscapeRoute1StatePayload state)
        {
            return state != null && state.serverDisabled ? "Ventilation is already off" : null;
        }

        protected override void OnLocalInteractionCompletedWithoutState(EscapeRoute1StatePayload state)
        {
            if (state != null && state.serverDisabled) return;

#if UNITY_2023_1_OR_NEWER
            var bridge = Object.FindFirstObjectByType<GameAudioBridge>(FindObjectsInactive.Exclude);
#else
            var bridge = Object.FindObjectOfType<GameAudioBridge>();
#endif
            bridge?.PlayWrongPowerSupplyAlarmCue();
        }
    }
}
