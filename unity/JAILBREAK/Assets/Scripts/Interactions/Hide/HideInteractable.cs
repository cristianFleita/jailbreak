using System.Collections.Generic;
using Jailbreak.Network;
using Jailbreak.Player;
using UnityEngine;

/// <summary>
/// Manual-toggle hiding spot for prisoners (e.g. laundry cart). Local prisoner
/// presses E to climb in; either presses E again, or the duration timer
/// expires, to come out.
///
/// Mirrors the authoring pattern of <see cref="SleepInteractable"/>:
/// snap-to-hidePoint + looping animator bool, with start/stop broadcast via
/// <see cref="NetworkInteractable"/> so remote clients replay the conceal on
/// this avatar via <see cref="ApplyRemote"/>.
///
/// Timing:
///   • <see cref="hideDurationSeconds"/> — auto-exit after this many seconds.
///   • <see cref="cooldownSeconds"/> — after exit, the local prisoner can't
///     hide in ANY laundry cart for this many seconds. Cooldown is a static
///     timestamp so it follows the player across cart instances.
///   • A <see cref="ProgressBar"/> (optional) displays remaining hide time
///     (1 → 0). Local-only feedback; remotes just see the avatar conceal.
///
/// While hidden:
///   • The local <see cref="CharacterController"/> is disabled (no movement).
///   • The avatar's renderers are turned off so the prisoner is fully
///     concealed (skinned + mesh, locally and on every remote).
///   • <see cref="InteractionManager"/> is gated to the Hiding state so other
///     nearby interactables can't be triggered. The cart itself stays usable
///     in this state so the player can press E again to exit early.
///
/// Role gating: prisoners only. Guards never get the prompt.
/// </summary>
[RequireComponent(typeof(HideAction))]
[RequireComponent(typeof(NetworkInteractable))]
public class HideInteractable : MonoBehaviour, IInteractable
{
    [Header("Timing")]
    [Tooltip("Maximum time the prisoner can remain hidden before being auto-ejected.")]
    public float hideDurationSeconds = 10f;

    [Tooltip("After exit (auto or manual), how long the local prisoner is locked out of every laundry cart.")]
    public float cooldownSeconds = 10f;

    [Header("UI (optional)")]
    [Tooltip("Drag the world/scene ProgressBar that should display remaining hide time. Leave null to skip the bar.")]
    public ProgressBar progressBar;
    public string hidingLabel    = "Hiding...";
    public string cooldownLabel  = "Cooldown";

    public KeyCode InteractKey      => KeyCode.E;
    public string ActionLabel
    {
        get
        {
            if (isActive) return "Come out";
            if (IsLocalCoolingDown) return $"Cooldown {Mathf.CeilToInt(LocalCooldownRemaining)}s";
            return "Hide";
        }
    }
    public int Priority             => 10;
    public Transform Transform      => transform;
    public bool CanInteract         => isActive
                                       || (!isOccupied && IsLocalRoleAllowed() && !IsLocalCoolingDown);
    public string[] AllowedInStates => new[] { ActiveState };

    public const string ActionStartHide = "startHide";
    public const string ActionStopHide  = "stopHide";

    const string ActiveState = "Hiding";

    /// <summary>
    /// Cross-cart cooldown timestamp for the LOCAL player. Static so all
    /// laundry cart instances share the same lockout window. Networked games:
    /// each client tracks its own clock; the server isn't authoritative on
    /// this gameplay-side gate (matches the rest of the local-driven
    /// interaction pattern).
    /// </summary>
    private static float LocalNextHideAllowedTime;
    static bool IsLocalCoolingDown        => Time.time < LocalNextHideAllowedTime;
    static float LocalCooldownRemaining   => Mathf.Max(0f, LocalNextHideAllowedTime - Time.time);

    /// <summary>
    /// Runtime reservation flag. Set to true by the prisoner currently inside
    /// this hiding spot so a second prisoner can't pile in.
    /// </summary>
    [System.NonSerialized] public bool isOccupied;

    private HideAction hideAction;
    private NetworkInteractable networkInteractable;

    private bool isActive;
    private float hideEndTime;
    private bool localOwned;
    private Transform localPlayer;
    private Animator localAnimator;
    private CharacterController localController;
    private InteractionManager localManager;
    private Vector3 localExitPosition;
    private Dictionary<Renderer, bool> localRendererSnapshot;
    private Dictionary<Renderer, bool> remoteRendererSnapshot;
    private bool cooldownLabelVisible;

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
        {
            if (IsLocalCoolingDown) return; // double-checked in case CanInteract was bypassed
            StartHide(root, animator, cc, manager);
        }
        else if (localOwned && root == localPlayer)
        {
            StopHide(autoExit: false);
        }
    }

    void Update()
    {
        if (isActive && localOwned)
        {
            float remaining   = Mathf.Max(0f, hideEndTime - Time.time);
            float normalized  = hideDurationSeconds > 0f ? remaining / hideDurationSeconds : 0f;

            progressBar?.UpdateProgress(normalized);

            if (remaining <= 0f)
            {
                StopHide(autoExit: true);
                return;
            }
        }
        else if (cooldownLabelVisible && progressBar != null)
        {
            if (!IsLocalCoolingDown)
            {
                progressBar.Hide();
                cooldownLabelVisible = false;
            }
            else
            {
                float normalized = cooldownSeconds > 0f
                    ? 1f - (LocalCooldownRemaining / cooldownSeconds)
                    : 1f;
                progressBar.UpdateProgress(Mathf.Clamp01(normalized));
            }
        }
    }

    void StartHide(Transform player, Animator animator, CharacterController cc, InteractionManager manager)
    {
        isActive          = true;
        isOccupied        = true;
        localOwned        = true;
        localPlayer       = player;
        localAnimator     = animator;
        localController   = cc;
        localManager      = manager;
        localExitPosition = player.position;
        hideEndTime       = Time.time + hideDurationSeconds;

        if (cc != null) cc.enabled = false;
        if (manager != null) manager.PushState(ActiveState);

        hideAction.BeginHide(animator, player);
        HideAction.SetAvatarVisible(player, false, ref localRendererSnapshot);

        progressBar?.Show(hidingLabel, 1f);
        cooldownLabelVisible = false;

        Broadcast(ActionStartHide);
    }

    void StopHide(bool autoExit)
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

        // Start the cross-cart cooldown for the local player.
        LocalNextHideAllowedTime = Time.time + cooldownSeconds;

        // Swap the bar from "remaining hide" to "cooldown filling" so the
        // player gets visible feedback while they wait. Only meaningful while
        // they're still standing next to this cart — InteractionManager will
        // hide the prompt on its own when they walk away.
        if (progressBar != null && cooldownSeconds > 0f)
        {
            progressBar.Show(cooldownLabel, 0f);
            cooldownLabelVisible = true;
        }
        else
        {
            progressBar?.Hide();
        }

        isActive        = false;
        isOccupied      = false;
        localOwned      = false;
        localPlayer     = null;
        localAnimator   = null;
        localController = null;
        localManager    = null;

        if (autoExit) Debug.Log("[Hide] Auto-exit: hide duration elapsed.");
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
            isActive   = true;
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
            isActive   = false;
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
