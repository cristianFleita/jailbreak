using System.Collections;
using UnityEngine;

[RequireComponent(typeof(ProgressPointAction))]
public class SearchInteractable : MonoBehaviour, IInteractable
{
    [Header("UI")]
    public ProgressBar progressBar;
    public string progressLabel = "Searching...";

    public KeyCode InteractKey      => KeyCode.E;
    public string ActionLabel       => isActive ? "Stop" : "Search";
    public int Priority             => 10;
    public Transform Transform      => transform;
    public bool CanInteract         => true;
    public string[] AllowedInStates => new[] { "Searching" };

    private bool isActive;
    private ProgressPointAction progressPointAction;

    const string ActiveState = "Searching";

    void Awake()
    {
        progressPointAction = GetComponent<ProgressPointAction>();
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

        yield return progressPointAction.Execute(animator, player, onStop: () =>
        {
            isActive   = false;
            cc.enabled = true;
            manager.PopState(ActiveState);
            progressBar?.Hide();
        });
    }

    void Update()
    {
        if (!isActive) return;
        progressBar?.UpdateProgress(progressPointAction.ProgressAction.progress);
    }
}