using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Character-side component that drives the looping "lying down / sleeping"
/// pose while the character is stationed on a <see cref="SleepInteractable"/>'s
/// sleep point.
///
/// Mirrors the authoring pattern of <see cref="WorkInspectCabinetsInteraction"/>:
/// the state lives on the character, the world-side interactable is used only
/// as a position lookup and (for the local player) as the input entry point.
///
/// Scope: this is the path for NPCs (and any other script-driven actor).
/// The LOCAL player still goes through <see cref="SleepInteractable.OnInteract"/>
/// which owns the toggle and network broadcast. This component intentionally
/// does NOT broadcast <c>player:action</c> events — NPCs are synced via
/// <c>NPCNetworkSync</c> instead.
/// </summary>
public class SleepInteraction : MonoBehaviour
{
    [Header("References")]
    public Animator animator;
    public NavMeshAgent navMeshAgent;
    public CharacterController characterController;

    [Header("Animator")]
    [Tooltip("Bool flipped on while sleeping. Defaults to 'isSleeping'.")]
    public string animatorBoolName = "isSleeping";

    [Tooltip("Animator state crossfaded on start as a fallback, in case the transition isn't wired from the current state. Default: LyingDown.")]
    public string animatorStateName = "LyingDown";

    [Tooltip("Animator state crossfaded on stop, so the character visibly exits the sleep loop. Default: Idle.")]
    public string idleStateName = "Idle";

    [Header("Position")]
    [Tooltip("Y offset applied so the avatar lies flat on the mattress instead of floating above it. Mirrors SleepAction.sleepPointOffset.")]
    public float sleepPointOffset = 0.3f;

    [Tooltip("Crossfade duration when entering / exiting the sleep state.")]
    public float crossfadeDuration = 0.25f;

    /// <summary>True while snapped on the bed and playing the sleep loop.</summary>
    public bool IsSleeping => isSleeping;

    private bool isSleeping;
    private Transform currentSleepPoint;

    const string SleepingState = "sleeping";
    private InteractionManager interactionManager;

    void Awake()
    {
        if (animator == null)            animator            = GetComponentInChildren<Animator>();
        if (navMeshAgent == null)        navMeshAgent        = GetComponent<NavMeshAgent>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
        interactionManager = GetComponent<InteractionManager>();
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Begin the sleep loop at <paramref name="sleepPoint"/>. No-op if already sleeping.</summary>
    public void TryStartSleep(Transform sleepPoint)
    {
        if (isSleeping) return;
        StartSleep(sleepPoint);
    }

    /// <summary>End the sleep loop: re-enable locomotion, ground-snap, crossfade to idle. No-op if not sleeping.</summary>
    public void TryStopSleep()
    {
        if (!isSleeping) return;
        StopSleep();
    }

    /// <summary>Immediate reset with no animation. Safe for despawn / disconnect.</summary>
    public void ForceReset()
    {
        isSleeping = false;
        currentSleepPoint = null;

        if (animator != null && !string.IsNullOrEmpty(animatorBoolName))
            animator.SetBool(animatorBoolName, false);

        if (navMeshAgent != null)        navMeshAgent.enabled        = true;
        if (characterController != null) characterController.enabled = true;

        if (interactionManager != null) interactionManager.PopState(SleepingState);
    }

    // ─── Private implementation ──────────────────────────────────────────────

    void StartSleep(Transform sleepPoint)
    {
        isSleeping = true;
        currentSleepPoint = sleepPoint;

        // 1) Stop the agent cleanly BEFORE disabling, so we don't leave queued
        //    velocity that can flicker the Transform on re-enable.
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.ResetPath();
            navMeshAgent.velocity = Vector3.zero;
        }

        // 2) Disable locomotion BEFORE snapping position, matching the pattern
        //    used by WorkTableInteraction / WorkInspectCabinetsInteraction.
        if (characterController != null) characterController.enabled = false;
        if (navMeshAgent != null)         navMeshAgent.enabled        = false;

        // 3) Snap onto the bed. Unlike the cabinet flow we DO override Y here:
        //    the avatar must sit on the mattress surface, not the floor.
        //    Rotation uses the full sleepPoint rotation so the character lies
        //    flat along the bed's authored axis.
        if (sleepPoint != null)
        {
            transform.position = new Vector3(
                sleepPoint.position.x,
                sleepPoint.position.y - sleepPointOffset,
                sleepPoint.position.z);
            transform.rotation = sleepPoint.rotation;
        }

        // 4) Drive the animator. Set the bool first, then crossfade the state
        //    as a fallback so NPCs arriving from Walking/Idle enter the loop.
        if (animator != null)
        {
            if (!string.IsNullOrEmpty(animatorBoolName))
                animator.SetBool(animatorBoolName, true);

            if (!string.IsNullOrEmpty(animatorStateName)
                && animator.HasState(0, Animator.StringToHash(animatorStateName)))
            {
                animator.CrossFade(animatorStateName, crossfadeDuration);
            }
        }

        if (interactionManager != null) interactionManager.PushState(SleepingState);
    }

    void StopSleep()
    {
        isSleeping = false;

        if (animator != null)
        {
            if (!string.IsNullOrEmpty(animatorBoolName))
                animator.SetBool(animatorBoolName, false);

            if (!string.IsNullOrEmpty(idleStateName)
                && animator.HasState(0, Animator.StringToHash(idleStateName)))
            {
                animator.CrossFade(idleStateName, crossfadeDuration);
            }
        }

        Vector3 groundPos = ResolveGroundPosition(transform.position);

        transform.position = groundPos;

        if (characterController != null)
            characterController.enabled = true;

        if (navMeshAgent != null)
        {
            navMeshAgent.enabled = true;

            if (navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.ResetPath();
                navMeshAgent.velocity = Vector3.zero;
            }
        }

        if (interactionManager != null) interactionManager.PopState(SleepingState);

        currentSleepPoint = null;
    }

    static Vector3 ResolveGroundPosition(Vector3 current)
    {
        if (NavMesh.SamplePosition(current, out var hit, 2f, NavMesh.AllAreas))
            return hit.position;

        if (Physics.Raycast(current + Vector3.up * 0.1f, Vector3.down, out var rayHit, 3f, ~0, QueryTriggerInteraction.Ignore))
            return rayHit.point;

        return current;
    }
}
