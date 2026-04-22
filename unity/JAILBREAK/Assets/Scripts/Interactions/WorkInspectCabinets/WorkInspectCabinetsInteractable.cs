using System.Collections;
using Jailbreak.Network;
using UnityEngine;

/// <summary>
/// Progress-based cabinet inspection station. Local player presses E to start
/// a looping inspect animation; presses E again (or lets progress hit 100%) to stop.
///
/// Mirrors the authoring pattern of <see cref="WorkTableInteractable"/>:
/// pure animation relay driven by a <see cref="ProgressPointAction"/>, with
/// start/stop broadcast via <see cref="NetworkInteractable"/> so remote clients
/// replay the snap-to-actionPoint + looping animator bool on this avatar.
///
/// The animator bool (e.g. <c>isWorkingTable</c> to reuse the ButtonPushing
/// work animation, or <c>isSearching</c> for the Searching/Rummaging pose)
/// is configured on the prefab's <see cref="ProgressAction"/> component — the
/// script itself is animation-agnostic.
/// </summary>
[RequireComponent(typeof(ProgressPointAction))]
[RequireComponent(typeof(NetworkInteractable))]
public class WorkInspectCabinetsInteractable : MonoBehaviour, IInteractable
{
    [Header("UI")]
    public ProgressBar progressBar;
    public string progressLabel = "Inspecting...";

    /// <summary>
    /// Runtime reservation flag. Set to true by the actor that's currently
    /// walking to / using this cabinet so another actor picks a different one.
    /// Mirrors <see cref="WorkTableInteractable.isOccupied"/> so NPC flows can
    /// share the same de-duplication convention.
    /// </summary>
    [System.NonSerialized] public bool isOccupied;

    public KeyCode InteractKey      => KeyCode.E;
    public string ActionLabel       => isActive ? "Stop" : "Inspect";
    public int Priority             => 10;
    public Transform Transform      => transform;
    public bool CanInteract         => true;
    public string[] AllowedInStates => new[] { ActiveState };

    public const string ActionStartInspect = "startInspect";
    public const string ActionStopInspect  = "stopInspect";

    private bool isActive;
    private ProgressPointAction progressPointAction;
    private NetworkInteractable networkInteractable;

    const string ActiveState = "Inspecting";

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

        if (animator == null) return;

        if (!isActive)
            StartCoroutine(PlayerRoutine(animator, cc, manager, root));
        else
            progressPointAction.Stop();
    }

    IEnumerator PlayerRoutine(
        Animator animator,
        CharacterController cc,
        InteractionManager manager,
        Transform player)
    {
        isActive   = true;
        cc.enabled = false;

        manager.PushState(ActiveState);

        progressBar?.Show(progressLabel, progressPointAction.ProgressAction.progress);

        Broadcast(ActionStartInspect);

        yield return progressPointAction.Execute(animator, player, onStop: () =>
        {
            isActive   = false;
            cc.enabled = true;
            manager.PopState(ActiveState);
            progressBar?.Hide();
            Broadcast(ActionStopInspect);
        });
    }

    void Update()
    {
        if (!isActive) return;
        progressBar?.UpdateProgress(progressPointAction.ProgressAction.progress);
    }

    /// <summary>
    /// Replays start/stop on a remote avatar (called by RemoteInteractionHandler).
    /// Snaps position to the action point and toggles the looping animator bool;
    /// disables RemotePlayerSync while inspecting so interpolation doesn't fight the snap.
    /// </summary>
    public void ApplyRemote(Transform remoteRoot, string action)
    {
        if (remoteRoot == null) return;

        var animator = remoteRoot.GetComponentInChildren<Animator>();
        if (animator == null) return;

        string boolName = progressPointAction.ProgressAction.animatorBoolName;
        var sync = remoteRoot.GetComponent<Jailbreak.Player.RemotePlayerSync>();

        if (action == ActionStartInspect)
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
        }
        else if (action == ActionStopInspect)
        {
            if (!string.IsNullOrEmpty(boolName))
                animator.SetBool(boolName, false);

            if (sync != null) sync.enabled = true;
        }
    }

    // ─── Network ─────────────────────────────────────────────────────────────

    void Broadcast(string action)
    {
        if (networkInteractable == null)
        {
            Debug.LogWarning($"[WorkInspectCabinets] No NetworkInteractable on {gameObject.name}. Cannot broadcast '{action}'.");
            return;
        }

        var net = NetworkManager.Instance;
        if (net == null)
        {
            Debug.LogWarning($"[WorkInspectCabinets] No NetworkManager instance. Cannot broadcast '{action}' on '{networkInteractable.NetworkId}'.");
            return;
        }

        Debug.Log($"[WorkInspectCabinets] Broadcasting '{action}' on '{networkInteractable.NetworkId}'");
        net.SendPlayerAction(networkInteractable.NetworkId, action);
    }
}
