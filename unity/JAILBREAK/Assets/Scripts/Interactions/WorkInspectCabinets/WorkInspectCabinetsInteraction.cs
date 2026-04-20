using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Character-side component that drives the looping "inspecting cabinets"
/// animation while the character is stationed at a
/// <see cref="WorkInspectCabinetsInteractable"/>'s action point.
///
/// Mirrors the authoring pattern of <see cref="WorkTableInteraction"/>:
/// the state lives on the character, the world-side interactable is used only
/// as a position lookup (via <see cref="Jailbreak.NPC.ZoneRegistry"/>).
///
/// Scope: this is the path for NPCs (and any other script-driven actor).
/// The LOCAL player still goes through <see cref="WorkInspectCabinetsInteractable.OnInteract"/>
/// which owns the progress bar, ProgressAction loop, and network broadcast.
/// This component intentionally does NOT touch the progress bar or broadcast
/// player:action events — NPCs are synced via NPCNetworkSync instead.
/// </summary>
public class WorkInspectCabinetsInteraction : MonoBehaviour
{
    [Header("References")]
    public Animator animator;
    public NavMeshAgent navMeshAgent;
    public CharacterController characterController;

    [Header("Animator")]
    [Tooltip("Bool flipped on while inspecting. Defaults to 'isWorkingTable' so the cabinet reuses the existing ButtonPushing work loop.")]
    public string animatorBoolName = "isWorkingTable";

    [Tooltip("Animator state crossfaded on start as a fallback, in case the transition isn't wired from the current state. Default: ButtonPushing (same as worktable).")]
    public string animatorStateName = "ButtonPushing";

    [Tooltip("Animator state crossfaded on stop, so the character visibly exits the inspect loop. Default: Idle.")]
    public string idleStateName = "Idle";

    /// <summary>True while snapped at the action point and playing the inspect loop.</summary>
    public bool IsInspecting => isInspecting;

    private bool isInspecting;
    private Transform currentActionPoint;

    const string InspectingState = "inspecting";
    private InteractionManager interactionManager;

    void Awake()
    {
        if (animator == null)            animator            = GetComponentInChildren<Animator>();
        if (navMeshAgent == null)        navMeshAgent        = GetComponent<NavMeshAgent>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
        interactionManager = GetComponent<InteractionManager>();
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Begin the inspect loop at <paramref name="actionPoint"/>. No-op if already inspecting.</summary>
    public void TryStartInspect(Transform actionPoint)
    {
        if (isInspecting) return;
        StartInspect(actionPoint);
    }

    /// <summary>End the inspect loop: re-enable locomotion, ground-snap, crossfade to idle. No-op if not inspecting.</summary>
    public void TryStopInspect()
    {
        if (!isInspecting) return;
        StopInspect();
    }

    /// <summary>Immediate reset with no animation. Safe for despawn / disconnect.</summary>
    public void ForceReset()
    {
        isInspecting = false;
        currentActionPoint = null;

        if (animator != null && !string.IsNullOrEmpty(animatorBoolName))
            animator.SetBool(animatorBoolName, false);

        if (navMeshAgent != null)        navMeshAgent.enabled        = true;
        if (characterController != null) characterController.enabled = true;

        if (interactionManager != null) interactionManager.PopState(InspectingState);
    }

    // ─── Private implementation ──────────────────────────────────────────────

    void StartInspect(Transform actionPoint)
    {
        isInspecting = true;
        currentActionPoint = actionPoint;

        // 1) Stop the agent cleanly BEFORE disabling, so we don't leave
        //    queued velocity that can flicker the Transform on re-enable.
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.ResetPath();
            navMeshAgent.velocity = Vector3.zero;
        }

        // 2) Disable locomotion BEFORE snapping position, matching WorkTableInteraction.
        if (characterController != null) characterController.enabled = false;
        if (navMeshAgent != null)         navMeshAgent.enabled        = false;

        // 3) Snap to the action point. Keep the actor's current Y so we don't
        //    clip into the cabinet, and apply yaw-only rotation so inherited
        //    X/Z rotations on the action-point Transform don't lay the
        //    character on its side.
        if (actionPoint != null)
        {
            transform.position = new Vector3(
                actionPoint.position.x,
                transform.position.y,
                actionPoint.position.z);
            transform.rotation = Quaternion.Euler(0f, actionPoint.eulerAngles.y, 0f);
        }

        // 4) Drive the animator. Set the bool first, then crossfade the state
        //    as a fallback so NPCs arriving from Walking enter the loop.
        if (animator != null)
        {
            if (!string.IsNullOrEmpty(animatorBoolName))
                animator.SetBool(animatorBoolName, true);

            if (!string.IsNullOrEmpty(animatorStateName)
                && animator.HasState(0, Animator.StringToHash(animatorStateName)))
            {
                animator.CrossFade(animatorStateName, 0.25f);
            }
        }

        if (interactionManager != null) interactionManager.PushState(InspectingState);
    }

    void StopInspect()
    {
        isInspecting = false;

        if (animator != null)
        {
            if (!string.IsNullOrEmpty(animatorBoolName))
                animator.SetBool(animatorBoolName, false);

            if (!string.IsNullOrEmpty(idleStateName)
                && animator.HasState(0, Animator.StringToHash(idleStateName)))
            {
                animator.CrossFade(idleStateName, 0.25f);
            }
        }

        Vector3 groundPos = ResolveGroundPosition(transform.position);

        if (characterController != null)
        {
            transform.position = groundPos;
            characterController.enabled = true;
        }
        else if (navMeshAgent != null)
        {
            transform.position = groundPos;
            navMeshAgent.enabled = true;

            if (navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.ResetPath();
                navMeshAgent.velocity = Vector3.zero;
            }
        }

        if (interactionManager != null) interactionManager.PopState(InspectingState);

        currentActionPoint = null;
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
