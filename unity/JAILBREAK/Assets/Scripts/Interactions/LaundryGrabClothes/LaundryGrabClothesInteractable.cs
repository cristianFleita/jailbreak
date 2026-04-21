using System.Collections;
using Jailbreak.Network;
using UnityEngine;

/// <summary>
/// Laundry pile. Player presses E next to it to begin a rummaging progress
/// loop (snaps to the action point + looping animator bool). On completion,
/// a clothes bundle is attached to the hand via <see cref="CarryClothesInteraction"/>.
///
/// Combines two established patterns:
///   • Progress loop: <see cref="WorkInspectCabinetsInteractable"/> / <see cref="WorkTableInteractable"/>
///   • Hand-prop attach + remote replay: <see cref="FoodCounterInteractable"/>
///
/// Broadcasts via <see cref="NetworkInteractable"/> so remotes replay the
/// loop (startGrabClothes), the cancel (stopGrabClothes), and the pickup
/// (grabClothes — also ends the loop on the remote side).
/// </summary>
[RequireComponent(typeof(ProgressPointAction))]
[RequireComponent(typeof(NetworkInteractable))]
public class LaundryGrabClothesInteractable : MonoBehaviour, IInteractable
{
    [Header("UI")]
    public ProgressBar progressBar;
    public string progressLabel = "Grabbing clothes...";

    /// <summary>Runtime reservation flag (mirrors WorkTable / InspectCabinets for NPC flows).</summary>
    [System.NonSerialized] public bool isOccupied;

    public KeyCode InteractKey      => KeyCode.E;
    public string ActionLabel       => isActive ? "Stop" : "Grab clothes";
    public int Priority             => 10;
    public Transform Transform      => transform;
    public string[] AllowedInStates => new[] { ActiveState };

    /// <summary>Hidden when the player is already carrying clothes or any other item.</summary>
    public bool CanInteract
    {
        get
        {
            var carries = FindLocalCarries();
            if (carries.clothes != null && carries.clothes.IsCarrying) return false;
            if (carries.folded  != null && carries.folded.IsCarrying)  return false;
            if (carries.food    != null && carries.food.IsCarrying)    return false;
            return true;
        }
    }

    public const string ActionStartGrabClothes = "startGrabClothes";
    public const string ActionStopGrabClothes  = "stopGrabClothes";
    public const string ActionGrabClothes      = "grabClothes";

    private bool isActive;
    private ProgressPointAction progressPointAction;
    private NetworkInteractable networkInteractable;

    // Cached per-scene — same trick as FoodCounter's CanInteract.
    private CarryClothesInteraction       cachedLocalClothes;
    private CarryFoldedClothesInteraction cachedLocalFolded;
    private CarryFoodInteraction          cachedLocalFood;

    const string ActiveState = "GrabbingClothes";

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
        var cc       = root.GetComponent<CharacterController>();
        var manager  = root.GetComponent<InteractionManager>();
        var clothes  = root.GetComponent<CarryClothesInteraction>();

        if (animator == null) return;

        // Guard: don't start the loop if the player is already carrying something.
        if (!isActive && clothes != null && clothes.IsCarrying) return;
        var food = root.GetComponent<CarryFoodInteraction>();
        if (!isActive && food != null && food.IsCarrying) return;

        if (!isActive)
            StartCoroutine(PlayerRoutine(animator, cc, manager, root, clothes));
        else
            progressPointAction.Stop();
    }

    IEnumerator PlayerRoutine(
        Animator animator,
        CharacterController cc,
        InteractionManager manager,
        Transform player,
        CarryClothesInteraction clothes)
    {
        isActive   = true;
        cc.enabled = false;

        manager.PushState(ActiveState);

        progressBar?.Show(progressLabel, progressPointAction.ProgressAction.progress);

        Broadcast(ActionStartGrabClothes);

        yield return progressPointAction.Execute(animator, player, onStop: () =>
        {
            bool completed = progressPointAction.ProgressAction.IsComplete;

            isActive   = false;
            cc.enabled = true;
            manager.PopState(ActiveState);
            progressBar?.Hide();

            if (completed && clothes != null && !clothes.IsCarrying)
            {
                // Success: attach the bundle locally + tell remotes to do the same.
                clothes.TryPickUp();
                Broadcast(ActionGrabClothes);
                // Reset progress so a fresh attempt starts at 0 next time.
                progressPointAction.ProgressAction.progress = 0f;
            }
            else
            {
                // Cancelled mid-loop (E pressed again) — no bundle attached.
                Broadcast(ActionStopGrabClothes);
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
    /// Replays start/stop/grab on a remote avatar (called by RemoteInteractionHandler).
    /// Snaps position to the action point and toggles the looping animator bool;
    /// on <c>grabClothes</c>, also attaches the bundle to the remote's hand.
    /// </summary>
    public void ApplyRemote(Transform remoteRoot, CarryClothesInteraction clothes, string action)
    {
        if (remoteRoot == null) return;

        var animator = remoteRoot.GetComponentInChildren<Animator>();
        if (animator == null) return;

        string boolName = progressPointAction.ProgressAction.animatorBoolName;
        var sync = remoteRoot.GetComponent<Jailbreak.Player.RemotePlayerSync>();

        if (action == ActionStartGrabClothes)
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

        if (action == ActionStopGrabClothes)
        {
            if (!string.IsNullOrEmpty(boolName))
                animator.SetBool(boolName, false);

            if (sync != null) sync.enabled = true;
            return;
        }

        if (action == ActionGrabClothes)
        {
            // Stop the loop, re-enable sync, then attach the bundle on the remote.
            if (!string.IsNullOrEmpty(boolName))
                animator.SetBool(boolName, false);

            if (sync != null) sync.enabled = true;

            if (clothes != null) clothes.TryPickUp();
        }
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
                if (c.GetComponent<InteractionManager>() != null) { cachedLocalClothes = c; break; }
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
                if (c.GetComponent<InteractionManager>() != null) { cachedLocalFolded = c; break; }
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
                if (c.GetComponent<InteractionManager>() != null) { cachedLocalFood = c; break; }
            }
        }

        return (cachedLocalClothes, cachedLocalFolded, cachedLocalFood);
    }

    void Broadcast(string action)
    {
        if (networkInteractable == null)
        {
            Debug.LogWarning($"[LaundryGrab] No NetworkInteractable on {gameObject.name}. Cannot broadcast '{action}'.");
            return;
        }

        var net = NetworkManager.Instance;
        if (net == null)
        {
            Debug.LogWarning($"[LaundryGrab] No NetworkManager instance. Cannot broadcast '{action}' on '{networkInteractable.NetworkId}'.");
            return;
        }

        Debug.Log($"[LaundryGrab] Broadcasting '{action}' on '{networkInteractable.NetworkId}'");
        net.SendPlayerAction(networkInteractable.NetworkId, action);
    }
}
