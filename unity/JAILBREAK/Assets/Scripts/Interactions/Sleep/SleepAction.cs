using System;
using System.Collections;
using UnityEngine;

public class SleepAction : MonoBehaviour
{
    [Header("Position")]
    [Tooltip("Transform where the character lies down — position and rotation")]
    public Transform sleepPoint;
    public float sleepPointOffset = 0.3f;

    [Header("Animation")]
    public string stateSleep            = "Sleep";
    public string stateWakeUp           = "WakeUp";
    public float  animationEndThreshold = 0.9f;

    [Header("Timing")]
    public float delayBeforeFade = 0.5f;

    public IEnumerator Execute(Animator animator, Transform actor, Action<float, Action> onFade = null, float holdDuration = 0f)
    {
        if (sleepPoint != null)
        {
            actor.position = new Vector3(sleepPoint.position.x, actor.position.y - sleepPointOffset, sleepPoint.position.z);
            actor.rotation = sleepPoint.rotation;
        }

        yield return FadeAction.Execute(
            animator,
            triggerName:     "Sleep",
            stateName:       stateSleep,
            delayBeforeFade: delayBeforeFade,
            onFade:          onFade,
            holdDuration:    holdDuration
        );

        yield return AnimatorUtils.WaitForState(animator, stateWakeUp);
        yield return AnimatorUtils.WaitForAnimationEnd(animator, stateWakeUp, animationEndThreshold);
    }
}