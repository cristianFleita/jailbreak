using System.Collections;
using UnityEngine;

public static class AnimatorUtils
{
    public static IEnumerator WaitForState(Animator animator, string stateName, float timeout = 2f)
    {
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            if (animator.GetCurrentAnimatorStateInfo(0).IsName(stateName)) break;
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    public static IEnumerator WaitForAnimationEnd(Animator animator, string expectedState, float threshold = 0.9f, float timeout = 5f)
    {
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);

            bool reachedEnd    = !animator.IsInTransition(0) && info.normalizedTime >= threshold;
            bool exitedAlready = !info.IsName(expectedState) && !animator.IsInTransition(0);

            if (reachedEnd || exitedAlready) break;

            elapsed += Time.deltaTime;
            yield return null;
        }
    }
}