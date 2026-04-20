using System.Collections;
using UnityEngine;

/// <summary>
/// Attaches a clothes-bundle prop to a character's hand bone and drives the
/// shared <see cref="CarryFoodInteraction.animBoolCarrying"/> flag so the
/// prisoner routes to CarryingIdle / CarryingWalk while holding the bundle.
///
/// Parallel to <see cref="CarryFoodInteraction"/> — only the prop prefab and
/// the item id differ. Lives on both local and remote avatars (local-only
/// fields are null-guarded, mirroring the food flow).
///
/// Flow:
///   • Local:  <see cref="LaundryGrabClothesInteractable.OnInteract"/> progress
///             loop → on completion → <see cref="TryPickUp"/>
///   • Remote: <see cref="LaundryGrabClothesInteractable.ApplyRemote"/> on
///             the <c>grabClothes</c> action → <see cref="TryPickUp"/>
/// </summary>
public class CarryClothesInteraction : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Hand bone (or any transform) the clothes bundle will be parented to.")]
    public Transform handAttachPoint;

    [Tooltip("Clothes prefab instantiated on pickup. Leave null to skip the visual (animation still plays).")]
    public GameObject clothesPrefab;

    [Header("Clothes Offset (local-space, relative to hand)")]
    public Vector3 clothesLocalPosition = Vector3.zero;
    public Vector3 clothesLocalEuler    = Vector3.zero;
    public Vector3 clothesLocalScale    = Vector3.one;

    [Header("Animator Flags")]
    [Tooltip("Bool parameter flipped on/off to route to CarryingIdle / CarryingWalk states. Shares the same key as CarryFoodInteraction by design — only one carry is active at a time.")]
    public string animBoolCarrying = "isCarrying";

    [Tooltip("Bool flipped on while the grab-clothes rummaging animation plays (NPC path — used by TryPickUpWithGrabAnimation). Matches the Character controller's 'Searching' state gate.")]
    public string animBoolGrabbing = "isSearching";

    [Tooltip("Trigger fired when the player puts the bundle down. Leave empty to skip the put-down animation.")]
    public string animTriggerPutDown = "PutDown";

    [Tooltip("Seconds to wait after firing the put-down trigger before the bundle visual is destroyed.")]
    public float clothesDestroyDelay = 0.6f;

    /// <summary>Stable id of the item currently held (e.g. "clothes_bundle"), or null.</summary>
    public string CarryingId { get; private set; }

    /// <summary>True while the bundle is visually in the player's hand.</summary>
    public bool IsCarrying => !string.IsNullOrEmpty(CarryingId);

    private Animator animator;
    private GameObject clothesInstance;
    private Coroutine putDownRoutine;
    private Coroutine grabRoutine;

    const string ClothesBundleId = "clothes_bundle";

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
    }

    void OnDestroy()
    {
        // Avatar destroyed (player left / despawn) — clean up the clothes prop.
        if (clothesInstance != null) Destroy(clothesInstance);
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Begin carrying the clothes bundle. No-op if already carrying.</summary>
    public void TryPickUp()
    {
        if (IsCarrying) return;
        ApplyPickUp(ClothesBundleId);
    }

    /// <summary>
    /// NPC path. Plays the rummaging loop (isSearching=true) for <paramref name="duration"/>
    /// seconds, then clears the bool, attaches the clothes prefab in-hand, and flips
    /// isCarrying. Mirrors <see cref="CarryFoodInteraction.TryPickUp"/>'s trigger+delay
    /// flow — the NPC stays in place (no snap / agent disable), the animator transitions
    /// via the Character controller's Searching state while the timer runs.
    /// No-op if already carrying.
    /// </summary>
    public void TryPickUpWithGrabAnimation(float duration)
    {
        if (IsCarrying) return;
        if (grabRoutine != null) StopCoroutine(grabRoutine);
        grabRoutine = StartCoroutine(GrabAnimationRoutine(duration));
    }

    /// <summary>Release the bundle, playing the put-down animation if available. No-op if not carrying.</summary>
    public void TryDrop()
    {
        if (!IsCarrying) return;
        ApplyDrop(playAnimation: true);
    }

    /// <summary>
    /// Re-applies carrying state from an authoritative server value without
    /// broadcasting. Used on reconnect / late-join so the bundle persists.
    ///
    /// Scoped to the <c>clothes_bundle</c> id — other carry ids (e.g.
    /// <c>food_plate</c>) are owned by their own components. A null or
    /// mismatching id while carrying still drops the bundle so stale props
    /// don't linger.
    /// </summary>
    public void SyncFromServer(string serverCarryingId)
    {
        bool isClothes = serverCarryingId == ClothesBundleId;
        // ApplyPickUp already snaps the prop without a pick trigger (the
        // rummaging loop owns the grab animation) so it doubles as the
        // silent reconnect path.
        if (isClothes && !IsCarrying)       ApplyPickUp(ClothesBundleId);
        else if (!isClothes && IsCarrying)  ApplyDrop(playAnimation: false);
    }

    /// <summary>Immediate reset with no animation. Safe for OnDisable / despawn.</summary>
    public void ForceReset()
    {
        if (putDownRoutine != null)
        {
            StopCoroutine(putDownRoutine);
            putDownRoutine = null;
        }

        if (grabRoutine != null)
        {
            StopCoroutine(grabRoutine);
            grabRoutine = null;
        }

        if (clothesInstance != null)
        {
            Destroy(clothesInstance);
            clothesInstance = null;
        }

        CarryingId = null;

        if (animator != null)
        {
            if (!string.IsNullOrEmpty(animBoolCarrying)) animator.SetBool(animBoolCarrying, false);
            if (!string.IsNullOrEmpty(animBoolGrabbing)) animator.SetBool(animBoolGrabbing, false);
        }
    }

    // ─── Private implementation ──────────────────────────────────────────────

    void ApplyPickUp(string itemId)
    {
        CarryingId = itemId;

        // The rummaging progress loop has already played the "grab" beat, so
        // we snap the bundle in-hand immediately — no extra pick trigger.
        if (handAttachPoint != null && clothesPrefab != null)
        {
            clothesInstance = Instantiate(clothesPrefab, handAttachPoint);
            clothesInstance.transform.localPosition = clothesLocalPosition;
            clothesInstance.transform.localRotation = Quaternion.Euler(clothesLocalEuler);
            clothesInstance.transform.localScale    = clothesLocalScale;
        }
        else if (clothesPrefab == null)
        {
            Debug.LogWarning($"[CarryClothes] '{gameObject.name}' has no clothesPrefab assigned — clothes visual skipped.");
        }
        else
        {
            Debug.LogWarning($"[CarryClothes] '{gameObject.name}' has no handAttachPoint assigned — clothes visual skipped.");
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
            putDownRoutine = StartCoroutine(DestroyClothesAfterDelay(clothesDestroyDelay));
        }
        else
        {
            DestroyClothesNow();
        }
    }

    IEnumerator DestroyClothesAfterDelay(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        DestroyClothesNow();
        putDownRoutine = null;
    }

    IEnumerator GrabAnimationRoutine(float duration)
    {
        if (animator != null && !string.IsNullOrEmpty(animBoolGrabbing))
            animator.SetBool(animBoolGrabbing, true);

        if (duration > 0f) yield return new WaitForSeconds(duration);

        if (animator != null && !string.IsNullOrEmpty(animBoolGrabbing))
            animator.SetBool(animBoolGrabbing, false);

        grabRoutine = null;

        ApplyPickUp(ClothesBundleId);
    }

    void DestroyClothesNow()
    {
        if (clothesInstance != null)
        {
            Destroy(clothesInstance);
            clothesInstance = null;
        }
    }

    static bool HasAnimParam(Animator a, string name, AnimatorControllerParameterType type)
    {
        foreach (var p in a.parameters)
            if (p.name == name && p.type == type) return true;
        return false;
    }
}
