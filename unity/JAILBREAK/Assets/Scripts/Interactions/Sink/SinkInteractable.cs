using System.Collections;
using Jailbreak.Network;
using UnityEngine;

/// <summary>
/// The cafeteria sink. When a prisoner carrying a food plate presses E next
/// to it, they begin a progress interaction. Once completed, they drop the plate:
/// the held prop is destroyed and the player returns to non-carrying animations.
///
/// Mirrors <see cref="FoodCounterInteractable"/> — inverse gating: this
/// interactable is only available while the local player IS carrying.
///
/// Broadcasts via <see cref="NetworkInteractable"/> so remote clients
/// replay the put-down animation on this player's avatar.
/// </summary>
[RequireComponent(typeof(ProgressPointAction))]
[RequireComponent(typeof(NetworkInteractable))]
public class SinkInteractable : MonoBehaviour, IInteractable
{
    [Header("UI")]
    public ProgressBar progressBar;
    public string progressLabel = "Leaving food...";

    [System.NonSerialized] public bool isOccupied;

    public KeyCode InteractKey => KeyCode.E;
    public string ActionLabel => isActive ? "Stop" : "Leave food";
    public int Priority => 10;
    public Transform Transform => transform;
    public string[] AllowedInStates => new[] { ActiveState };

    public bool CanInteract
    {
        get
        {
            if (isActive) return true;
            var carry = FindLocalCarryFood();
            if (carry == null) return false;
            return carry.IsCarrying;
        }
    }

    public const string ActionStartLeaveFood = "startLeaveFood";
    public const string ActionStopLeaveFood = "stopLeaveFood";
    public const string ActionLeaveFood = "leaveFood";

    private bool isActive;
    private ProgressPointAction progressPointAction;
    private NetworkInteractable networkInteractable;
    private CarryFoodInteraction cachedLocalCarry;
    
    const string ActiveState = "LeavingFood";
    
    void Awake()
    {
        progressPointAction = GetComponent<ProgressPointAction>();
        networkInteractable = GetComponent<NetworkInteractable>();
    }

    public void OnInteract(Collider source)
    {
        var root = source.transform.root;
        var animator = root.GetComponentInChildren<Animator>();
        var cc = root.GetComponentInChildren<CharacterController>();
        var manager = root.GetComponentInChildren<InteractionManager>();
        var carry = root.GetComponentInChildren<CarryFoodInteraction>();

        if (animator == null) return;
        
        if (!isActive)
        {
            if (carry == null || !carry.IsCarrying) return;
        }

        if (!isActive)
            StartCoroutine(PlayerRoutine(animator, cc, manager, root, carry));
        else
            progressPointAction.Stop();
    }

    IEnumerator PlayerRoutine(Animator animator, CharacterController cc, InteractionManager manager, Transform player, CarryFoodInteraction carry)
    {
        isActive = true;
        cc.enabled = false;
        manager.PushState(ActiveState);
        progressBar?.Show(progressLabel, progressPointAction.ProgressAction.progress);
        
        Broadcast(ActionStartLeaveFood);

        yield return progressPointAction.Execute(animator, player, onStop: () =>
        {
            bool completed = progressPointAction.ProgressAction.IsComplete;
            isActive = false;
            cc.enabled = true;
            manager.PopState(ActiveState);
            progressBar?.Hide();

            if (completed)
            {
                Broadcast(ActionLeaveFood);
                if (carry != null && carry.IsCarrying)
                {
                    carry.SuppressSync = true;
                    // We don't want a drop animation after a progress bar loop!
                    carry.ForceReset();
                    StartCoroutine(DelayedUnsuppressSync(carry));
                }
                progressPointAction.ProgressAction.progress = 0f;
            }
            else
            {
                Broadcast(ActionStopLeaveFood);
            }
        });
    }

    void Update()
    {
        if (!isActive) return;
        progressBar?.UpdateProgress(progressPointAction.ProgressAction.progress);
    }
    
    public void ApplyRemote(Transform remoteRoot, CarryFoodInteraction carry, string action)
    {
        if (remoteRoot == null) return;
        var animator = remoteRoot.GetComponentInChildren<Animator>();
        if (animator == null) return;

        string boolName = progressPointAction.ProgressAction.animatorBoolName;
        var sync = remoteRoot.GetComponent<Jailbreak.Player.RemotePlayerSync>();

        if (action == ActionStartLeaveFood)
        {
            if (progressPointAction.actionPoint != null)
            {
                remoteRoot.position = new Vector3(progressPointAction.actionPoint.position.x, remoteRoot.position.y, progressPointAction.actionPoint.position.z);
                remoteRoot.rotation = progressPointAction.actionPoint.rotation;
            }
            if (sync != null) sync.enabled = false;
            if (!string.IsNullOrEmpty(boolName)) animator.SetBool(boolName, true);
        }
        else if (action == ActionStopLeaveFood)
        {
            if (!string.IsNullOrEmpty(boolName)) animator.SetBool(boolName, false);
            if (sync != null) sync.enabled = true;
        }
        else if (action == ActionLeaveFood)
        {
            if (!string.IsNullOrEmpty(boolName)) animator.SetBool(boolName, false);
            if (sync != null) sync.enabled = true;
            if (carry != null && carry.IsCarrying) carry.ForceReset();
        }
    }

    IEnumerator DelayedUnsuppressSync(CarryFoodInteraction carry)
    {
        yield return new WaitForSeconds(0.5f);
        if (carry != null) carry.SuppressSync = false;
    }

    CarryFoodInteraction FindLocalCarryFood()
    {
        if (cachedLocalCarry != null) return cachedLocalCarry;

#if UNITY_2023_1_OR_NEWER
        var all = Object.FindObjectsByType<CarryFoodInteraction>(FindObjectsSortMode.None);
#else
        var all = Object.FindObjectsOfType<CarryFoodInteraction>();
#endif
        foreach (var c in all)
        {
            if (c.GetComponentInChildren<InteractionManager>() != null || c.transform.root.GetComponentInChildren<InteractionManager>() != null)
            {
                cachedLocalCarry = c;
                return c;
            }
        }
        return null;
    }

    void Broadcast(string action)
    {
        if (networkInteractable == null)
        {
            Debug.LogWarning($"[Sink] No NetworkInteractable on {gameObject.name}. Cannot broadcast '{action}'.");
            return;
        }

        var net = NetworkManager.Instance;
        if (net == null)
        {
            Debug.LogWarning($"[Sink] No NetworkManager instance. Cannot broadcast '{action}' on '{networkInteractable.NetworkId}'.");
            return;
        }

        Debug.Log($"[Sink] Broadcasting '{action}' on '{networkInteractable.NetworkId}'");
        net.SendPlayerAction(networkInteractable.NetworkId, action);
    }
}
