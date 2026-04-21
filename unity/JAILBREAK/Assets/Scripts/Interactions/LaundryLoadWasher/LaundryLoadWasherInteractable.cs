using System.Collections;
using Jailbreak.Network;
using UnityEngine;

/// <summary>
/// Washing machine. The player must arrive with an unfolded clothes bundle
/// (from <see cref="LaundryGrabClothesInteractable"/>). Pressing E snaps to
/// the action point, silently removes the bundle from the hand, and starts a
/// looping work animation. On completion, a folded clothes prop is attached
/// via <see cref="CarryFoldedClothesInteraction"/>. If cancelled mid-loop
/// (E pressed again), the unfolded bundle is restored to the hand.
///
/// Combines two established patterns:
///   • Progress loop: <see cref="WorkTableInteractable"/> / <see cref="LaundryGrabClothesInteractable"/>
///   • Hand-prop swap + remote replay: <see cref="LaundryGrabClothesInteractable"/>
///
/// Broadcasts via <see cref="NetworkInteractable"/> so remotes replay the
/// loop (startLoadWasher), the cancel (stopLoadWasher), and the swap
/// (loadWasher — also ends the loop on the remote side).
/// </summary>
[RequireComponent(typeof(ProgressPointAction))]
[RequireComponent(typeof(NetworkInteractable))]
public class LaundryLoadWasherInteractable : MonoBehaviour, IInteractable
{
    [Header("UI")]
    public ProgressBar progressBar;
    public string progressLabel = "Loading washer...";

    /// <summary>Runtime reservation flag (mirrors WorkTable / LaundryGrab for NPC flows).</summary>
    [System.NonSerialized] public bool isOccupied;

    public KeyCode InteractKey      => KeyCode.E;
    public string ActionLabel       => isActive ? "Stop" : "Load washer";
    public int Priority             => 10;
    public Transform Transform      => transform;
    public string[] AllowedInStates => new[] { ActiveState };

    /// <summary>Only available when the local player is carrying an unfolded clothes bundle (and nothing else).</summary>
    public bool CanInteract
    {
        get
        {
            if (isActive) return true; // Allows stopping the interaction
            
            var carries = FindLocalCarries();
            if (carries.clothes == null || !carries.clothes.IsCarrying) return false;
            if (carries.folded != null && carries.folded.IsCarrying)    return false;
            if (carries.food   != null && carries.food.IsCarrying)      return false;
            return true;
        }
    }

    public const string ActionStartLoadWasher = "startLoadWasher";
    public const string ActionStopLoadWasher  = "stopLoadWasher";
    public const string ActionLoadWasher      = "loadWasher";

    private bool isActive;
    private ProgressPointAction progressPointAction;
    private NetworkInteractable networkInteractable;

    // Cached per-scene — same trick as FoodCounter's CanInteract.
    private CarryClothesInteraction       cachedLocalClothes;
    private CarryFoldedClothesInteraction cachedLocalFolded;
    private CarryFoodInteraction          cachedLocalFood;

    const string ActiveState = "LoadingWasher";

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
        var clothes  = root.GetComponentInChildren<CarryClothesInteraction>();
        var folded   = root.GetComponentInChildren<CarryFoldedClothesInteraction>();

        if (animator == null) return;

        // Guard: can only start if carrying unfolded clothes and nothing else.
        if (!isActive)
        {
            if (clothes == null || !clothes.IsCarrying) return;
            if (folded != null && folded.IsCarrying)    return;
        }

        if (!isActive)
            StartCoroutine(PlayerRoutine(animator, cc, manager, root, clothes, folded));
        else
            progressPointAction.Stop();
    }

    IEnumerator PlayerRoutine(
        Animator animator,
        CharacterController cc,
        InteractionManager manager,
        Transform player,
        CarryClothesInteraction clothes,
        CarryFoldedClothesInteraction folded)
    {
        isActive   = true;
        cc.enabled = false;

        manager.PushState(ActiveState);

        progressBar?.Show(progressLabel, progressPointAction.ProgressAction.progress);

        // Silently remove the unfolded bundle — it's "going into the washer".
        // SuppressSync prevents the server's player:state from re-creating
        // the prop via SyncFromServer while we're in this interaction.
        if (clothes != null)
        {
            clothes.SuppressSync = true;
            clothes.ForceReset();

            // Extra safety: if ForceReset's tracked clothesInstance was stale/null,
            // hunt down the visual clone directly on the hand bone and destroy it.
            if (clothes.handAttachPoint != null && clothes.clothesPrefab != null)
            {
                string prefabName = clothes.clothesPrefab.name;
                for (int i = clothes.handAttachPoint.childCount - 1; i >= 0; i--)
                {
                    var child = clothes.handAttachPoint.GetChild(i);
                    if (child.name.StartsWith(prefabName))
                    {
                        Destroy(child.gameObject);
                        Debug.Log($"[LoadWasher] Fallback-destroyed orphaned '{child.name}' on hand.");
                    }
                }
            }

            Debug.Log($"[LoadWasher] Clothes ForceReset — SuppressSync=true, IsCarrying={clothes.IsCarrying}");
        }

        Broadcast(ActionStartLoadWasher);

        yield return progressPointAction.Execute(animator, player, onStop: () =>
        {
            bool completed = progressPointAction.ProgressAction.IsComplete;

            isActive   = false;
            cc.enabled = true;
            manager.PopState(ActiveState);
            progressBar?.Hide();

            if (completed)
            {
                // Success: attach folded clothes locally + tell remotes to do the same.
                // Broadcast FIRST so the server starts updating its carrying state.
                Broadcast(ActionLoadWasher);
                if (folded != null && !folded.IsCarrying) folded.TryPickUp();
                progressPointAction.ProgressAction.progress = 0f;

                // Keep SuppressSync=true until the server has had time to process
                // the action and stop sending stale carrying="clothes_bundle" ticks.
                // Without this delay, GameStateManager.SyncLocalCarrying re-creates
                // the clothes prop from the stale player:state before the server
                // clears the carrying field.
                if (clothes != null) StartCoroutine(DelayedUnsuppressSync(clothes));
            }
            else
            {
                // Cancelled mid-loop — restore the unfolded bundle and tell remotes.
                Broadcast(ActionStopLoadWasher);
                if (clothes != null)
                {
                    clothes.SuppressSync = false;
                    if (!clothes.IsCarrying) clothes.TryPickUp();
                }
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
    /// Replays start/stop/load on a remote avatar (called by RemoteInteractionHandler).
    /// Snaps position to the action point and toggles the looping animator bool;
    /// swaps hand props to match the action.
    /// </summary>
    public void ApplyRemote(
        Transform remoteRoot,
        CarryClothesInteraction clothes,
        CarryFoldedClothesInteraction folded,
        string action)
    {
        if (remoteRoot == null) return;

        var animator = remoteRoot.GetComponentInChildren<Animator>();
        if (animator == null) return;

        string boolName = progressPointAction.ProgressAction.animatorBoolName;
        var sync = remoteRoot.GetComponent<Jailbreak.Player.RemotePlayerSync>();

        if (action == ActionStartLoadWasher)
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

            // Silently remove the bundle on the remote to match the local player.
            if (clothes != null) clothes.ForceReset();

            if (!string.IsNullOrEmpty(boolName))
                animator.SetBool(boolName, true);
            return;
        }

        if (action == ActionStopLoadWasher)
        {
            if (!string.IsNullOrEmpty(boolName))
                animator.SetBool(boolName, false);

            if (sync != null) sync.enabled = true;

            // Restore the unfolded bundle (cancel path).
            if (clothes != null && !clothes.IsCarrying) clothes.TryPickUp();
            return;
        }

        if (action == ActionLoadWasher)
        {
            if (!string.IsNullOrEmpty(boolName))
                animator.SetBool(boolName, false);

            if (sync != null) sync.enabled = true;

            // Attach the folded clothes on the remote.
            if (folded != null && !folded.IsCarrying) folded.TryPickUp();
        }
    }

    /// <summary>
    /// Waits a short window before re-enabling SyncFromServer, giving the
    /// server enough time to process the loadWasher action and clear the
    /// stale carrying="clothes_bundle" from player:state ticks.
    /// </summary>
    IEnumerator DelayedUnsuppressSync(CarryClothesInteraction clothes)
    {
        yield return new WaitForSeconds(0.5f);
        if (clothes != null) clothes.SuppressSync = false;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    (CarryClothesInteraction clothes, CarryFoldedClothesInteraction folded, CarryFoodInteraction food) FindLocalCarries()
    {
        if (cachedLocalClothes == null)
        {
#if UNITY_2023_1_OR_NEWER
            var all = Object.FindObjectsByType<CarryClothesInteraction>(FindObjectsSortMode.None);
#else
            var all = Object.FindObjectsOfType<CarryClothesInteraction>();
#endif
            foreach (var c in all)
            {
                if (c.GetComponentInChildren<InteractionManager>() != null || c.transform.root.GetComponentInChildren<InteractionManager>() != null) { cachedLocalClothes = c; break; }
            }
        }

        if (cachedLocalFolded == null)
        {
#if UNITY_2023_1_OR_NEWER
            var all = Object.FindObjectsByType<CarryFoldedClothesInteraction>(FindObjectsSortMode.None);
#else
            var all = Object.FindObjectsOfType<CarryFoldedClothesInteraction>();
#endif
            foreach (var c in all)
            {
                if (c.GetComponentInChildren<InteractionManager>() != null || c.transform.root.GetComponentInChildren<InteractionManager>() != null) { cachedLocalFolded = c; break; }
            }
        }

        if (cachedLocalFood == null)
        {
#if UNITY_2023_1_OR_NEWER
            var all = Object.FindObjectsByType<CarryFoodInteraction>(FindObjectsSortMode.None);
#else
            var all = Object.FindObjectsOfType<CarryFoodInteraction>();
#endif
            foreach (var c in all)
            {
                if (c.GetComponentInChildren<InteractionManager>() != null || c.transform.root.GetComponentInChildren<InteractionManager>() != null) { cachedLocalFood = c; break; }
            }
        }

        return (cachedLocalClothes, cachedLocalFolded, cachedLocalFood);
    }

    void Broadcast(string action)
    {
        if (networkInteractable == null)
        {
            Debug.LogWarning($"[LaundryLoadWasher] No NetworkInteractable on {gameObject.name}. Cannot broadcast '{action}'.");
            return;
        }

        var net = NetworkManager.Instance;
        if (net == null)
        {
            Debug.LogWarning($"[LaundryLoadWasher] No NetworkManager instance. Cannot broadcast '{action}' on '{networkInteractable.NetworkId}'.");
            return;
        }

        Debug.Log($"[LaundryLoadWasher] Broadcasting '{action}' on '{networkInteractable.NetworkId}'");
        net.SendPlayerAction(networkInteractable.NetworkId, action);
    }
}
