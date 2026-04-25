using UnityEngine;
using UnityEngine.Events;

public class SlipTriggerReceiver : MonoBehaviour
{
    public UnityEvent<Collider> onTriggerEntered;

    void OnTriggerEnter(Collider other)
    {
        onTriggerEntered.Invoke(other);
    }
}