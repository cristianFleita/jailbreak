using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scans for nearby IInteractables and routes the interaction key
/// (new Input System) to the best candidate. Lives on the local
/// player prefab. On remote players this component is destroyed by
/// GameStateManager.
/// </summary>
public class InteractionManager : MonoBehaviour
{
    [Header("Detection")]
    public float detectionRadius = 2f;
    public LayerMask interactableLayer;

    [Header("UI (auto-located from scene if null)")]
    public InteractionPrompt prompt;

    [Header("Arrow")]
    public GameObject arrowPrefab;

    [Header("Debug")]
    [Tooltip("Logs interactable detection, candidate skips, and final selection. Disable once pickup setup is verified.")]
    public bool debugDetection = false;
    public float debugLogInterval = 0.75f;

    /// <summary>Raised after a local interaction fires (already executed).</summary>
    public event Action<IInteractable> OnLocalInteract;

    private IInteractable current;
    private GameObject currentArrow;
    private Collider selfCollider;
    private string currentActionLabel;

    private int transitionLockCount;
    private readonly HashSet<string> activeStateTags = new HashSet<string>();
    private string lastDebugMessage;
    private float nextDebugLogTime;

    void Awake()
    {
        selfCollider = GetComponent<Collider>();
    }

    void Start()
    {
        EnsurePrompt();
    }

    void Update()
    {
        if (transitionLockCount > 0) return;

        // Lazy re-locate the prompt: it lives on a scene canvas and may
        // load AFTER this component (GameScene → UI spawn order).
        if (prompt == null) EnsurePrompt();

        IInteractable best = DetectBest();

        if (best != current)
            OnCandidateChanged(best);
        else if (best != null && best.ActionLabel != currentActionLabel)
            RefreshPrompt(best);

        current = best;

        if (current != null && IsKeyPressedThisFrame(current.InteractKey))
        {
            DebugDetection($"Interact pressed: {Describe(current)} source={selfCollider?.name ?? "null"}", true);
            current.OnInteract(selfCollider);
            OnLocalInteract?.Invoke(current);
        }
    }

    void LateUpdate()
    {
        if (current == null) return;
        InteractionIndicator.UpdateArrow(currentArrow, current.Transform.position);
    }

    public void PushLock()
    {
        transitionLockCount++;
        if (transitionLockCount == 1)
        {
            OnCandidateChanged(null);
            current = null;
            currentActionLabel = null;
        }
    }

    public void PopLock()
    {
        transitionLockCount = Mathf.Max(0, transitionLockCount - 1);
    }

    public void PushState(string tag) => activeStateTags.Add(tag);
    public void PopState(string tag)  => activeStateTags.Remove(tag);
    public bool HasState(string tag)  => activeStateTags.Contains(tag);

    IInteractable DetectBest()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, detectionRadius, interactableLayer, QueryTriggerInteraction.Collide);

        IInteractable best = null;
        float bestDist = Mathf.Infinity;
        int candidateCount = 0;

        foreach (var hit in hits)
        {
            var candidates = hit.GetComponents<IInteractable>();
            if (candidates == null || candidates.Length == 0)
                candidates = hit.GetComponentsInParent<IInteractable>();

            if (candidates == null || candidates.Length == 0)
            {
                DebugDetection($"Hit {hit.name} on layer {LayerMask.LayerToName(hit.gameObject.layer)} but found no IInteractable");
                continue;
            }

            foreach (var candidate in candidates)
            {
                candidateCount++;
                if (candidate == null) continue;

                bool canInteract = candidate.CanInteract;
                bool allowed = canInteract && IsAllowed(candidate);
                if (!canInteract || !allowed)
                {
                    DebugDetection($"Skip {Describe(candidate)} hit={hit.name} canInteract={canInteract} allowed={allowed}");
                    continue;
                }

                float dist = Vector3.Distance(transform.position, candidate.Transform.position);

                if (best == null
                    || candidate.Priority > best.Priority
                    || (candidate.Priority == best.Priority && dist < bestDist))
                {
                    best = candidate;
                    bestDist = dist;
                }
            }
        }

        if (best != null)
            DebugDetection($"Best {Describe(best)} hits={hits.Length} candidates={candidateCount} dist={bestDist:F2}");
        else if (hits.Length > 0)
            DebugDetection($"No usable interactable from hits={hits.Length} candidates={candidateCount}");

        return best;
    }

    bool IsAllowed(IInteractable candidate)
    {
        // null AllowedInStates = universally available in any state
        if (candidate.AllowedInStates == null) return true;

        // No active state tags = default state — allow everything
        if (activeStateTags.Count == 0) return true;

        // Empty array = only available in the default (no-tag) state
        if (candidate.AllowedInStates.Length == 0) return false;

        // Check if any declared state matches the active tags
        foreach (var tag in candidate.AllowedInStates)
            if (activeStateTags.Contains(tag)) return true;

        return false;
    }

    void OnCandidateChanged(IInteractable next)
    {
        InteractionIndicator.DestroyArrow(ref currentArrow);
        prompt?.Hide();

        if (next == null)
        {
            currentActionLabel = null;
            return;
        }

        currentActionLabel = next.ActionLabel;
        prompt?.Show(next.InteractKey, next.ActionLabel);
        currentArrow = InteractionIndicator.CreateArrow(arrowPrefab);
        InteractionIndicator.UpdateArrow(currentArrow, next.Transform.position);
        DebugDetection($"Prompt shown: {Describe(next)} prompt={(prompt != null ? prompt.name : "null")} arrow={(currentArrow != null ? currentArrow.name : "null")}", true);
    }

    void RefreshPrompt(IInteractable interactable)
    {
        currentActionLabel = interactable.ActionLabel;
        prompt?.Show(interactable.InteractKey, interactable.ActionLabel);
    }

    void EnsurePrompt()
    {
        if (prompt != null) return;
#if UNITY_2023_1_OR_NEWER
        prompt = FindFirstObjectByType<InteractionPrompt>(FindObjectsInactive.Include);
#else
        prompt = FindObjectOfType<InteractionPrompt>(true);
#endif
    }

    // ─── Input System key translation ──────────────────────────────────────

    static bool IsKeyPressedThisFrame(KeyCode code)
    {
        return InputSystemKey.WasPressedThisFrame(code);
    }

    private void DebugDetection(string message, bool force = false)
    {
        if (!debugDetection) return;
        if (!force && Time.unscaledTime < nextDebugLogTime) return;
        if (message == lastDebugMessage && Time.unscaledTime < nextDebugLogTime) return;

        lastDebugMessage = message;
        nextDebugLogTime = Time.unscaledTime + Mathf.Max(0.1f, debugLogInterval);
        Debug.Log($"[InteractionManager] {message}", this);
    }

    private static string Describe(IInteractable interactable)
    {
        if (interactable == null) return "null";

        string extra = "";
        if (interactable is NetworkRoutePickable routePickable)
            extra = $" {routePickable.DebugState}";

        return $"{interactable.GetType().Name} label='{interactable.ActionLabel}' key={interactable.InteractKey} priority={interactable.Priority} object={interactable.Transform.name}{extra}";
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.2f);
        Gizmos.DrawSphere(transform.position, detectionRadius);
    }
}
