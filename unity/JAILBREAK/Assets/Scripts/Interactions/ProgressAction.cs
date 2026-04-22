using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class ProgressAction : MonoBehaviour
{
    [Header("Animation")]
    public string animatorBoolName = "";

    [Header("Progress")]
    [Tooltip("Total seconds to reach 100%")]
    public float duration = 10f;

    [Tooltip("Reset progress to 0 when stopped before completion")]
    public bool resetOnStop = false;

    [Header("Events")]
    public UnityEvent onComplete;

    [HideInInspector]
    public float progress;

    public bool IsComplete => progress >= 1f;

    private bool running;

    public IEnumerator Execute(Animator animator, Action onStop = null)
    {
        if (progress >= 1f)
            progress = 0f;

        running = true;
        animator.SetBool(animatorBoolName, true);

        while (running && progress < 1f)
        {
            progress += Time.deltaTime / duration;
            progress  = Mathf.Clamp01(progress);
            
            Debug.Log($"[ProgressAction] State={animator.GetCurrentAnimatorStateInfo(0).shortNameHash} norm={animator.GetCurrentAnimatorStateInfo(0).normalizedTime:F2}");

            yield return null;
        }

        animator.SetBool(animatorBoolName, false);

        bool completed = progress >= 1f;

        if (!completed && resetOnStop)
            progress = 0f;

        if (completed)
            onComplete?.Invoke();

        onStop?.Invoke();
    }

    public void Stop()
    {
        running = false;
    }
}