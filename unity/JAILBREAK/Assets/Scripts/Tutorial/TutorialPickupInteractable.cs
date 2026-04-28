using UnityEngine;

namespace Jailbreak.Tutorial
{
    /// <summary>
    /// Tutorial-only pickable. Behaves like <see cref="PickUpInteractable"/>
    /// but never talks to the backend — the server's `player:interact` handler
    /// rejects everything outside the active match, so a NetworkRoutePickable
    /// would never receive an `item:state` reply during tutorial and the prop
    /// would never attach to the player's hand.
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

        [Header("Hold Point")]
        [Tooltip("Bone name to attach the prop to. Defaults match the player rig.")]
        public string holdPointName = "mixamorig:RightHandIndex1";

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
            var heldInput = root.GetComponent<HeldItemInput>();
            var inventory = root.GetComponent<ItemInventory>();
            if (heldInput == null) return;

            // If the hand is full, push the current item into the inventory so
            // the player can pick this one up. Mirrors PickUpInteractable.
            if (heldInput.HasItem)
            {
                if (inventory != null && !inventory.IsFull())
                    heldInput.ForceStore();
                else
                    heldInput.Throw();
            }

            var holdPoint = ResolveHoldPoint(root);
            if (holdPoint == null) return;

            _pickable.OnPickedUp(holdPoint);
            heldInput.Attach(_pickable);
            TutorialMissionEvents.Emit(TutorialMissionEvents.ItemPicked);
        }

        private Transform ResolveHoldPoint(Transform root)
        {
            if (string.IsNullOrEmpty(holdPointName)) return root;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == holdPointName) return t;
            return root;
        }
    }
}
