using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Snap-to-bed + animator driver for the manual-toggle sleep interaction.
///
/// Mirrors the authoring pattern of <see cref="ProgressPointAction"/>: a pure
/// position/animation relay, driven externally by <see cref="SleepInteractable"/>
/// for the local player and via <see cref="SleepInteractable.ApplyRemote"/> for
/// remote avatars. No coroutines, no timers, no screen fade — the player
/// chooses when to start and when to stop.
///
/// Animator contract:
///   • <see cref="animatorBoolName"/> (default "isSleeping") — set true while
///     sleeping, false on wake. The animator should loop a Sleep state while
///     the bool is true and transition to a WakeUp/Idle state when it flips
///     back to false.
///   • <see cref="sleepStateName"/> / <see cref="wakeUpStateName"/> /
///     <see cref="idleStateName"/> are crossfade fallbacks used when the
///     animator graph doesn't have a transition wired from the current state.
/// </summary>
public class SleepAction : MonoBehaviour
{
    [Header("Position")]
    [Tooltip("Transform the avatar lies on (position + yaw). Required.")]
    public Transform sleepPoint;
    [Tooltip("Y offset from sleepPoint applied to the avatar so it lies flat on the mattress instead of floating above it.")]
    public float sleepPointOffset = 0.3f;

    [Header("Animator")]
    [Tooltip("Bool flipped on while sleeping. The animator should loop a Sleep state while true and exit on false.")]
    public string animatorBoolName = "isSleeping";

    [Tooltip("Animator state crossfaded on start as a fallback, in case no transition is wired from the current state.")]
    public string sleepStateName = "Sleep";

    [Tooltip("Animator state crossfaded on stop, so the avatar visibly exits the sleep loop.")]
    public string wakeUpStateName = "WakeUp";

    [Tooltip("Idle fallback used if the WakeUp state isn't present on the animator.")]
    public string idleStateName = "Idle";

    [Header("Tuning")]
    public float enterCrossfade = 0.2f;
    public float exitCrossfade  = 0.2f;

    /// <summary>
    /// Snap <paramref name="actor"/> onto the sleep point and start the looping
    /// sleep animation on <paramref name="animator"/>.
    /// </summary>
    public void BeginSleep(Animator animator, Transform actor)
    {
        if (sleepPoint != null && actor != null)
        {
            actor.position = new Vector3(
                sleepPoint.position.x,
                sleepPoint.position.y - sleepPointOffset,
                sleepPoint.position.z);
            actor.rotation = sleepPoint.rotation;
        }

        if (animator == null) return;

        if (!string.IsNullOrEmpty(animatorBoolName))
            animator.SetBool(animatorBoolName, true);

        if (!string.IsNullOrEmpty(sleepStateName)
            && animator.HasState(0, Animator.StringToHash(sleepStateName)))
        {
            animator.CrossFade(sleepStateName, enterCrossfade);
        }
    }

    /// <summary>
    /// End the sleep loop: clear the animator bool and crossfade to the wake-up
    /// state (or idle as fallback) so the avatar visibly exits the pose.
    /// </summary>
    public void EndSleep(Animator animator)
    {
        if (animator == null) return;

        if (!string.IsNullOrEmpty(animatorBoolName))
            animator.SetBool(animatorBoolName, false);

        if (!string.IsNullOrEmpty(wakeUpStateName)
            && animator.HasState(0, Animator.StringToHash(wakeUpStateName)))
        {
            animator.CrossFade(wakeUpStateName, exitCrossfade);
        }
        else if (!string.IsNullOrEmpty(idleStateName)
                 && animator.HasState(0, Animator.StringToHash(idleStateName)))
        {
            animator.CrossFade(idleStateName, exitCrossfade);
        }
    }

    /// <summary>
    /// Resolve a walkable ground position near <paramref name="current"/>:
    /// prefers the NavMesh, falls back to a downward physics raycast, then
    /// returns the input as a final fallback.
    /// </summary>
    public static Vector3 ResolveGroundPosition(Vector3 current)
    {
        if (NavMesh.SamplePosition(current, out var navHit, 2f, NavMesh.AllAreas))
            return navHit.position;

        if (Physics.Raycast(current + Vector3.up * 0.1f, Vector3.down, out var rayHit, 3f, ~0, QueryTriggerInteraction.Ignore))
            return rayHit.point;

        return current;
    }
}
