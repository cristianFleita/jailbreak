using UnityEngine;
using Jailbreak.Network;

[RequireComponent(typeof(PickableItem))]
public class ThrowHitHandler : MonoBehaviour
{
    [Header("Stun")]
    public float  stunDuration = 3f;
    public string animationVar = "IsStunned";

    [Header("Network")]
    public string itemKind = "";
    public bool broadcastThrow = false;
    public bool reportGuardHit = false;
    public float fallbackThrowForce = 12f;

    private PickableItem pickable;
    private Rigidbody rb;
    private float lastHitReportAt = -999f;

    void Awake()
    {
        pickable = GetComponent<PickableItem>();
        rb = GetComponent<Rigidbody>();
        pickable.onThrown.AddListener(OnThrown);
        pickable.onHitWhileThrown.AddListener(OnHit);
    }

    void OnDestroy()
    {
        if (pickable == null) return;
        pickable.onThrown.RemoveListener(OnThrown);
        pickable.onHitWhileThrown.RemoveListener(OnHit);
    }

    private void OnThrown(Vector3 direction)
    {
        if (!broadcastThrow || string.IsNullOrEmpty(itemKind)) return;
        if (!IsLocalPrisoner()) return;

        var velocity = rb != null ? rb.linearVelocity : direction.normalized * fallbackThrowForce;
        float force = velocity.magnitude > 0.01f ? velocity.magnitude : fallbackThrowForce;

        var net = NetworkManager.Instance;
        if (net == null) return;

        net.SendThrowableThrow(new ThrowableThrowPayload
        {
            itemKind = itemKind,
            origin = SVector3.FromUnity(transform.position),
            direction = SVector3.FromUnity(direction.normalized),
            force = force
        });
    }

    private void OnHit(Collider other)
    {
        var stun = other.transform.root.GetComponent<StunAction>();
        if (stun != null)
            stun.ApplyStun(stunDuration, animationVar);

        if (!reportGuardHit || string.IsNullOrEmpty(itemKind)) return;
        if (!IsLocalPrisoner()) return;
        if (Time.time - lastHitReportAt < 0.35f) return;

        string targetGuardId = NetworkThrowableProjectile.ResolveGuardId(other);
        if (string.IsNullOrEmpty(targetGuardId)) return;

        var net = NetworkManager.Instance;
        if (net == null) return;

        lastHitReportAt = Time.time;
        net.SendThrowableHit(new ThrowableHitPayload
        {
            targetGuardId = targetGuardId,
            itemKind = itemKind,
            hitPosition = SVector3.FromUnity(other.ClosestPoint(transform.position)),
            stunDuration = stunDuration
        });
    }

    private static bool IsLocalPrisoner()
    {
        var gsm = GameStateManager.Instance;
        return gsm == null || string.IsNullOrEmpty(gsm.LocalRole) || gsm.LocalRole == "prisoner";
    }
}
