using Jailbreak.Network;

namespace Jailbreak.Interactions.Route1
{
    public class GuardDeskClueInteractable : Route1ProgressInteractable
    {
        protected override string StartAction => "route1.search_clue.start";
        protected override string StopAction => "route1.search_clue.stop";
        protected override string StateAction => "route1.search_clue";
        protected override string DefaultProgressLabel => "Searching desk...";
        protected override string DefaultStartLabel => "Search";
        protected override float DefaultDurationSeconds => 3f;

        protected override bool IsAvailable(EscapeRoute1StatePayload state)
        {
            return state == null || !state.clueFound;
        }
    }
}
