using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Character-side component that drives the looping "store clothes" animation while
/// the character is stationed at a <see cref="LaundryStoreClothesInteractable"/>'s
/// action point.
///
/// Mirrors <see cref="LaundryLoadWasherInteraction"/>: the state lives on the
/// character, the world-side interactable is used only as a position lookup
/// (via <see cref="Jailbreak.NPC.ZoneRegistry"/>).
///
/// On start:  snaps to action point, disables locomotion, plays store anim.
/// On stop:   destroys the folded clothes bundle (the laundry cycle is complete),
///            re-enables locomotion, and ground-snaps.
/// </summary>
public class LaundryStoreClothesInteraction : MonoBehaviour
{
    [Header("References")]
    public Animator animator;
    public NavMeshAgent navMeshAgent;
    public CharacterController characterController;

    [Header("Animator")]
    [Tooltip("Bool flipped on while storing clothes. Matches the ProgressAction on the world-side shelf (default: isStoringClothes).")]
    public string animatorBoolName = "isStoringClothes";

    [Tooltip("Animator state crossfaded on start as a fallback. Default: Opening.")]
    public string animatorStateName = "Opening";

    [Tooltip("Animator state crossfaded on stop. Default: Idle.")]
    public string idleStateName = "Idle";

    public bool IsWorking => isWorking;

    private bool isWorking;
    private Transform currentActionPoint;
    private InteractionManager interactionManager;

    const string ActiveState = "StoringClothes";

    void Awake()
    {
        if (animator == null)            animator            = GetComponentInChildren<Animator>();
        if (navMeshAgent == null)        navMeshAgent        = GetComponent<NavMeshAgent>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
        interactionManager = GetComponent<InteractionManager>();
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    public void TryStartWork(Transform actionPoint)
    {
        if (isWorking) return;
        StartWork(actionPoint);
    }

    public void TryStopWork()
    {
        if (!isWorking) return;
        StopWork();
    }

    public void ForceReset()
    {
        isWorking = false;
        currentActionPoint = null;

        if (animator != null && !string.IsNullOrEmpty(animatorBoolName))
            animator.SetBool(animatorBoolName, false);

        if (navMeshAgent != null)        navMeshAgent.enabled        = true;
        if (characterController != null) characterController.enabled = true;

        if (interactionManager != null) interactionManager.PopState(ActiveState);
    }

    // ─── Private implementation ──────────────────────────────────────────────

    void StartWork(Transform actionPoint)
    {
        isWorking = true;
        currentActionPoint = actionPoint;

        // 1) Stop agent cleanly
        if (navMeshAgent != null && navMeshAgent.isOnNavMesh)
        {
            navMeshAgent.ResetPath();
            navMeshAgent.velocity = Vector3.zero;
        }

        // 2) Disable locomotion
        if (characterController != null) characterController.enabled = false;
        if (navMeshAgent != null)         navMeshAgent.enabled        = false;

        // 3) Snap to action point
        if (actionPoint != null)
        {
            transform.position = new Vector3(
                actionPoint.position.x,
                transform.position.y,
                actionPoint.position.z);
            transform.rotation = Quaternion.Euler(0f, actionPoint.eulerAngles.y, 0f);
        }

        // 4) Drive the animator
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

        if (interactionManager != null) interactionManager.PushState(ActiveState);
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

        // Destroy the folded clothes — the laundry cycle is fully complete.
        var folded = GetComponent<CarryFoldedClothesInteraction>();
        if (folded != null && folded.IsCarrying)
        {
            folded.ForceReset();
        }

        // Ground-snap before re-enabling locomotion
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

        if (interactionManager != null) interactionManager.PopState(ActiveState);

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
