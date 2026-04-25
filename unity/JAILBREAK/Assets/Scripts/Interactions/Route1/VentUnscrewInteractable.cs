using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak.Interactions.Route1
{
    public class VentUnscrewInteractable : Route1ProgressInteractable
    {
        [Header("Vent Visuals")]
        public GameObject closedVisual;
        public GameObject openVisual;
        public Animator ventAnimator;
        public string openAnimatorBool = "isOpen";
        public bool hideClosedVisualWhenOpen = true;
        public bool showOpenVisualWhenOpen = false;
        public bool disableAssignedCollidersWhenOpen = true;
        public Collider[] collidersToDisableWhenOpen;

        protected override string StartAction => "route1.open_vent.start";
        protected override string StopAction => "route1.open_vent.stop";
        protected override string StateAction => "route1.open_vent";
        protected override string DefaultProgressLabel => "Opening vent...";
        protected override string DefaultStartLabel => "Unscrew";
        protected override float DefaultDurationSeconds => 25f;

        protected override bool IsAvailable(EscapeRoute1StatePayload state)
        {
            if (state == null) return false;
            return state.serverDisabled && !IsVentOpen(state, RouteObjectId);
        }

        protected override string UnavailableMessage(EscapeRoute1StatePayload state)
        {
            if (state == null) return null;
            if (!state.serverDisabled) return "Disable ventilation first";
            return IsVentOpen(state, RouteObjectId) ? "This vent is already open" : null;
        }

        protected override float GetInitialProgress(EscapeRoute1StatePayload state)
        {
            var active = base.GetInitialProgress(state);
            return active > 0f ? active : GetVentProgress(state, RouteObjectId);
        }

        protected override void OnServerStateApplied(EscapeRoute1StatePayload state)
        {
            SetOpenVisual(IsVentOpen(state, RouteObjectId));
        }

        public void SetOpenVisual(bool isOpen)
        {
            SetAssignedCollidersEnabled(!isOpen || !disableAssignedCollidersWhenOpen);

            if (closedVisual != null)
                closedVisual.SetActive(!isOpen || !hideClosedVisualWhenOpen);

            if (openVisual != null && openVisual != closedVisual)
                openVisual.SetActive(isOpen && showOpenVisualWhenOpen);

            if (ventAnimator != null && !string.IsNullOrEmpty(openAnimatorBool))
                ventAnimator.SetBool(openAnimatorBool, isOpen);
        }

        private void SetAssignedCollidersEnabled(bool enabled)
        {
            if (collidersToDisableWhenOpen == null) return;
            foreach (var c in collidersToDisableWhenOpen)
                if (c != null) c.enabled = enabled;
        }
    }
}
