using System.Collections;
using Jailbreak.Network;
using UnityEngine;

/// <summary>
/// Laundry shelf / storage spot. The player must arrive with a folded clothes
/// bundle (from <see cref="LaundryLoadWasherInteractable"/>). Pressing E snaps
/// to the action point, starts a storing loop animation, and on completion the
/// folded clothes prop is destroyed — the laundry cycle is done.
///
/// Combines two established patterns:
///   • Progress loop: <see cref="WorkInspectCabinetsInteractable"/>
///   • Hand-prop removal + remote replay: <see cref="LaundryLoadWasherInteractable"/>
///
/// Broadcasts via <see cref="NetworkInteractable"/> so remotes replay the
/// loop (startStoreClothes), the cancel (stopStoreClothes), and the completion
/// (storeClothes — also removes the folded prop on the remote side).
/// </summary>
[RequireComponent(typeof(ProgressPointAction))]
[RequireComponent(typeof(NetworkInteractable))]
public class LaundryStoreClothesInteractable : MonoBehaviour, IInteractable
{
    [Header("UI")]
    public ProgressBar progressBar;
    public string progressLabel = "Storing clothes...";

    /// <summary>Runtime reservation flag (mirrors WorkTable / LaundryGrab for NPC flows).</summary>
    [System.NonSerialized] public bool isOccupied;

    public KeyCode InteractKey      => KeyCode.E;
    public string ActionLabel       => isActive ? "Stop" : "Store clothes";
    public int Priority             => 10;
    public Transform Transform      => transform;
    public string[] AllowedInStates => new[] { ActiveState };

    /// <summary>Only available when the local player is carrying folded clothes.</summary>
    public bool CanInteract
    {
        get
        {
            if (isActive) return true; // Allows stopping the interaction
            var folded = FindLocalFolded();
            return folded != null && folded.IsCarrying;
        }
    }

    public const string ActionStartStoreClothes = "startStoreClothes";
    public const string ActionStopStoreClothes  = "stopStoreClothes";
    public const string ActionStoreClothes      = "storeClothes";

    private bool isActive;
    private ProgressPointAction progressPointAction;
    private NetworkInteractable networkInteractable;

    // Cached per-scene.
    private CarryFoldedClothesInteraction cachedLocalFolded;

    const string ActiveState = "StoringClothes";

    void Awake()
    {
        progressPointAction = GetComponent<ProgressPointAction>();
        networkInteractable = GetComponent<NetworkInteractable>();
    }

    // ─── Interaction entry points ────────────────────────────────────────────

    public void OnInteract(Collider source)
    {
        var root     = source.transform.root;
        var animator = root.GetComponentInChildren<Animator>();
        var cc       = root.GetComponentInChildren<CharacterController>();
        var manager  = root.GetComponentInChildren<InteractionManager>();
        var folded   = root.GetComponentInChildren<CarryFoldedClothesInteraction>();

        if (animator == null) return;

        // Guard: can only start if carrying folded clothes.
        if (!isActive)
        {
            if (folded == null || !folded.IsCarrying) return;
        }

        if (!isActive)
            StartCoroutine(PlayerRoutine(animator, cc, manager, root, folded));
        else
            progressPointAction.Stop();
    }

    IEnumerator PlayerRoutine(
        Animator animator,
        CharacterController cc,
        InteractionManager manager,
        Transform player,
        CarryFoldedClothesInteraction folded)
    {
        isActive   = true;
        cc.enabled = false;

        manager.PushState(ActiveState);

        progressBar?.Show(progressLabel, progressPointAction.ProgressAction.progress);

        Broadcast(ActionStartStoreClothes);

        yield return progressPointAction.Execute(animator, player, onStop: () =>
        {
            bool completed = progressPointAction.ProgressAction.IsComplete;

            isActive   = false;
            cc.enabled = true;
            manager.PopState(ActiveState);
            progressBar?.Hide();

            if (completed)
            {
                // Success: destroy the folded clothes — the laundry cycle is done.
                if (folded != null) folded.ForceReset();
                Broadcast(ActionStoreClothes);
                progressPointAction.ProgressAction.progress = 0f;
            }
            else
            {
                // Cancelled mid-loop — folded clothes stay in hand.
                Broadcast(ActionStopStoreClothes);
            }
        });
    }

    void Update()
    {
        if (!isActive) return;
        progressBar?.UpdateProgress(progressPointAction.ProgressAction.progress);
    }

    // ─── Remote replay ───────────────────────────────────────────────────────

    /// <summary>
    /// Replays start/stop/store on a remote avatar (called by RemoteInteractionHandler).
    /// Snaps position to the action point and toggles the looping animator bool;
    /// removes the folded clothes prop on the store action.
    /// </summary>
    public void ApplyRemote(
        Transform remoteRoot,
        CarryFoldedClothesInteraction folded,
        string action)
    {
        if (remoteRoot == null) return;

        var animator = remoteRoot.GetComponentInChildren<Animator>();
        if (animator == null) return;

        string boolName = progressPointAction.ProgressAction.animatorBoolName;
        var sync = remoteRoot.GetComponent<Jailbreak.Player.RemotePlayerSync>();

        if (action == ActionStartStoreClothes)
        {
            if (progressPointAction.actionPoint != null)
            {
                remoteRoot.position = new Vector3(
                    progressPointAction.actionPoint.position.x,
                    remoteRoot.position.y,
                    progressPointAction.actionPoint.position.z);
                remoteRoot.rotation = progressPointAction.actionPoint.rotation;
            }

            if (sync != null) sync.enabled = false;

            if (!string.IsNullOrEmpty(boolName))
                animator.SetBool(boolName, true);
            return;
        }

        if (action == ActionStopStoreClothes)
        {
            if (!string.IsNullOrEmpty(boolName))
                animator.SetBool(boolName, false);

            if (sync != null) sync.enabled = true;
            return;
        }

        if (action == ActionStoreClothes)
        {
            if (!string.IsNullOrEmpty(boolName))
                animator.SetBool(boolName, false);

            if (sync != null) sync.enabled = true;

            // Destroy the folded clothes on the remote avatar.
            if (folded != null) folded.ForceReset();
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    CarryFoldedClothesInteraction FindLocalFolded()
    {
        if (cachedLocalFolded == null)
        {
#if UNITY_2023_1_OR_NEWER
            var all = Object.FindObjectsByType<CarryFoldedClothesInteraction>(FindObjectsSortMode.None);
#else
            var all = Object.FindObjectsOfType<CarryFoldedClothesInteraction>();
#endif
            foreach (var c in all)
            {
                if (c.GetComponentInChildren<InteractionManager>() != null
                    || c.transform.root.GetComponentInChildren<InteractionManager>() != null)
                {
                    cachedLocalFolded = c;
                    break;
                }
            }
        }

        return cachedLocalFolded;
    }

    void Broadcast(string action)
    {
        if (networkInteractable == null)
        {
            Debug.LogWarning($"[LaundryStoreClothes] No NetworkInteractable on {gameObject.name}. Cannot broadcast '{action}'.");
            return;
        }

        var net = NetworkManager.Instance;
        if (net == null)
        {
            Debug.LogWarning($"[LaundryStoreClothes] No NetworkManager instance. Cannot broadcast '{action}' on '{networkInteractable.NetworkId}'.");
            return;
        }

        Debug.Log($"[LaundryStoreClothes] Broadcasting '{action}' on '{networkInteractable.NetworkId}'");
        net.SendPlayerAction(networkInteractable.NetworkId, action);
    }
}
