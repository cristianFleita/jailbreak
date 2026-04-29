using Jailbreak.Network;
using Jailbreak.Player;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class NetworkThrowableProjectile : MonoBehaviour
{
    private const float HitReportCooldown = 0.35f;

    private string itemKind;
    private string throwerUserId;
    private Transform ownerRoot;
    private float stunDuration = 3f;
    private bool reportHits;
    private bool hasHit;
    private float lastHitReportAt = -999f;
    private Rigidbody rb;
    private Collider[] projectileColliders;

    public void Launch(
        string kind,
        string throwerId,
        Transform throwerRoot,
        Vector3 direction,
        float force,
        float stunSeconds,
        float lifetimeSeconds,
        bool shouldReportHits)
    {
        itemKind = kind;
        throwerUserId = throwerId;
        ownerRoot = throwerRoot;
        stunDuration = stunSeconds;
        reportHits = shouldReportHits;

        rb = GetComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.WakeUp();
        rb.linearVelocity = direction.normalized * force;

        CacheProjectileColliders();
        IgnoreOwnerCollisions(true);

        if (lifetimeSeconds > 0f)
            Destroy(gameObject, lifetimeSeconds);
    }

    private void OnDestroy()
    {
        IgnoreOwnerCollisions(false);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (hasHit) return;
        if (ownerRoot != null && collision.collider.transform.root == ownerRoot) return;

        hasHit = true;
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.None;
            rb.useGravity = true;
        }

        var hitPosition = collision.contactCount > 0
            ? collision.GetContact(0).point
            : collision.collider.ClosestPoint(transform.position);
        TryApplyGuardStun(collision.collider, hitPosition);
    }

    private void TryApplyGuardStun(Collider other, Vector3 hitPosition)
    {
        string targetGuardId = ResolveGuardId(other);
        if (string.IsNullOrEmpty(targetGuardId)) return;
        if (targetGuardId == throwerUserId) return;

        // string animParam = itemKind == "soap" ? "IsFalling" : "IsStunned";
        var stun = other.transform.root.GetComponent<StunAction>();
        if (stun != null)
            stun.ApplyStun(stunDuration, "IsStunned");
            // stun.ApplyStun(stunDuration, animParam);

        if (!reportHits) return;
        if (Time.time - lastHitReportAt < HitReportCooldown) return;
        lastHitReportAt = Time.time;

        var net = NetworkManager.Instance;
        if (net == null) return;

        net.SendThrowableHit(new ThrowableHitPayload
        {
            targetGuardId = targetGuardId,
            itemKind = itemKind,
            hitPosition = SVector3.FromUnity(hitPosition),
            stunDuration = stunDuration
        });
    }

    public static string ResolveGuardId(Collider other)
    {
        var remote = other.GetComponentInParent<RemotePlayerSync>();
        if (remote != null && remote.Role == "guard")
            return remote.PlayerId;

        var gsm = GameStateManager.Instance;
        var net = NetworkManager.Instance;
        if (gsm != null && net != null && gsm.LocalRole == "guard")
        {
            var root = other.transform.root;
            if (root.GetComponent<PlayerInputController>() != null)
                return net.LocalUserId ?? net.LocalPlayerId;
        }

        return null;
    }

    private void CacheProjectileColliders()
    {
        projectileColliders = GetComponentsInChildren<Collider>(true);
    }

    private void IgnoreOwnerCollisions(bool ignore)
    {
        if (ownerRoot == null) return;
        if (projectileColliders == null || projectileColliders.Length == 0)
            CacheProjectileColliders();

        var ownerColliders = ownerRoot.GetComponentsInChildren<Collider>(true);
        foreach (var projectileCollider in projectileColliders)
        {
            if (projectileCollider == null) continue;
            foreach (var ownerCollider in ownerColliders)
            {
                if (ownerCollider == null || ownerCollider == projectileCollider) continue;
                Physics.IgnoreCollision(projectileCollider, ownerCollider, ignore);
            }
        }
    }
}
