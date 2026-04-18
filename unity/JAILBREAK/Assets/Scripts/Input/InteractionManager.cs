using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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

    /// <summary>Raised after a local interaction fires (already executed).</summary>
    public event Action<IInteractable> OnLocalInteract;

    private IInteractable current;
    private GameObject currentArrow;
    private Collider selfCollider;
    private string currentActionLabel;

    private int transitionLockCount;
    private readonly HashSet<string> activeStateTags = new HashSet<string>();

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

        foreach (var hit in hits)
        {
            IInteractable candidate = hit.GetComponent<IInteractable>();
            if (candidate == null || !candidate.CanInteract) continue;
            if (!IsAllowed(candidate)) continue;

            float dist = Vector3.Distance(transform.position, hit.transform.position);

            if (best == null
                || candidate.Priority > best.Priority
                || (candidate.Priority == best.Priority && dist < bestDist))
            {
                best = candidate;
                bestDist = dist;
            }
        }

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
        var kb = Keyboard.current;
        if (kb == null) return false;

        Key key = KeyCodeToKey(code);
        if (key == Key.None) return false;

        return kb[key].wasPressedThisFrame;
    }

    static Key KeyCodeToKey(KeyCode code)
    {
        switch (code)
        {
            case KeyCode.E:            return Key.E;
            case KeyCode.F:            return Key.F;
            case KeyCode.R:            return Key.R;
            case KeyCode.Q:            return Key.Q;
            case KeyCode.T:            return Key.T;
            case KeyCode.G:            return Key.G;
            case KeyCode.Space:        return Key.Space;
            case KeyCode.Return:       return Key.Enter;
            case KeyCode.Tab:          return Key.Tab;
            case KeyCode.LeftShift:    return Key.LeftShift;
            case KeyCode.RightShift:   return Key.RightShift;
            case KeyCode.LeftControl:  return Key.LeftCtrl;
            case KeyCode.RightControl: return Key.RightCtrl;
            case KeyCode.LeftAlt:      return Key.LeftAlt;
            case KeyCode.RightAlt:     return Key.RightAlt;
        }

        // Fallback: name-based parse catches A-Z, 0-9, F1-F12, arrows, etc.
        return Enum.TryParse(code.ToString(), out Key parsed) ? parsed : Key.None;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.2f);
        Gizmos.DrawSphere(transform.position, detectionRadius);
    }
}
