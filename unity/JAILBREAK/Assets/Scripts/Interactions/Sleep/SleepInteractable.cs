using Jailbreak.Network;
using Jailbreak.Player;
using UnityEngine;

/// <summary>
/// Manual-toggle sleep station. Local player presses E to lie down, presses E
/// again to wake up.
///
/// Mirrors the authoring pattern of <see cref="WorkInspectCabinetsInteractable"/>:
/// snap-to-actionPoint + looping animator bool, with start/stop broadcast via
/// <see cref="NetworkInteractable"/> so remote clients replay the pose on this
/// avatar via <see cref="ApplyRemote"/>. Unlike WorkInspectCabinets there is
/// no progress bar and no auto-stop — the player chooses when to start and
/// when to stop.
///
/// While sleeping, the local CharacterController is disabled (no movement) and
/// the InteractionManager is gated to this state so other nearby interactables
/// can't be triggered. Pressing E again wakes the player, ground-snaps them
/// off the mattress, and re-enables locomotion.
/// </summary>
[RequireComponent(typeof(SleepAction))]
[RequireComponent(typeof(NetworkInteractable))]
public class SleepInteractable : MonoBehaviour, IInteractable
{
    public KeyCode InteractKey      => KeyCode.E;
    public string ActionLabel       => isActive ? "Wake up" : "Sleep";
    public int Priority             => 10;
    public Transform Transform      => transform;
    public bool CanInteract         => isActive || !isOccupied;
    public string[] AllowedInStates => new[] { ActiveState };

    public const string ActionStartSleep = "startSleep";
    public const string ActionStopSleep  = "stopSleep";

    const string ActiveState = "Sleeping";

    /// <summary>
    /// Runtime reservation flag. Set to true by the actor (NPC or local player)
    /// currently walking to / sleeping in this bed so another actor picks a
    /// different one. Mirrors <see cref="WorkInspectCabinetsInteractable.isOccupied"/>.
    /// </summary>
    [System.NonSerialized] public bool isOccupied;

    private SleepAction sleepAction;
    private NetworkInteractable networkInteractable;

    private bool isActive;
    private Transform localPlayer;
    private Animator localAnimator;
    private CharacterController localController;
    private InteractionManager localManager;

    void Awake()
    {
        sleepAction         = GetComponent<SleepAction>();
        networkInteractable = GetComponent<NetworkInteractable>();
    }

    // ─── Local interaction entry point ───────────────────────────────────────

    public void OnInteract(Collider source)
    {
        var root     = source.transform.root;
        var animator = root.GetComponentInChildren<Animator>();
        var cc       = root.GetComponent<CharacterController>();
        var manager  = root.GetComponent<InteractionManager>();

        if (animator == null) return;

        if (!isActive)
            StartSleep(root, animator, cc, manager);
        else if (root == localPlayer)
            StopSleep();
    }

    void StartSleep(Transform player, Animator animator, CharacterController cc, InteractionManager manager)
    {
        isActive        = true;
        isOccupied      = true;
        localPlayer     = player;
        localAnimator   = animator;
        localController = cc;
        localManager    = manager;

        if (cc != null) cc.enabled = false;
        if (manager != null) manager.PushState(ActiveState);

        sleepAction.BeginSleep(animator, player);

        Broadcast(ActionStartSleep);
    }

    void StopSleep()
    {
        sleepAction.EndSleep(localAnimator);

        if (localPlayer != null)
            localPlayer.position = SleepAction.ResolveGroundPosition(localPlayer.position);

        if (localController != null) localController.enabled = true;
        if (localManager != null)    localManager.PopState(ActiveState);

        Broadcast(ActionStopSleep);

        isActive        = false;
        isOccupied      = false;
        localPlayer     = null;
        localAnimator   = null;
        localController = null;
        localManager    = null;
    }

    // ─── Remote replay ───────────────────────────────────────────────────────

    /// <summary>
    /// Replays start/stop on a remote avatar (called by RemoteInteractionHandler).
    /// Snaps the remote to the sleep point and toggles the looping animator bool;
    /// disables RemotePlayerSync while sleeping so interpolation doesn't fight
    /// the snap, and re-enables it on wake.
    /// </summary>
    public void ApplyRemote(Transform remoteRoot, string action)
    {
        if (remoteRoot == null) return;

        var animator = remoteRoot.GetComponentInChildren<Animator>();
        if (animator == null) return;

        var sync = remoteRoot.GetComponent<RemotePlayerSync>();

        if (action == ActionStartSleep)
        {
            isOccupied = true;
            if (sync != null) sync.enabled = false;
            sleepAction.BeginSleep(animator, remoteRoot);
        }
        else if (action == ActionStopSleep)
        {
            sleepAction.EndSleep(animator);
            remoteRoot.position = SleepAction.ResolveGroundPosition(remoteRoot.position);
            if (sync != null) sync.enabled = true;
            isOccupied = false;
        }
    }

    // ─── Network ─────────────────────────────────────────────────────────────

    void Broadcast(string action)
    {
        if (networkInteractable == null)
        {
            Debug.LogWarning($"[Sleep] No NetworkInteractable on {gameObject.name}. Cannot broadcast '{action}'.");
            return;
        }

        var net = NetworkManager.Instance;
        if (net == null)
        {
            Debug.LogWarning($"[Sleep] No NetworkManager. Cannot broadcast '{action}' on '{networkInteractable.NetworkId}'.");
            return;
        }

        Debug.Log($"[Sleep] Broadcasting '{action}' on '{networkInteractable.NetworkId}'");
        net.SendPlayerAction(networkInteractable.NetworkId, action);
    }
}
