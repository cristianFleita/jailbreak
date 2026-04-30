using UnityEngine;

namespace Jailbreak.Tutorial
{
    /// <summary>
    /// Tutorial-only pickable. Behaves like <see cref="PickUpInteractable"/>
    /// but never talks to the backend — the server's `player:interact` handler
    /// rejects everything outside the active match, so a NetworkRoutePickable
    /// would never receive an `item:state` reply during tutorial. Tutorial
    /// pickup mirrors live route items: E stores into the first available slot.
    ///
    /// Use this on the tutorial scene's contraband prop. The prop still needs
    /// a <see cref="PickableItem"/> + Rigidbody, but does NOT need a
    /// NetworkRoutePickable nor a NetworkInteractable.
    /// </summary>
    [RequireComponent(typeof(PickableItem))]
    public class TutorialPickupInteractable : MonoBehaviour, IInteractable
    {
        [Header("Interactable")]
        public string actionLabel = "Pick Up";
        public int priority = 5;
        public KeyCode interactKey = KeyCode.E;
        public string[] allowedInStates = null;

        public KeyCode InteractKey => interactKey;
        public string ActionLabel => actionLabel;
        public int Priority => priority;
        public Transform Transform => transform;
        public bool CanInteract => !_pickable.IsHeld;
        public string[] AllowedInStates => allowedInStates;

        private PickableItem _pickable;

        private void Awake()
        {
            _pickable = GetComponent<PickableItem>();
        }

        public void OnInteract(Collider source)
        {
            if (_pickable.IsHeld) return;

            var root = source.transform.root;
            var inventory = root.GetComponent<ItemInventory>();
            if (inventory == null) return;
            if (inventory.IsFull())
            {
                TutorialMissionEvents.Toast("Inventory slots are full");
                return;
            }

            if (!inventory.TryAdd(_pickable)) return;

            _pickable.OnStoredInInventory();
            TutorialMissionEvents.Emit(TutorialMissionEvents.ItemPicked);
        }
    }
}
