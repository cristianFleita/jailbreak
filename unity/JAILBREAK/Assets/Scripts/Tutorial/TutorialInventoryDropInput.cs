using UnityEngine;

namespace Jailbreak.Tutorial
{
    [RequireComponent(typeof(ItemInventory))]
    public class TutorialInventoryDropInput : MonoBehaviour
    {
        [SerializeField] private KeyCode dropKey = KeyCode.G;
        [SerializeField] private float dropForce = 2.5f;
        [SerializeField] private float dropForwardOffset = 0.8f;
        [SerializeField] private float dropUpOffset = 0.8f;

        private ItemInventory _inventory;

        private void Awake()
        {
            _inventory = GetComponent<ItemInventory>();
        }

        private void Update()
        {
            if (!InputSystemKey.WasPressedThisFrame(dropKey)) return;
            DropSelectedLocalItem();
        }

        private void DropSelectedLocalItem()
        {
            if (_inventory == null) return;

            var item = _inventory.TakeSelected();
            if (item == null) return;

            var aim = Camera.main != null ? Camera.main.transform : transform;
            var direction = aim.forward.sqrMagnitude > 0.001f ? aim.forward.normalized : transform.forward;

            item.gameObject.SetActive(true);
            item.transform.SetParent(null);
            item.transform.position = transform.position + Vector3.up * dropUpOffset + direction * dropForwardOffset;
            item.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            item.OnThrown(direction, dropForce);

            TutorialMissionEvents.Emit(TutorialMissionEvents.ItemDropped);
        }
    }
}
