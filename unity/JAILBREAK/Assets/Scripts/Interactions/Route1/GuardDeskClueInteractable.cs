using Jailbreak.Network;

namespace Jailbreak.Interactions.Route1
{
    public class GuardDeskClueInteractable : Route1ProgressInteractable
    {
        private bool localSearchStartedBeforeClue;

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

        protected override string UnavailableMessage(EscapeRoute1StatePayload state)
        {
            return state != null && state.clueFound ? "La pista ya fue encontrada" : null;
        }

        protected override void OnLocalInteractionStarted(EscapeRoute1StatePayload state)
        {
            localSearchStartedBeforeClue = state == null || !state.clueFound;
        }

        protected override void OnLocalInteractionCompletedWithoutState(EscapeRoute1StatePayload state)
        {
            if (localSearchStartedBeforeClue && (state == null || !state.clueFound))
                ShowRouteFeedback("No hay nada en este escritorio");

            localSearchStartedBeforeClue = false;
        }
    }
}
