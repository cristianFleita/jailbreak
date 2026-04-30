using Jailbreak.Audio;
using UnityEngine;

public class TriggerMoveOffset : MonoBehaviour
{
    [Header("References")]
    public GameObject objectToMove;
    public OneShotSfx openSfx;

    [Header("Settings")]
    public LayerMask activationLayer;
    public float zOffset = 5f;
    public float speed = 5f;

    private Vector3 initialPosition;
    private Vector3 targetPosition;
    private int entitiesInside = 0;

    void Start()
    {
        if (objectToMove != null)
        {
            initialPosition = objectToMove.transform.position;
            targetPosition = initialPosition;

            if (openSfx == null)
                openSfx = objectToMove.GetComponentInChildren<OneShotSfx>(true);
        }
    }

    void Update()
    {
        if (objectToMove == null) return;

        if (Vector3.Distance(objectToMove.transform.position, targetPosition) > 0.001f)
        {
            objectToMove.transform.position = Vector3.MoveTowards(
                objectToMove.transform.position,
                targetPosition,
                speed * Time.deltaTime
            );
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (((1 << other.gameObject.layer) & activationLayer) != 0)
        {
            bool wasClosed = entitiesInside == 0;
            entitiesInside++;
            UpdateTarget();
            if (wasClosed && openSfx != null)
                openSfx.Play();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (((1 << other.gameObject.layer) & activationLayer) != 0)
        {
            entitiesInside--;
            if (entitiesInside < 0) entitiesInside = 0;
            UpdateTarget();
        }
    }

    private void UpdateTarget()
    {
        if (objectToMove == null) return;

        if (entitiesInside > 0)
        {
            targetPosition = initialPosition + new Vector3(0, 0, zOffset);
        }
        else
        {
            targetPosition = initialPosition;
        }
    }
}
