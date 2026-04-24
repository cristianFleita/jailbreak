using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class StunAction : MonoBehaviour
{
    [Header("Events")]
    public UnityEvent onStunStart;
    public UnityEvent onStunEnd;

    private bool isStunned;

    public bool IsStunned => isStunned;

    public void ApplyStun(float duration, string animatorBoolParam = "")
    {
        if (isStunned) return;
        StartCoroutine(StunRoutine(duration, animatorBoolParam));
    }

    private IEnumerator StunRoutine(float duration, string animatorBoolParam)
    {
        isStunned = true;

        var animator = GetComponentInChildren<Animator>();
        var cc       = GetComponent<CharacterController>();
        

        if (cc != null) cc.enabled = false;
        SetAnimatorBool(animator, animatorBoolParam, true);

        onStunStart.Invoke();

        yield return new WaitForSeconds(duration);

        SetAnimatorBool(animator, animatorBoolParam, false);
        if (cc != null) cc.enabled = true;

        isStunned = false;
        onStunEnd.Invoke();
    }

    private void SetAnimatorBool(Animator animator, string param, bool value)
    {
        if (animator == null || string.IsNullOrEmpty(param)) return;
        animator.SetBool(param, value);
    }
}