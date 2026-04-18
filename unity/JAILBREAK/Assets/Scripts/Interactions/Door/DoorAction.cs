using System;
using System.Collections;
using UnityEngine;

public class DoorAction : MonoBehaviour
{
    [Header("Door")]
    public Transform destination;

    [Header("Animation")]
    public string stateOpening = "Opening";

    [Header("Timing")]
    [Tooltip("Seconds after animation starts before fade begins")]
    public float delayBeforeFade = 0.3f;

    public IEnumerator Execute(Animator animator, Transform actor, Action<float, Action> onFade = null, float holdDuration = 0f)
    {
        actor.rotation = destination.rotation;

        yield return FadeAction.Execute(
            animator,
            triggerName:     "Open",
            stateName:       stateOpening,
            delayBeforeFade: delayBeforeFade,
            onFade:          onFade,
            holdDuration:    holdDuration,
            onBlack: () =>
            {
                actor.position = new Vector3(destination.position.x, actor.position.y, destination.position.z);
                actor.rotation = destination.rotation;
            }
        );
    }

    void OnDrawGizmos()
    {
        if (destination == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, destination.position);
        Gizmos.DrawSphere(destination.position, 0.08f);
    }
}