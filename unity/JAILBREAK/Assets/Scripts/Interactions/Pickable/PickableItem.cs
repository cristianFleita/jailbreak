using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Rigidbody))]
public class PickableItem : MonoBehaviour, IPickable
{
    [Header("Hold")]
    public Vector3 holdPositionOffset = Vector3.zero;
    public Vector3 holdRotationOffset = Vector3.zero;

    [Header("Throw")]
    public float throwSpawnOffset = 0.6f;

    [Header("Flight Detection")]
    public float landedSpeedThreshold = 1f;

    [Header("Events")]
    public UnityEvent<Transform> onPickedUp;
    public UnityEvent<Vector3>   onThrown;
    public UnityEvent            onStoredInInventory;
    public UnityEvent            onConsumed;
    public UnityEvent<Collider>  onHitWhileThrown;

    public GameObject          GameObject  => gameObject;
    public bool                IsHeld      => held;
    public bool                IsInFlight  => wasThrown && rb.linearVelocity.magnitude > landedSpeedThreshold;
    public CharacterController OwnerCC     => ownerCC;

    private bool                held;
    private bool                wasThrown;
    private Rigidbody           rb;
    private Collider            mainCollider;
    private CharacterController ownerCC;
    private RigidbodyConstraints originalConstraints;

    void Awake()
    {
        rb                  = GetComponent<Rigidbody>();
        mainCollider        = FindMainCollider();
        originalConstraints = rb.constraints;
    }

    public void OnPickedUp(Transform holdPoint)
    {
        held           = true;
        wasThrown      = false;
        SetHeldVisible();

        ownerCC = holdPoint.root.GetComponent<CharacterController>();

        transform.SetParent(holdPoint);
        transform.localPosition = holdPositionOffset;
        transform.localRotation = Quaternion.Euler(holdRotationOffset);

        onPickedUp.Invoke(holdPoint);
    }

    public void SetHeldVisible()
    {
        SetRenderersEnabled(true);
        ApplyHeldPhysicsState();
    }

    public void OnThrown(Vector3 direction, float force)
    {
        held = false;
        wasThrown = true;

        transform.SetParent(null);

        SetAllCollidersEnabled(false);

        rb.isKinematic  = false;
        rb.constraints  = RigidbodyConstraints.FreezeRotation;
        rb.useGravity   = false;
        rb.linearVelocity     = Vector3.zero;

        rb.WakeUp();

        transform.position += direction.normalized * throwSpawnOffset;

        rb.linearVelocity = direction * force;

        SetAllCollidersEnabled(true);

        onThrown.Invoke(direction);
    }

    public void OnStoredInInventory()
    {
        OnStoredInInventory(true);
    }

    public void OnStoredInInventory(bool deactivateGameObject)
    {
        held            = false;
        wasThrown       = false;
        ownerCC         = null;
        rb.isKinematic  = true;
        rb.constraints  = originalConstraints;
        rb.useGravity   = true;
 
        transform.SetParent(null); 
        if (deactivateGameObject)
            gameObject.SetActive(false);
        else
            SetWorldVisible(false);

        onStoredInInventory.Invoke();
    }

    public void SetWorldVisible(bool value)
    {
        SetAllCollidersEnabled(value);
        SetRenderersEnabled(value);

        rb.constraints = originalConstraints;

        if (value)
        {
            held = false;
            wasThrown = false;
            ownerCC = null;
            transform.SetParent(null);
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        else
        {
            ClearDynamicVelocity();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    public void OnConsumed()
    {
        SetAllCollidersEnabled(false);
        onConsumed.Invoke();
        Destroy(gameObject);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!wasThrown) return;
        if (ownerCC != null && collision.collider.GetComponent<CharacterController>() == ownerCC) return;

        onHitWhileThrown.Invoke(collision.collider);
        wasThrown      = false;
        ownerCC        = null;
        rb.constraints = originalConstraints;
        rb.useGravity  = true;
    }

    private Collider FindMainCollider()
    {
        foreach (var c in GetComponents<Collider>())
            if (!c.isTrigger) return c;
        return null;
    }

    private void SetAllCollidersEnabled(bool value)
    {
        foreach (var c in GetComponentsInChildren<Collider>(true))
            c.enabled = value;
    }

    private void SetRenderersEnabled(bool value)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            r.enabled = value;
    }

    private void ApplyHeldPhysicsState()
    {
        SetAllCollidersEnabled(false);
        ClearDynamicVelocity();
        rb.isKinematic  = true;
        rb.useGravity   = true;
        rb.constraints  = RigidbodyConstraints.FreezeRotation;
    }

    private void ClearDynamicVelocity()
    {
        if (rb.isKinematic) return;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }
}
