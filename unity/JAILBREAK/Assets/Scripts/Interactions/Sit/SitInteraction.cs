using System.Collections;
using UnityEngine;

/// <summary>
/// Handles the sit-down and stand-up animation sequences for a player avatar.
///
/// Works for both the LOCAL player (has CharacterController + InteractionManager)
/// and REMOTE avatars (those components are absent/destroyed — all null-guarded).
///
/// Usage:
///   • Local:  <see cref="SitPointInteractable.OnInteract"/> → <see cref="TrySitDown"/> / <see cref="TryStandUp"/>
///   • Remote: <see cref="SitPointInteractable.ApplyRemote"/> → same entry points
/// </summary>
public class SitInteraction : MonoBehaviour
{
    [Header("References")]
    public Animator animator;
    public CharacterController characterController;

    [Header("Animation States")]
    public string stateSitDown          = "Sitting";
    public string stateSitIdle          = "Sitting";
    public string stateStandUp          = "SitToStand";
    public float  animationEndThreshold = 0.9f;

    /// <summary>True while sitting OR while the stand-up animation is playing.</summary>
    public bool IsSitting     => isSitting || isStandingUp;
    /// <summary>True only during the settled-sit idle (not transitioning).</summary>
    public bool IsIdleSitting => isSitting && !isStandingUp;

    private SitPoint currentSitPoint;
    private bool isSitting;
    private bool isStandingUp;

    // Optional — null on remote avatars.
    private InteractionManager interactionManager;
    private InteractionLock interactionLock;

    const string SittingState = "sitting";

    void Awake()
    {
        interactionManager = GetComponent<InteractionManager>();
        interactionLock    = new InteractionLock(interactionManager); // safe with null manager
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Begin sitting on <paramref name="point"/>. No-op if already seated.</summary>
    public void TrySitDown(SitPoint point)
    {
        if (isStandingUp || isSitting) return;
        SitDown(point);
    }

    /// <summary>Begin the stand-up animation. No-op if not sitting.</summary>
    public void TryStandUp()
    {
        if (!isSitting || isStandingUp) return;
        StandUp();
    }

    /// <summary>
    /// Immediately resets all sitting state (no animation). Use for edge-case
    /// cleanup, e.g. when a remote player disconnects mid-sit.
    /// </summary>
    public void ForceReset()
    {
        StopAllCoroutines();

        isSitting    = false;
        isStandingUp = false;
        interactionLock.Release();

        if (interactionManager != null)
            interactionManager.PopState(SittingState);

        if (currentSitPoint != null)
            currentSitPoint.isOccupied = false;

        currentSitPoint = null;

        if (characterController != null)
            characterController.enabled = true;

        if (animator != null)
        {
            animator.SetBool("isSitting", false);
            animator.applyRootMotion = false;
        }
    }

    // ─── Private implementation ───────────────────────────────────────────────

    void SitDown(SitPoint point)
    {
        isSitting       = true;
        currentSitPoint = point;
        point.isOccupied = true;
        interactionLock.Acquire();

        // Disable CharacterController so root-motion can drive the position.
        if (characterController != null)
            characterController.enabled = false;

        // Disable RemotePlayerSync on remote avatars to stop interpolation fighting
        var sync = GetComponent<Jailbreak.Player.RemotePlayerSync>();
        if (sync != null)
            sync.enabled = false;

        if (animator != null)
        {
            animator.applyRootMotion = true;
            animator.SetBool("isSitting", true);
        }

        // Snap to seat: for both local and remote avatars.
        transform.position = point.GetSitPosition();
        transform.rotation = point.transform.rotation;

        StartCoroutine(SitDownRoutine());
    }

    IEnumerator SitDownRoutine()
    {
        if (animator != null)
        {
            if (animator.HasState(0, Animator.StringToHash(stateSitDown)))
                animator.CrossFade(stateSitDown, 0.2f);
        }

        // Snappy wait to let the sit animation begin and settle
        yield return new WaitForSeconds(0.35f);

        if (animator != null) animator.applyRootMotion = false;
        interactionLock.Release();
        if (interactionManager != null) interactionManager.PushState(SittingState);
    }

    void StandUp()
    {
        if (interactionManager != null) interactionManager.PopState(SittingState);
        interactionLock.Acquire();
        StartCoroutine(StandUpRoutine());
    }

    IEnumerator StandUpRoutine()
    {
        isStandingUp = true;

        if (animator != null)
        {
            animator.SetBool("isSitting", false);
            
            // If there's a specific stand up state, play it. 
            // Otherwise force them back to Idle so they don't get stuck sitting.
            if (animator.HasState(0, Animator.StringToHash(stateStandUp)))
                animator.CrossFade(stateStandUp, 0.2f);
            else
                animator.CrossFade("Idle", 0.2f);
        }

        // Just wait a snappy 0.35s to let the crossfade/stand animation finish.
        // This avoids the 2-second timeout when the state name is missing.
        yield return new WaitForSeconds(0.35f);

        Vector3 finalPosition = transform.position;

        isSitting    = false;
        isStandingUp = false;

        if (currentSitPoint != null)
            currentSitPoint.isOccupied = false;

        currentSitPoint = null;

        if (characterController != null)
        {
            transform.position = finalPosition;
            yield return null;
            characterController.enabled = true;
        }

        // Re-enable network interpolation on remote avatars
        var sync = GetComponent<Jailbreak.Player.RemotePlayerSync>();
        if (sync != null)
            sync.enabled = true;

        interactionLock.Release();
    }
}