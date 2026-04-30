using Jailbreak.Audio;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Character-side component that drives the looping "working" animation while
/// the character is stationed at a <see cref="WorkTableInteractable"/>'s
/// action point.
///
/// Mirrors the authoring pattern of <see cref="SitInteraction"/> and
/// <see cref="CarryFoodInteraction"/>: the state lives on the character,
/// the world-side <see cref="WorkTableInteractable"/> is used only as a
/// position lookup (via <see cref="Jailbreak.NPC.ZoneRegistry"/>).
///
/// Scope: this is the path for NPCs (and any other script-driven actor).
/// The LOCAL player still goes through <see cref="WorkTableInteractable.OnInteract"/>
/// which owns the progress bar, ProgressAction loop, and network broadcast.
/// This component intentionally does NOT touch the progress bar or broadcast
/// player:action events — NPCs are synced via NPCNetworkSync instead.
/// </summary>
public class WorkTableInteraction : MonoBehaviour
{
    [Header("References")]
    public Animator animator;
    public NavMeshAgent navMeshAgent;
    public CharacterController characterController;

    [Header("Animator")]
    [Tooltip("Bool flipped on while working. Matches the ProgressAction on the world-side work table (default: isWorkingTable).")]
    public string animatorBoolName = "isWorkingTable";

    [Tooltip("Animator state crossfaded on start as a fallback, in case the bool transition isn't wired from the current state (e.g. arriving from Walking on NPCs). Default: ButtonPushing.")]
    public string animatorStateName = "ButtonPushing";

    [Tooltip("Animator state crossfaded on stop, so the character visibly exits the work loop. Default: Idle.")]
    public string idleStateName = "Idle";

    /// <summary>True while snapped at the action point and playing the work loop.</summary>
    public bool IsWorking => isWorking;

    private bool isWorking;
    private Transform currentActionPoint;
    private ProgressInteractableLoopingSfx currentLoopingSfx;

    const string WorkingState = "working";
    private InteractionManager interactionManager;

    void Awake()
    {
        if (animator == null)            animator            = GetComponentInChildren<Animator>();
        if (navMeshAgent == null)        navMeshAgent        = GetComponent<NavMeshAgent>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
        interactionManager = GetComponent<InteractionManager>();
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Begin the work loop at <paramref name="actionPoint"/>. No-op if already working.</summary>
    public void TryStartWork(Transform actionPoint)
    {
        if (isWorking) return;
        StartWork(actionPoint);
    }

    /// <summary>End the work loop: re-enable locomotion, ground-snap, crossfade to idle. No-op if not working.</summary>
    public void TryStopWork()
    {
        if (!isWorking) return;
        StopWork();
    }

    /// <summary>Immediate reset with no animation. Safe for despawn / disconnect.</summary>
    public void ForceReset()
    {
        isWorking = false;
        currentActionPoint = null;
        if (currentLoopingSfx != null) currentLoopingSfx.StopExternal(this);
        currentLoopingSfx = null;

        if (animator != null && !string.IsNullOrEmpty(animatorBoolName))
            animator.SetBool(animatorBoolName, false);

        if (navMeshAgent != null)        navMeshAgent.enabled        = true;
        if (characterController != null) characterController.enabled = true;

        if (interactionManager != null) interactionManager.PopState(WorkingState);
    }

    // ─── Private implementation ──────────────────────────────────────────────

    void StartWork(Transform actionPoint)
    {
        isWorking = true;
        currentActionPoint = actionPoint;
        currentLoopingSfx = ProgressInteractableLoopingSfx.FindForActionPoint(actionPoint);
        if (currentLoopingSfx != null) currentLoopingSfx.PlayExternal(this);

        // 1) Stop the agent cleanly BEFORE disabling, so we don't leave
        //    queued velocity that can flicker the Transform on re-enable.
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.ResetPath();
            navMeshAgent.velocity = Vector3.zero;
        }

        // 2) Disable locomotion BEFORE snapping position, matching SitInteraction.
        if (characterController != null) characterController.enabled = false;
        if (navMeshAgent != null)         navMeshAgent.enabled        = false;

        // 3) Snap to the action point. Keep the actor's current Y so we don't
        //    clip into the desk, and apply yaw-only rotation so inherited
        //    X/Z rotations on the action-point Transform (e.g. the Desk
        //    prefab's -90/90/90 gizmo orientation) don't lay the character
        //    on its side.
        if (actionPoint != null)
        {
            transform.position = new Vector3(
                actionPoint.position.x,
                transform.position.y,
                actionPoint.position.z);
            transform.rotation = Quaternion.Euler(0f, actionPoint.eulerAngles.y, 0f);
        }

        // 4) Drive the animator. Set the bool first (in case the player-side
        //    transition is wired to it) and crossfade the work state as a
        //    fallback so NPCs coming from Walking also enter the loop.
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

        if (interactionManager != null) interactionManager.PushState(WorkingState);
    }

    void StopWork()
    {
        isWorking = false;

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

        // Ground-snap before re-enabling locomotion so the actor doesn't
        // remain floating if the action point sits above the NavMesh.
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

            // Clear any residual path/velocity on re-enable.
            if (navMeshAgent.isOnNavMesh)
            {
                navMeshAgent.ResetPath();
                navMeshAgent.velocity = Vector3.zero;
            }
        }

        if (interactionManager != null) interactionManager.PopState(WorkingState);

        if (currentLoopingSfx != null) currentLoopingSfx.StopExternal(this);
        currentLoopingSfx = null;
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
