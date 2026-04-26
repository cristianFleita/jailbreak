using System.Collections.Generic;
using Jailbreak.Network;
using Jailbreak.Player;
using UnityEngine;

/// <summary>
/// Manual-toggle hiding spot for prisoners (e.g. laundry cart). Local prisoner
/// presses E to climb in, presses E again to come out.
///
/// Mirrors the authoring pattern of <see cref="SleepInteractable"/>:
/// snap-to-hidePoint + looping animator bool, with start/stop broadcast via
/// <see cref="NetworkInteractable"/> so remote clients replay the conceal on
/// this avatar via <see cref="ApplyRemote"/>. Like Sleep there is no progress
/// bar and no auto-stop — the player chooses when to hide and when to exit.
///
/// While hidden:
///   • The local <see cref="CharacterController"/> is disabled (no movement).
///   • The avatar's renderers are turned off so the prisoner is fully
///     concealed (skinned + mesh, locally and on every remote).
///   • <see cref="InteractionManager"/> is gated to the Hiding state so other
///     nearby interactables can't be triggered. The cart itself stays usable
///     in this state so the player can press E again to exit.
///
/// Pressing E again lifts the prisoner out, ground-snaps off the cart, and
/// re-enables locomotion + renderers.
///
/// Role gating: prisoners only. Guards never get the prompt.
/// </summary>
[RequireComponent(typeof(HideAction))]
[RequireComponent(typeof(NetworkInteractable))]
public class HideInteractable : MonoBehaviour, IInteractable
{
    public KeyCode InteractKey      => KeyCode.E;
    public string ActionLabel       => isActive ? "Come out" : "Hide";
    public int Priority             => 10;
    public Transform Transform      => transform;
    public bool CanInteract         => isActive || (!isOccupied && IsLocalRoleAllowed());
    public string[] AllowedInStates => new[] { ActiveState };

    public const string ActionStartHide = "startHide";
    public const string ActionStopHide  = "stopHide";

    const string ActiveState = "Hiding";

    /// <summary>
    /// Runtime reservation flag. Set to true by the prisoner currently inside
    /// this hiding spot so a second prisoner can't pile in. Mirrors
    /// <see cref="SleepInteractable.isOccupied"/>.
    /// </summary>
    [System.NonSerialized] public bool isOccupied;

    private HideAction hideAction;
    private NetworkInteractable networkInteractable;

    private bool isActive;
    private Transform localPlayer;
    private Animator localAnimator;
    private CharacterController localController;
    private InteractionManager localManager;
    private Vector3 localExitPosition;
    private Dictionary<Renderer, bool> localRendererSnapshot;
    private Dictionary<Renderer, bool> remoteRendererSnapshot;

    void Awake()
    {
        hideAction          = GetComponent<HideAction>();
        networkInteractable = GetComponent<NetworkInteractable>();
    }

    // ─── Local interaction entry point ───────────────────────────────────────

    public void OnInteract(Collider source)
    {
        var root     = source.transform.root;
        var animator = root.GetComponentInChildren<Animator>();
        var cc       = root.GetComponent<CharacterController>();
        var manager  = root.GetComponent<InteractionManager>();

        if (!isActive)
            StartHide(root, animator, cc, manager);
        else if (root == localPlayer)
            StopHide();
    }

    void StartHide(Transform player, Animator animator, CharacterController cc, InteractionManager manager)
    {
        isActive          = true;
        isOccupied        = true;
        localPlayer       = player;
        localAnimator     = animator;
        localController   = cc;
        localManager      = manager;
        localExitPosition = player.position;

        if (cc != null) cc.enabled = false;
        if (manager != null) manager.PushState(ActiveState);

        hideAction.BeginHide(animator, player);
        HideAction.SetAvatarVisible(player, false, ref localRendererSnapshot);

        Broadcast(ActionStartHide);
    }

    void StopHide()
    {
        hideAction.EndHide(localAnimator);

        if (localPlayer != null)
        {
            Vector3 exit = hideAction.exitPoint != null ? hideAction.exitPoint.position : localExitPosition;
            localPlayer.position = HideAction.ResolveGroundPosition(exit);
            HideAction.SetAvatarVisible(localPlayer, true, ref localRendererSnapshot);
        }

        if (localController != null) localController.enabled = true;
        if (localManager != null)    localManager.PopState(ActiveState);

        Broadcast(ActionStopHide);

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
    /// Snaps the remote into the cart, toggles the looping animator bool, and
    /// hides their renderers; disables RemotePlayerSync while hidden so
    /// interpolation doesn't fight the snap, and re-enables it on exit.
    /// </summary>
    public void ApplyRemote(Transform remoteRoot, string action)
    {
        if (remoteRoot == null) return;

        var animator = remoteRoot.GetComponentInChildren<Animator>();
        var sync     = remoteRoot.GetComponent<RemotePlayerSync>();

        if (action == ActionStartHide)
        {
            isOccupied = true;
            if (sync != null) sync.enabled = false;
            hideAction.BeginHide(animator, remoteRoot);
            HideAction.SetAvatarVisible(remoteRoot, false, ref remoteRendererSnapshot);
        }
        else if (action == ActionStopHide)
        {
            hideAction.EndHide(animator);
            HideAction.SetAvatarVisible(remoteRoot, true, ref remoteRendererSnapshot);
            remoteRoot.position = HideAction.ResolveGroundPosition(remoteRoot.position);
            if (sync != null) sync.enabled = true;
            isOccupied = false;
        }
    }

    // ─── Role gating ─────────────────────────────────────────────────────────

    static bool IsLocalRoleAllowed()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || string.IsNullOrEmpty(gsm.LocalRole)) return true; // pre-spawn / lobby
        return gsm.LocalRole == "prisoner";
    }

    // ─── Network ─────────────────────────────────────────────────────────────

    void Broadcast(string action)
    {
        if (networkInteractable == null)
        {
            Debug.LogWarning($"[Hide] No NetworkInteractable on {gameObject.name}. Cannot broadcast '{action}'.");
            return;
        }

        var net = NetworkManager.Instance;
        if (net == null)
        {
            Debug.LogWarning($"[Hide] No NetworkManager. Cannot broadcast '{action}' on '{networkInteractable.NetworkId}'.");
            return;
        }

        Debug.Log($"[Hide] Broadcasting '{action}' on '{networkInteractable.NetworkId}'");
        net.SendPlayerAction(networkInteractable.NetworkId, action);
    }
}
