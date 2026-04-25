using UnityEngine;

[RequireComponent(typeof(PickableItem))]
public class SoapSlipTrigger : MonoBehaviour
{
    [Header("Slip")]
    public float  slipStunDuration = 2f;
    public string animationVar     = "IsFalling";

    [Header("Trigger")]
    [Tooltip("Child GameObject with SlipTriggerReceiver — must NOT have its own Rigidbody")]
    public SlipTriggerReceiver slipReceiver;

    private PickableItem pickable;

    void Awake()
    {
        pickable = GetComponent<PickableItem>();
        slipReceiver.onTriggerEntered.AddListener(OnSlipTriggerEntered);
    }

    void OnDestroy()
    {
        slipReceiver.onTriggerEntered.RemoveListener(OnSlipTriggerEntered);
    }

    private void OnSlipTriggerEntered(Collider other)
    {
        if (pickable.IsHeld)     return;
        if (pickable.IsInFlight) return;

        if (pickable.OwnerCC != null)
        {
            var otherCC = other.GetComponent<CharacterController>();
            if (otherCC != null && otherCC == pickable.OwnerCC) return;
        }

        var stun = other.transform.root.GetComponent<StunAction>();
        if (stun == null) return;

        stun.ApplyStun(slipStunDuration, animationVar);
        pickable.OnConsumed();
    }
}