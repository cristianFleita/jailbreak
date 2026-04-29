using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using Jailbreak.Player;

public class StunAction : MonoBehaviour
{
    private const string FallingAnimatorParam = "IsFalling";

    [Header("Fall Animation")]
    public string fallStateName = "Fall";
    public string fallenIdleStateName = "FallenIdle";
    public string standUpStateName = "StandingUp";
    public float fallAnimationSeconds = 2.5f;
    public float standUpAnimationSeconds = 1.65f;
    public float fallCrossFadeSeconds = 0.08f;

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
        var input    = GetComponent<PlayerInputController>();
        var animationController = GetComponent<PlayerAnimationController>();
        bool hadInputEnabled = input != null && input.InputEnabled;
        bool hadAnimationControllerEnabled = animationController != null && animationController.enabled;


        if (cc != null) cc.enabled = false;
        if (input != null) input.InputEnabled = false;
        if (animationController != null) animationController.enabled = false;

        onStunStart?.Invoke();

        if (animatorBoolParam == FallingAnimatorParam)
        {
            yield return PlayFallSequence(animator, duration);
        }
        else
        {
            SetAnimatorBool(animator, animatorBoolParam, true);
            yield return new WaitForSeconds(duration);
            SetAnimatorBool(animator, animatorBoolParam, false);
        }

        if (animator != null)
        {
            SetAnimatorBool(animator, FallingAnimatorParam, false);
            TryCrossFade(animator, "Idle");
        }

        if (cc != null) cc.enabled = true;
        if (input != null) input.InputEnabled = hadInputEnabled;
        if (animationController != null)
        {
            animationController.enabled = hadAnimationControllerEnabled;
            // Stun bypassed PlayerAnimationController, so its cached state is stale.
            // Without this, remote avatars that received movementState="walk" during
            // the stun (because the source's input was frozen) skip the crossfade
            // back to Idle and look stuck in Walking.
            animationController.ResetCachedState();
        }

        isStunned = false;
        onStunEnd?.Invoke();
    }

    private IEnumerator PlayFallSequence(Animator animator, float duration)
    {
        if (animator == null)
        {
            yield return new WaitForSeconds(duration);
            yield break;
        }

        bool hasFall = TryCrossFade(animator, fallStateName);
        bool hasFallenIdle = HasState(animator, fallenIdleStateName);
        bool hasStandUp = HasState(animator, standUpStateName);

        if (!hasFall || !hasFallenIdle || !hasStandUp)
        {
            yield return new WaitForSeconds(duration);
            SetAnimatorBool(animator, FallingAnimatorParam, false);
            yield break;
        }

        yield return new WaitForSeconds(fallAnimationSeconds);
        TryCrossFade(animator, fallenIdleStateName);

        float downSeconds = Mathf.Max(0f, duration - fallAnimationSeconds - standUpAnimationSeconds);
        if (downSeconds > 0f)
            yield return new WaitForSeconds(downSeconds);

        TryCrossFade(animator, standUpStateName);
        yield return new WaitForSeconds(standUpAnimationSeconds);
    }

    private bool TryCrossFade(Animator animator, string stateName)
    {
        if (!HasState(animator, stateName)) return false;
        animator.CrossFadeInFixedTime(stateName, fallCrossFadeSeconds, 0);
        return true;
    }

    private bool HasState(Animator animator, string stateName)
    {
        return animator != null
            && !string.IsNullOrEmpty(stateName)
            && animator.HasState(0, Animator.StringToHash(stateName));
    }

    private void SetAnimatorBool(Animator animator, string param, bool value)
    {
        if (animator == null || string.IsNullOrEmpty(param)) return;
        if (!HasParameter(animator, param, AnimatorControllerParameterType.Bool)) return;
        animator.SetBool(param, value);
    }

    private bool HasParameter(Animator animator, string param, AnimatorControllerParameterType type)
    {
        foreach (var p in animator.parameters)
            if (p.type == type && p.name == param) return true;
        return false;
    }
}
