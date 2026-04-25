using UnityEngine;

public interface IPickable
{
    GameObject GameObject { get; }
    bool IsHeld           { get; }

    void OnPickedUp(Transform holdPoint);
    void OnThrown(Vector3 direction, float force);
    void OnStoredInInventory();
    void OnConsumed();
}