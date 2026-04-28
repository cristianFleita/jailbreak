using System.Collections;
using UnityEngine;

namespace Jailbreak.Tutorial
{
    public class TutorialMissionTracker : MonoBehaviour
    {
        [SerializeField] private float sprintSecondsRequired = 1f;
        [SerializeField] private float hideSecondsRequired = 3f;

        private InteractionManager _interactionManager;
        private ItemInventory _inventory;
        private HeldItemInput _heldItemInput;
        private CarryFoodInteraction _carryFood;
        private SitInteraction _sit;
        private float _sprintTimer;
        private bool _sprintCompleted;
        private bool _hidePending;

        // Edge-detection state for the food/sit/sink flow. We poll the actual
        // gameplay components instead of OnLocalInteract — the latter fires on
        // press (before the progress bar resolves) and on stand-up, both of
        // which produced false-positive "blend in" signals before.
        private bool _wasCarryingFood;
        private bool _wasSitting;
        private bool _hasCarriedFoodOnce;

        private void Awake()
        {
            _interactionManager = GetComponentInChildren<InteractionManager>();
            _inventory = GetComponent<ItemInventory>();
            _heldItemInput = GetComponent<HeldItemInput>();
            _carryFood = GetComponentInChildren<CarryFoodInteraction>();
            _sit = GetComponentInChildren<SitInteraction>();
        }

        private void OnEnable()
        {
            if (_interactionManager != null)
                _interactionManager.OnLocalInteract += HandleLocalInteract;
            if (_inventory != null)
            {
                _inventory.onSlotSelected.AddListener(HandleSlotSelected);
                _inventory.onItemAdded.AddListener(HandleItemAdded);
            }
            if (_heldItemInput != null)
            {
                _heldItemInput.onItemAttached.AddListener(HandleItemAttached);
                _heldItemInput.onItemStored.AddListener(HandleItemStored);
            }
        }

        private void OnDisable()
        {
            if (_interactionManager != null)
                _interactionManager.OnLocalInteract -= HandleLocalInteract;
            if (_inventory != null)
            {
                _inventory.onSlotSelected.RemoveListener(HandleSlotSelected);
                _inventory.onItemAdded.RemoveListener(HandleItemAdded);
            }
            if (_heldItemInput != null)
            {
                _heldItemInput.onItemAttached.RemoveListener(HandleItemAttached);
                _heldItemInput.onItemStored.RemoveListener(HandleItemStored);
            }
        }

        private void Update()
        {
            TrackSprint();
            TrackBlendInFlow();
        }

        // Detects rising/falling edges on the actual gameplay state. This makes
        // the P2 mission gates fire only when the player truly carried food,
        // sat down, and then dropped the tray at the sink — never on a stray E
        // press that didn't resolve.
        private void TrackBlendInFlow()
        {
            var carrying = _carryFood != null && _carryFood.IsCarrying;
            if (carrying && !_wasCarryingFood)
            {
                _hasCarriedFoodOnce = true;
                TutorialMissionEvents.Emit(TutorialMissionEvents.FoodPicked);
            }
            else if (!carrying && _wasCarryingFood && _hasCarriedFoodOnce)
            {
                TutorialMissionEvents.Emit(TutorialMissionEvents.TrayDeposited);
            }
            _wasCarryingFood = carrying;

            var sitting = _sit != null && _sit.IsSitting;
            if (sitting && !_wasSitting)
                TutorialMissionEvents.Emit(TutorialMissionEvents.Seated);
            _wasSitting = sitting;
        }

        private void TrackSprint()
        {
            if (_sprintCompleted) return;

            var sprinting = InputSystemKey.IsPressed(KeyCode.LeftShift)
                || InputSystemKey.IsPressed(KeyCode.RightShift);
            var moving = InputSystemKey.IsPressed(KeyCode.W)
                || InputSystemKey.IsPressed(KeyCode.A)
                || InputSystemKey.IsPressed(KeyCode.S)
                || InputSystemKey.IsPressed(KeyCode.D)
                || InputSystemKey.IsPressed(KeyCode.UpArrow)
                || InputSystemKey.IsPressed(KeyCode.DownArrow)
                || InputSystemKey.IsPressed(KeyCode.LeftArrow)
                || InputSystemKey.IsPressed(KeyCode.RightArrow);

            if (sprinting && moving)
            {
                _sprintTimer += Time.deltaTime;
                if (_sprintTimer >= sprintSecondsRequired)
                {
                    _sprintCompleted = true;
                    TutorialMissionEvents.Emit(TutorialMissionEvents.Sprinted);
                }
            }
            else
            {
                _sprintTimer = 0f;
            }
        }

        private void HandleLocalInteract(IInteractable interactable)
        {
            // Food / sit / sink are intentionally NOT handled here — those
            // signals are driven by TrackBlendInFlow (state-edge polling) so
            // they only fire when the action actually resolved.
            switch (interactable)
            {
                case PickUpInteractable:
                case NetworkRoutePickable:
                    TutorialMissionEvents.Emit(TutorialMissionEvents.ItemPicked);
                    break;
                case HideInteractable:
                    StartHideCompletionTimer();
                    break;
            }
        }

        private void StartHideCompletionTimer()
        {
            if (_hidePending) return;
            _hidePending = true;
            StartCoroutine(HideCompletionRoutine());
        }

        private IEnumerator HideCompletionRoutine()
        {
            yield return new WaitForSeconds(hideSecondsRequired);
            _hidePending = false;
            TutorialMissionEvents.Emit(TutorialMissionEvents.HiddenInCart);
        }

        private void HandleSlotSelected(int _)
        {
            TutorialMissionEvents.Emit(TutorialMissionEvents.SlotChanged);
        }

        private void HandleItemAdded(int _, PickableItem item)
        {
            if (item != null)
                TutorialMissionEvents.Emit(TutorialMissionEvents.ItemStored);
        }

        private void HandleItemAttached(PickableItem item)
        {
            if (item != null)
                TutorialMissionEvents.Emit(TutorialMissionEvents.ItemPicked);
        }

        private void HandleItemStored(PickableItem item)
        {
            if (item != null)
                TutorialMissionEvents.Emit(TutorialMissionEvents.ItemStored);
        }
    }
}
