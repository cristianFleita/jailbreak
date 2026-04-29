using System.Collections;
using UnityEngine;

/// <summary>
/// Attaches a folded-clothes prop to a character's hand bone. Parallel to
/// <see cref="CarryClothesInteraction"/> and <see cref="CarryFoodInteraction"/>
/// — shares the <c>isCarrying</c> animator bool so prisoner locomotion routes
/// through the same CarryingIdle / CarryingWalk states (only one carry is
/// active at a time by design).
///
/// Flow:
///   • Local:  <see cref="LaundryLoadWasherInteractable.OnInteract"/> progress
///             loop → on completion → <see cref="TryPickUp"/>
///   • Remote: <see cref="LaundryLoadWasherInteractable.ApplyRemote"/> on
///             the <c>loadWasher</c> action → <see cref="TryPickUp"/>
/// </summary>
public class CarryFoldedClothesInteraction : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Hand bone (or any transform) the folded clothes will be parented to.")]
    public Transform handAttachPoint;

    [Tooltip("Folded clothes prefab instantiated on pickup. Leave null to skip the visual (animation still plays).")]
    public GameObject foldedClothesPrefab;

    [Header("Folded Clothes Offset (local-space, relative to hand)")]
    public Vector3 foldedLocalPosition = Vector3.zero;
    public Vector3 foldedLocalEuler    = Vector3.zero;
    public Vector3 foldedLocalScale    = Vector3.one;

    [Header("Animator Flags")]
    [Tooltip("Bool parameter flipped on/off to route to CarryingIdle / CarryingWalk states. Shares the same key as CarryFoodInteraction / CarryClothesInteraction by design — only one carry is active at a time.")]
    public string animBoolCarrying = "isCarrying";

    [Tooltip("Trigger fired when the player puts the folded clothes down. Leave empty to skip the put-down animation.")]
    public string animTriggerPutDown = "PutDown";

    [Tooltip("Seconds to wait after firing the put-down trigger before the folded visual is destroyed.")]
    public float foldedDestroyDelay = 0.6f;

    /// <summary>Stable id of the item currently held (e.g. "folded_clothes"), or null.</summary>
    public string CarryingId { get; private set; }

    /// <summary>True while the folded clothes are visually in the player's hand.</summary>
    public bool IsCarrying => !string.IsNullOrEmpty(CarryingId);

    [System.NonSerialized] public bool SuppressSync;

    private Animator animator;
    private GameObject foldedInstance;
    private Coroutine putDownRoutine;

    const string FoldedClothesId = "folded_clothes";

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
    }

    void OnDestroy()
    {
        if (foldedInstance != null) Destroy(foldedInstance);
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Begin carrying the folded clothes. No-op if already carrying.</summary>
    public void TryPickUp()
    {
        if (IsCarrying) return;
        ApplyPickUp(FoldedClothesId);
    }

    /// <summary>Release the folded clothes, playing the put-down animation if available. No-op if not carrying.</summary>
    public void TryDrop()
    {
        if (!IsCarrying) return;
        ApplyDrop(playAnimation: true);
    }

    /// <summary>
    /// Re-applies carrying state from an authoritative server value without
    /// broadcasting. Used on reconnect / late-join so the folded clothes persist.
    /// </summary>
    public void SyncFromServer(string serverCarryingId)
    {
        if (SuppressSync) return;

        bool isFolded = serverCarryingId == FoldedClothesId;
        if (isFolded && !IsCarrying)       ApplyPickUp(FoldedClothesId);
        else if (!isFolded && IsCarrying)  ApplyDrop(playAnimation: false);
    }

    /// <summary>Immediate reset with no animation. Safe for OnDisable / despawn.</summary>
    public void ForceReset()
    {
        if (putDownRoutine != null)
        {
            StopCoroutine(putDownRoutine);
            putDownRoutine = null;
        }

        if (foldedInstance != null)
        {
            Destroy(foldedInstance);
            foldedInstance = null;
        }

        CarryingId = null;

        if (animator != null && !string.IsNullOrEmpty(animBoolCarrying))
            animator.SetBool(animBoolCarrying, false);
    }

    // ─── Private implementation ──────────────────────────────────────────────

    void ApplyPickUp(string itemId)
    {
        CarryingId = itemId;

        if (handAttachPoint != null && foldedClothesPrefab != null)
        {
            foldedInstance = Instantiate(foldedClothesPrefab, handAttachPoint);
            foldedInstance.transform.localPosition = foldedLocalPosition;
            foldedInstance.transform.localRotation = Quaternion.Euler(foldedLocalEuler);
            foldedInstance.transform.localScale    = foldedLocalScale;
        }
        else if (foldedClothesPrefab == null)
        {
            Debug.LogWarning($"[CarryFolded] '{gameObject.name}' has no foldedClothesPrefab assigned — folded visual skipped.");
        }
        else
        {
            Debug.LogWarning($"[CarryFolded] '{gameObject.name}' has no handAttachPoint assigned — folded visual skipped.");
        }

        if (animator != null && !string.IsNullOrEmpty(animBoolCarrying))
            animator.SetBool(animBoolCarrying, true);
    }

    void ApplyDrop(bool playAnimation)
    {
        CarryingId = null;

        if (animator != null && !string.IsNullOrEmpty(animBoolCarrying))
            animator.SetBool(animBoolCarrying, false);

        if (playAnimation && animator != null && !string.IsNullOrEmpty(animTriggerPutDown)
            && HasAnimParam(animator, animTriggerPutDown, AnimatorControllerParameterType.Trigger))
        {
            animator.SetTrigger(animTriggerPutDown);

            if (putDownRoutine != null) StopCoroutine(putDownRoutine);
            putDownRoutine = StartCoroutine(DestroyFoldedAfterDelay(foldedDestroyDelay));
        }
        else
        {
            DestroyFoldedNow();
        }
    }

    IEnumerator DestroyFoldedAfterDelay(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        DestroyFoldedNow();
        putDownRoutine = null;
    }

    void DestroyFoldedNow()
    {
        if (foldedInstance != null)
        {
            Destroy(foldedInstance);
            foldedInstance = null;
        }
    }

    static bool HasAnimParam(Animator a, string name, AnimatorControllerParameterType type)
    {
        foreach (var p in a.parameters)
            if (p.name == name && p.type == type) return true;
        return false;
    }
}
