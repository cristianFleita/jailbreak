using UnityEngine;
using UnityEngine.UIElements;

namespace Jailbreak.Tutorial
{
    /// <summary>
    /// Drives the prisoner-tutorial inventory panel.
    ///
    /// Binding priority:
    ///   1. TutorialSceneController calls BindPlayer(go) immediately after Instantiate.
    ///   2. If that hasn't happened yet, Update() keeps trying to find an active
    ///      InteractionManager in the scene and walks up its hierarchy.
    ///
    /// Refresh is event-driven (HeldItemInput / ItemInventory callbacks) so there
    /// is no per-frame polling once the player is found.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TutorialInventoryHud : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;

        private HeldItemInput _heldInput;
        private ItemInventory _inventory;
        private VisualElement _panel;
        private Label _heldLabel;
        private VisualElement _heldIcon;
        private Label[] _slotLabels = new Label[2];
        private VisualElement[] _slotRoots = new VisualElement[2];
        private bool _bound;

        private void OnEnable()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            CacheElements();
            Refresh();
        }

        private void OnDisable()
        {
            UnbindFromPlayer();
        }

        private void Update()
        {
            if (!_bound) TryBindToLocalPlayer();
        }

        // ------------------------------------------------------------------
        // Public API — called by TutorialSceneController
        // ------------------------------------------------------------------

        /// <summary>
        /// Wire the HUD directly to a known player GameObject.
        /// Searches the entire hierarchy (root + all children, including inactive).
        /// </summary>
        public void BindPlayer(GameObject player)
        {
            if (player == null) return;
            UnbindFromPlayer();

            // Search root first, then all children (including inactive)
            _heldInput = player.GetComponent<HeldItemInput>()
                         ?? player.GetComponentInChildren<HeldItemInput>(true);
            _inventory = player.GetComponent<ItemInventory>()
                         ?? player.GetComponentInChildren<ItemInventory>(true);

            if (_heldInput != null || _inventory != null)
            {
                BindToPlayer();
            }
        }

        // ------------------------------------------------------------------
        // Internal binding
        // ------------------------------------------------------------------

        private void TryBindToLocalPlayer()
        {
#if UNITY_2023_1_OR_NEWER
            var managers = Object.FindObjectsByType<InteractionManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            var managers = Object.FindObjectsOfType<InteractionManager>();
#endif
            foreach (var manager in managers)
            {
                if (manager == null || !manager.enabled) continue;

                // Walk up to root and search downward — covers all component layouts
                var root = manager.transform.root.gameObject;
                _heldInput = root.GetComponent<HeldItemInput>()
                             ?? root.GetComponentInChildren<HeldItemInput>(true);
                _inventory = root.GetComponent<ItemInventory>()
                             ?? root.GetComponentInChildren<ItemInventory>(true);

                if (_heldInput == null && _inventory == null) continue;

                BindToPlayer();
                return;
            }
        }

        private void BindToPlayer()
        {
            if (_heldInput != null)
            {
                _heldInput.onItemAttached.AddListener(OnHeldChanged);
                _heldInput.onItemThrown.AddListener(OnHeldChanged);
                _heldInput.onItemStored.AddListener(OnHeldChanged);
            }

            if (_inventory != null)
            {
                _inventory.onSlotSelected.AddListener(OnSlotIndexChanged);
                _inventory.onItemAdded.AddListener(OnItemAdded);
                _inventory.onItemRemoved.AddListener(OnItemRemoved);
            }

            _bound = true;
            Refresh();
        }

        private void UnbindFromPlayer()
        {
            if (_heldInput != null)
            {
                _heldInput.onItemAttached.RemoveListener(OnHeldChanged);
                _heldInput.onItemThrown.RemoveListener(OnHeldChanged);
                _heldInput.onItemStored.RemoveListener(OnHeldChanged);
            }

            if (_inventory != null)
            {
                _inventory.onSlotSelected.RemoveListener(OnSlotIndexChanged);
                _inventory.onItemAdded.RemoveListener(OnItemAdded);
                _inventory.onItemRemoved.RemoveListener(OnItemRemoved);
            }

            _heldInput = null;
            _inventory = null;
            _bound = false;
        }

        private void OnHeldChanged(PickableItem _) => Refresh();
        private void OnSlotIndexChanged(int _) => Refresh();
        private void OnItemAdded(int _, PickableItem __) => Refresh();
        private void OnItemRemoved(int _, PickableItem __) => Refresh();

        // ------------------------------------------------------------------
        // Element caching + refresh
        // ------------------------------------------------------------------

        private void CacheElements()
        {
            var root = uiDocument != null ? uiDocument.rootVisualElement : null;
            if (root == null) return;

            _panel = root.Q<VisualElement>("InventoryPanel");
            _heldLabel = root.Q<Label>("HeldItemLabel");
            _heldIcon = root.Q<VisualElement>("HeldItemIcon");

            _slotRoots[0] = root.Q<VisualElement>("InventorySlot0");
            _slotRoots[1] = root.Q<VisualElement>("InventorySlot1");
            _slotLabels[0] = root.Q<Label>("InventorySlot0Label");
            _slotLabels[1] = root.Q<Label>("InventorySlot1Label");
        }

        private void Refresh()
        {
            if (_panel == null)
            {
                // Elements not cached yet — try again (UIDocument may not have initialised)
                CacheElements();
                if (_panel == null) return;
            }

            // Held item row
            if (_heldLabel != null)
            {
                var hasHeld = _heldInput != null && _heldInput.HasItem;
                _heldLabel.text = hasHeld ? "Holding tool" : "Empty hand";
            }

            // Inventory slots
            var selectedIndex = _inventory != null ? _inventory.SelectedIndex : -1;

            for (int i = 0; i < _slotLabels.Length; i++)
            {
                var label = _slotLabels[i];
                var slotRoot = _slotRoots[i];

                var item = _inventory != null && _inventory.IsValidIndex(i)
                    ? _inventory.GetAt(i)
                    : null;

                if (label != null)
                    label.text = item != null ? "Stored" : "Empty";

                if (slotRoot == null) continue;

                var selected = selectedIndex == i;
                float borderWidth = selected ? 1f : 0f;
                var borderColor = selected ? new Color(0.55f, 0.85f, 0.95f, 1f) : Color.clear;

                slotRoot.style.borderTopWidth = borderWidth;
                slotRoot.style.borderRightWidth = borderWidth;
                slotRoot.style.borderBottomWidth = borderWidth;
                slotRoot.style.borderLeftWidth = borderWidth;
                slotRoot.style.borderTopColor = borderColor;
                slotRoot.style.borderRightColor = borderColor;
                slotRoot.style.borderBottomColor = borderColor;
                slotRoot.style.borderLeftColor = borderColor;
            }
        }
    }
}
