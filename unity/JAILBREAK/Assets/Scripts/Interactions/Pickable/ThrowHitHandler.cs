using UnityEngine;

[RequireComponent(typeof(PickableItem))]
public class ThrowHitHandler : MonoBehaviour
{
    [Header("Stun")]
    public float  stunDuration = 3f;
    public string animationVar = "IsStunned";

    private PickableItem pickable;

    void Awake()
    {
        pickable = GetComponent<PickableItem>();
        pickable.onHitWhileThrown.AddListener(OnHit);
    }

    void OnDestroy()
    {
        pickable.onHitWhileThrown.RemoveListener(OnHit);
    }

    private void OnHit(Collider other)
    {
        var stun = other.transform.root.GetComponent<StunAction>();
        if (stun == null) return;

        stun.ApplyStun(stunDuration, animationVar);
    }
}