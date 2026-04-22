using System;
using System.Collections;
using UnityEngine;

public static class FadeAction
{
    public static IEnumerator Execute(
        Animator animator,
        string triggerName,
        string stateName,
        float delayBeforeFade,
        Action<float, Action> onFade,
        float holdDuration = 0f,
        Action onBlack = null)
    {
        animator.SetTrigger(triggerName);

        yield return AnimatorUtils.WaitForState(animator, stateName);
        yield return new WaitForSeconds(delayBeforeFade);

        if (onFade != null)
        {
            bool done = false;
            onFade(holdDuration, () =>
            {
                onBlack?.Invoke();
                done = true;
            });
            yield return new WaitUntil(() => done);
        }
    }
}