using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Snap-to-hidePoint + visibility/animator driver for the manual-toggle hide
/// interaction (used by the laundry cart and any other "hide me inside this
/// prop" interactable).
///
/// Mirrors the authoring pattern of <see cref="SleepAction"/>: a pure
/// position/animation relay, driven externally by <see cref="HideInteractable"/>
/// for the local player and via <see cref="HideInteractable.ApplyRemote"/> for
/// remote avatars. No coroutines, no timers, no progress bar — the player
/// chooses when to hide and when to come out.
///
/// Animator contract (optional):
///   • <see cref="animatorBoolName"/> (default "isHiding") — set true while
///     hidden, false on exit. The animator should hold a crouched/ducking pose
///     while the bool is true and transition back to Idle when it flips false.
///   • <see cref="hideStateName"/> / <see cref="exitStateName"/> /
///     <see cref="idleStateName"/> are crossfade fallbacks used when the
///     animator graph doesn't have a transition wired from the current state.
///
/// Visibility:
///   While hidden the avatar's renderers (skinned + mesh) are toggled off so
///   the player is fully concealed inside the prop, regardless of camera angle
///   or LOS. Renderer state is snapshotted on Hide and restored on Exit so we
///   never leave a previously-disabled renderer enabled.
/// </summary>
public class HideAction : MonoBehaviour
{
    [Header("Position")]
    [Tooltip("Transform the avatar snaps to (position + yaw) while hidden. Required.")]
    public Transform hidePoint;

    [Tooltip("Y offset from hidePoint applied to the avatar so its capsule sits inside the prop instead of clipping through the floor.")]
    public float hidePointOffset = 0f;

    [Tooltip("Optional exit point. Defaults to the avatar's pre-hide position when null.")]
    public Transform exitPoint;

    [Header("Animator (optional)")]
    [Tooltip("Bool flipped on while hidden. Leave empty to skip animator driving entirely.")]
    public string animatorBoolName = "isHiding";

    [Tooltip("Animator state crossfaded on hide as a fallback, in case no transition is wired from the current state.")]
    public string hideStateName = "Hide";

    [Tooltip("Animator state crossfaded on exit, so the avatar visibly comes out of the hide pose.")]
    public string exitStateName = "Idle";

    [Tooltip("Idle fallback used if the exit state isn't present on the animator.")]
    public string idleStateName = "Idle";

    [Header("Tuning")]
    public float enterCrossfade = 0.15f;
    public float exitCrossfade  = 0.15f;

    /// <summary>
    /// Snap <paramref name="actor"/> onto the hide point and start the hide
    /// animator pose on <paramref name="animator"/>.
    /// </summary>
    public void BeginHide(Animator animator, Transform actor)
    {
        if (hidePoint != null && actor != null)
        {
            actor.position = new Vector3(
                hidePoint.position.x,
                hidePoint.position.y + hidePointOffset,
                hidePoint.position.z);
            actor.rotation = hidePoint.rotation;
        }

        if (animator == null) return;

        if (!string.IsNullOrEmpty(animatorBoolName))
            animator.SetBool(animatorBoolName, true);

        if (!string.IsNullOrEmpty(hideStateName)
            && animator.HasState(0, Animator.StringToHash(hideStateName)))
        {
            animator.CrossFade(hideStateName, enterCrossfade);
        }
    }

    /// <summary>
    /// End the hide pose: clear the animator bool and crossfade to the exit
    /// state (or idle as fallback) so the avatar visibly comes out.
    /// </summary>
    public void EndHide(Animator animator)
    {
        if (animator == null) return;

        if (!string.IsNullOrEmpty(animatorBoolName))
            animator.SetBool(animatorBoolName, false);

        if (!string.IsNullOrEmpty(exitStateName)
            && animator.HasState(0, Animator.StringToHash(exitStateName)))
        {
            animator.CrossFade(exitStateName, exitCrossfade);
        }
        else if (!string.IsNullOrEmpty(idleStateName)
                 && animator.HasState(0, Animator.StringToHash(idleStateName)))
        {
            animator.CrossFade(idleStateName, exitCrossfade);
        }
    }

    /// <summary>
    /// Toggle every renderer under <paramref name="actor"/> so the avatar is
    /// fully concealed while hidden. Snapshots the prior enabled-state so we
    /// don't accidentally re-enable renderers that were already off.
    /// </summary>
    public static void SetAvatarVisible(Transform actor, bool visible, ref Dictionary<Renderer, bool> snapshot)
    {
        if (actor == null) return;

        var renderers = actor.GetComponentsInChildren<Renderer>(true);

        if (!visible)
        {
            snapshot ??= new Dictionary<Renderer, bool>(renderers.Length);
            snapshot.Clear();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                snapshot[r] = r.enabled;
                r.enabled = false;
            }
        }
        else
        {
            if (snapshot == null || snapshot.Count == 0)
            {
                foreach (var r in renderers)
                    if (r != null) r.enabled = true;
            }
            else
            {
                foreach (var kv in snapshot)
                    if (kv.Key != null) kv.Key.enabled = kv.Value;
                snapshot.Clear();
            }
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
