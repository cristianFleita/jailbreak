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
        rb.isKinematic = true;

        SetAllCollidersEnabled(false);

        ownerCC = holdPoint.root.GetComponent<CharacterController>();

        transform.SetParent(holdPoint);
        transform.localPosition = holdPositionOffset;
        transform.localRotation = Quaternion.Euler(holdRotationOffset);

        onPickedUp.Invoke(holdPoint);
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
        held            = false;
        wasThrown       = false;
        ownerCC         = null;
        rb.constraints  = originalConstraints;
        rb.useGravity   = true;
 
        transform.SetParent(null); 
        gameObject.SetActive(false);
        onStoredInInventory.Invoke();
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
}