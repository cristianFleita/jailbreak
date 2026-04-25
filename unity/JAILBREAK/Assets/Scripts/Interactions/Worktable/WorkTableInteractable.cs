using System.Collections;
using Jailbreak.Network;
using UnityEngine;

/// <summary>
/// Progress-based work station. Local player presses E to start a looping
/// work animation; presses E again (or lets progress hit 100%) to stop.
///
/// Broadcasts via <see cref="NetworkInteractable"/> so remote clients mirror
/// the snap-to-actionPoint + looping animator bool on this player's avatar.
/// Pure animation relay — no server-side state (matches sit/stand).
/// </summary>
[RequireComponent(typeof(ProgressPointAction))]
[RequireComponent(typeof(NetworkInteractable))]
public class WorkTableInteractable : MonoBehaviour, IInteractable
{
    [Header("UI")]
    public ProgressBar progressBar;
    public string progressLabel = "Working...";

    /// <summary>
    /// Runtime reservation flag. Set to true by the actor that's currently
    /// walking to / using this desk so another actor picks a different one.
    /// Player-flow and NPC-flow both respect it (see <see cref="Jailbreak.NPC.ZoneRegistry.GetDeterministicWorkTable"/>).
    /// </summary>
    [System.NonSerialized] public bool isOccupied;

    public KeyCode InteractKey      => KeyCode.E;
    public string ActionLabel       => isActive ? "Stop" : "Work";
    public int Priority             => 10;
    public Transform Transform      => transform;
    public bool CanInteract         => true;
    public string[] AllowedInStates => new[] { "Working" };

    public const string ActionStartWork = "startWork";
    public const string ActionStopWork  = "stopWork";

    private bool isActive;
    private ProgressPointAction progressPointAction;
    private NetworkInteractable networkInteractable;

    const string ActiveState = "Working";

    void Awake()
    {
        progressPointAction = GetComponent<ProgressPointAction>();
        networkInteractable = GetComponent<NetworkInteractable>();
    }

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

        Broadcast(ActionStartWork);

        yield return progressPointAction.Execute(animator, player, onStop: () =>
        {
            isActive   = false;
            cc.enabled = true;
            manager.PopState(ActiveState);
            progressBar?.Hide();
            Broadcast(ActionStopWork);
        });
    }

    void Update()
    {
        if (!isActive) return;
        progressBar?.UpdateProgress(progressPointAction.ProgressAction.progress);
    }

    /// <summary>
    /// Replays start/stop on a remote avatar (called by RemoteInteractionHandler).
    /// Snaps position to the action point and toggles the looping work animator bool;
    /// disables RemotePlayerSync while working so interpolation doesn't fight the snap.
    /// </summary>
    public void ApplyRemote(Transform remoteRoot, string action)
    {
        if (remoteRoot == null) return;

        var animator = remoteRoot.GetComponentInChildren<Animator>();
        if (animator == null) return;

        string boolName = progressPointAction.ProgressAction.animatorBoolName;
        var sync = remoteRoot.GetComponent<Jailbreak.Player.RemotePlayerSync>();

        if (action == ActionStartWork)
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
        else if (action == ActionStopWork)
        {
            if (!string.IsNullOrEmpty(boolName))
                animator.SetBool(boolName, false);

            if (sync != null) sync.enabled = true;
        }
    }

    void Broadcast(string action)
    {
        if (networkInteractable == null)
        {
            Debug.LogWarning($"[WorkTable] No NetworkInteractable on {gameObject.name}. Cannot broadcast '{action}'.");
            return;
        }

        var net = NetworkManager.Instance;
        if (net == null)
        {
            Debug.LogWarning($"[WorkTable] No NetworkManager instance. Cannot broadcast '{action}' on '{networkInteractable.NetworkId}'.");
            return;
        }

        net.SendPlayerAction(networkInteractable.NetworkId, action);
    }
}
