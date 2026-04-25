using Jailbreak.Network;

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
            return state != null && state.serverDisabled ? "La ventilacion ya esta apagada" : null;
        }
    }
}
