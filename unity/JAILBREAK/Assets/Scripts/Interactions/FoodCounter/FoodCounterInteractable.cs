using System.Collections;
using Jailbreak.Network;
using UnityEngine;

/// <summary>
/// The cafeteria counter. When a prisoner (local player) presses E next to it,
/// they pick up a food plate through a progress bar interaction.
///
/// Guards cannot interact. Prisoners who are already carrying cannot pick up
/// another plate (CanInteract returns false).
///
/// Broadcasts via <see cref="NetworkInteractable"/> so remote clients replay
/// the progress bar and plate-attach on this player's avatar.
/// </summary>
[RequireComponent(typeof(ProgressPointAction))]
[RequireComponent(typeof(NetworkInteractable))]
public class FoodCounterInteractable : MonoBehaviour, IInteractable
{
    [Header("UI")]
    public ProgressBar progressBar;
    public string progressLabel = "Taking food...";

    [System.NonSerialized] public bool isOccupied;

    public KeyCode InteractKey => KeyCode.E;
    public string ActionLabel => isActive ? "Stop" : "Take food";
    public int Priority => 10;
    public Transform Transform => transform;
    public string[] AllowedInStates => new[] { ActiveState };

    public bool CanInteract
    {
        get
        {
            if (isActive) return true;
            var carry = FindLocalCarryFood();
            if (carry == null) return true; // unknown — let InteractionManager attempt
            return !carry.IsCarrying;
        }
    }

    public const string ActionStartTakeFood = "startTakeFood";
    public const string ActionStopTakeFood = "stopTakeFood";
    public const string ActionTakeFood = "takeFood";

    private bool isActive;
    private ProgressPointAction progressPointAction;
    private NetworkInteractable networkInteractable;
    private CarryFoodInteraction cachedLocalCarry;
    
    const string ActiveState = "TakingFood";

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
            if (carry != null && carry.IsCarrying) return;
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
        Broadcast(ActionStartTakeFood);

        yield return progressPointAction.Execute(animator, player, onStop: () =>
        {
            bool completed = progressPointAction.ProgressAction.IsComplete;
            isActive = false;
            cc.enabled = true;
            manager.PopState(ActiveState);
            progressBar?.Hide();

            if (completed)
            {
                Broadcast(ActionTakeFood);
                if (carry != null && !carry.IsCarrying)
                {
                    // For pick up, let the animation play if desired, or we can just attach it immediately.
                    // TryPickUp will trigger the animation or pop it in hand.
                    carry.TryPickUp();
                }
                progressPointAction.ProgressAction.progress = 0f;
            }
            else
            {
                Broadcast(ActionStopTakeFood);
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

        if (action == ActionStartTakeFood)
        {
            if (progressPointAction.actionPoint != null)
            {
                remoteRoot.position = new Vector3(progressPointAction.actionPoint.position.x, remoteRoot.position.y, progressPointAction.actionPoint.position.z);
                remoteRoot.rotation = progressPointAction.actionPoint.rotation;
            }
            if (sync != null) sync.enabled = false;
            if (!string.IsNullOrEmpty(boolName)) animator.SetBool(boolName, true);
        }
        else if (action == ActionStopTakeFood)
        {
            if (!string.IsNullOrEmpty(boolName)) animator.SetBool(boolName, false);
            if (sync != null) sync.enabled = true;
        }
        else if (action == ActionTakeFood)
        {
            if (!string.IsNullOrEmpty(boolName)) animator.SetBool(boolName, false);
            if (sync != null) sync.enabled = true;
            if (carry != null && !carry.IsCarrying) carry.TryPickUp();
        }
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
            Debug.LogWarning($"[FoodCounter] No NetworkInteractable on {gameObject.name}. Cannot broadcast '{action}'.");
            return;
        }

        var net = NetworkManager.Instance;
        if (net == null)
        {
            Debug.LogWarning($"[FoodCounter] No NetworkManager instance. Cannot broadcast '{action}' on '{networkInteractable.NetworkId}'.");
            return;
        }

        Debug.Log($"[FoodCounter] Broadcasting '{action}' on '{networkInteractable.NetworkId}'");
        net.SendPlayerAction(networkInteractable.NetworkId, action);
    }
}
