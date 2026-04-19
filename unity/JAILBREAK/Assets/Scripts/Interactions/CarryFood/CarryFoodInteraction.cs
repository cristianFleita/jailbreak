using System.Collections;
using UnityEngine;

/// <summary>
/// Attaches a food-plate prop to a character's hand bone and drives the
/// "carrying" animation flag. Works on both the LOCAL prisoner (has
/// InteractionManager + CharacterController) and REMOTE avatars
/// (those local-only components are stripped — all null-guarded).
///
/// Mirrors the SitInteraction authoring pattern:
///   • Local:  <see cref="FoodCounterInteractable.OnInteract"/> → <see cref="TryPickUp"/>
///   • Remote: <see cref="FoodCounterInteractable.ApplyRemote"/> → <see cref="TryPickUp"/>
///   • Reconnect: <see cref="SyncFromServer"/> re-applies the plate silently.
///
/// Drop-on-sink is not implemented yet (pending sink interactable).
/// </summary>
public class CarryFoodInteraction : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Right-hand bone (or any transform) the plate will be parented to.")]
    public Transform handAttachPoint;

    [Tooltip("Plate prefab instantiated on pickup. Leave null to skip the visual (animation still plays).")]
    public GameObject platePrefab;

    [Header("Plate Offset (local-space, relative to hand)")]
    public Vector3 plateLocalPosition = Vector3.zero;
    public Vector3 plateLocalEuler    = Vector3.zero;
    public Vector3 plateLocalScale    = Vector3.one;

    [Header("Animator Flags")]
    [Tooltip("Bool parameter flipped on/off to route to CarryingIdle / CarryingWalk states.")]
    public string animBoolCarrying = "isCarrying";

    [Tooltip("Trigger parameter fired when the player starts picking up a plate. Leave empty to skip the pick animation.")]
    public string animTriggerPickUp = "PickUp";

    [Tooltip("Seconds to wait after firing the pick trigger before the plate visual appears in the hand. Tune to match the animation beat.")]
    public float plateSpawnDelay = 0.6f;

    /// <summary>Stable id of the item currently held (e.g. "food_plate"), or null.</summary>
    public string CarryingId { get; private set; }

    /// <summary>True while the plate is visually in the player's hand.</summary>
    public bool IsCarrying => !string.IsNullOrEmpty(CarryingId);

    /// <summary>True while the one-shot pick-up animation is playing (between trigger fire and plate attach).</summary>
    public bool IsPickingUp => pickUpRoutine != null;

    // Optional — null on remote avatars.
    private Animator animator;
    private GameObject plateInstance;
    private Coroutine  pickUpRoutine;

    const string FoodPlateId = "food_plate";

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
    }

    void OnDestroy()
    {
        // Avatar destroyed (player left / despawn) — clean up the plate prop.
        if (plateInstance != null) Destroy(plateInstance);
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Begin carrying the plate (plays the pick-up animation). No-op if already carrying.</summary>
    public void TryPickUp()
    {
        if (IsCarrying) return;
        ApplyPickUp(FoodPlateId, playAnimation: true);
    }

    /// <summary>Release the plate. No-op if not carrying.</summary>
    public void TryDrop()
    {
        if (!IsCarrying) return;
        ApplyDrop();
    }

    /// <summary>
    /// Re-applies carrying state from an authoritative server value without
    /// broadcasting. Used on reconnect / late-join so the plate persists.
    /// </summary>
    public void SyncFromServer(string serverCarryingId)
    {
        bool shouldCarry = !string.IsNullOrEmpty(serverCarryingId);
        // Reconnect path: skip the pick animation — just snap the plate into hand.
        if (shouldCarry && !IsCarrying)       ApplyPickUp(serverCarryingId, playAnimation: false);
        else if (!shouldCarry && IsCarrying)  ApplyDrop();
    }

    /// <summary>Immediate reset with no animation. Safe to call from OnDisable.</summary>
    public void ForceReset()
    {
        if (pickUpRoutine != null)
        {
            StopCoroutine(pickUpRoutine);
            pickUpRoutine = null;
        }

        if (plateInstance != null)
        {
            Destroy(plateInstance);
            plateInstance = null;
        }

        CarryingId = null;

        if (animator != null && !string.IsNullOrEmpty(animBoolCarrying))
            animator.SetBool(animBoolCarrying, false);
    }

    // ─── Private implementation ──────────────────────────────────────────────

    void ApplyPickUp(string itemId, bool playAnimation)
    {
        CarryingId = itemId;

        // Fire the pick-up trigger so the one-shot animation plays before the plate appears.
        if (playAnimation && animator != null && !string.IsNullOrEmpty(animTriggerPickUp)
            && HasAnimParam(animator, animTriggerPickUp, AnimatorControllerParameterType.Trigger))
        {
            animator.SetTrigger(animTriggerPickUp);

            if (pickUpRoutine != null) StopCoroutine(pickUpRoutine);
            pickUpRoutine = StartCoroutine(AttachPlateAfterDelay(plateSpawnDelay));
        }
        else
        {
            // Reconnect / no trigger available → snap plate instantly, no animation.
            AttachPlateAndFlipBool();
        }
    }

    IEnumerator AttachPlateAfterDelay(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        AttachPlateAndFlipBool();
        pickUpRoutine = null;
    }

    void AttachPlateAndFlipBool()
    {
        if (handAttachPoint != null && platePrefab != null)
        {
            plateInstance = Instantiate(platePrefab, handAttachPoint);
            plateInstance.transform.localPosition = plateLocalPosition;
            plateInstance.transform.localRotation = Quaternion.Euler(plateLocalEuler);
            plateInstance.transform.localScale    = plateLocalScale;
        }
        else if (platePrefab == null)
        {
            Debug.LogWarning($"[CarryFood] '{gameObject.name}' has no platePrefab assigned — plate visual skipped.");
        }
        else
        {
            Debug.LogWarning($"[CarryFood] '{gameObject.name}' has no handAttachPoint assigned — plate visual skipped.");
        }

        if (animator != null && !string.IsNullOrEmpty(animBoolCarrying))
            animator.SetBool(animBoolCarrying, true);
    }

    // Checks whether the animator controller actually declares a parameter of the given type.
    // Keeps us from spamming warnings when a field is left at its default but the controller
    // doesn't have the matching parameter wired up yet.
    static bool HasAnimParam(Animator a, string name, AnimatorControllerParameterType type)
    {
        foreach (var p in a.parameters)
            if (p.name == name && p.type == type) return true;
        return false;
    }

    void ApplyDrop()
    {
        CarryingId = null;

        if (plateInstance != null)
        {
            Destroy(plateInstance);
            plateInstance = null;
        }

        if (animator != null && !string.IsNullOrEmpty(animBoolCarrying))
            animator.SetBool(animBoolCarrying, false);
    }
}
