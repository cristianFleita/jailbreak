using UnityEngine;
using Jailbreak.Network;

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
    private NetworkRoutePickable routePickable;
    private float lastSlipReportAt = -999f;

    void Awake()
    {
        pickable = GetComponent<PickableItem>();
        routePickable = GetComponent<NetworkRoutePickable>();

        if (slipReceiver != null)
            slipReceiver.onTriggerEntered.AddListener(OnSlipTriggerEntered);
    }

    void OnDestroy()
    {
        if (slipReceiver != null)
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

        if (!IsLocalPrisoner()) return;
        if (Time.time - lastSlipReportAt < 0.5f) return;

        string targetGuardId = NetworkThrowableProjectile.ResolveGuardId(other);
        if (string.IsNullOrEmpty(targetGuardId)) return;

        var net = NetworkManager.Instance;
        if (net == null || routePickable == null || string.IsNullOrEmpty(routePickable.itemId)) return;

        lastSlipReportAt = Time.time;
        net.SendThrowableHit(new ThrowableHitPayload
        {
            itemId = routePickable.itemId,
            targetGuardId = targetGuardId,
            itemKind = "soap",
            hitPosition = SVector3.FromUnity(other.ClosestPoint(transform.position)),
            stunDuration = slipStunDuration
        });
    }

    private static bool IsLocalPrisoner()
    {
        var gsm = GameStateManager.Instance;
        return gsm != null && gsm.LocalRole == "prisoner";
    }
}
