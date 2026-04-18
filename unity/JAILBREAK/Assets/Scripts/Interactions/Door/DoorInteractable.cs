using System.Collections;
using UnityEngine;

[RequireComponent(typeof(DoorAction))]
public class DoorInteractable : MonoBehaviour, IInteractable
{
    [Header("UI")]
    public FadeScreen fade;

    [Header("Timing")]
    public float holdDuration = 0.2f;

    public KeyCode InteractKey      => KeyCode.E;
    public string ActionLabel       => "Open";
    public int Priority             => 10;
    public Transform Transform      => transform;
    public bool CanInteract         => !isInUse;
    public string[] AllowedInStates => null;

    private bool isInUse;
    private DoorAction doorAction;

    void Awake()
    {
        doorAction = GetComponent<DoorAction>();
    }

    public void OnInteract(Collider source)
    {
        if (isInUse) return;

        var root     = source.transform.root;
        var animator = root.GetComponentInChildren<Animator>();
        var cc       = root.GetComponent<CharacterController>();
        var manager  = root.GetComponent<InteractionManager>();

        if (animator == null || fade == null || doorAction.destination == null) return;

        StartCoroutine(PlayerOpenRoutine(animator, cc, manager, root));
    }

    IEnumerator PlayerOpenRoutine(Animator animator, CharacterController cc, InteractionManager manager, Transform player)
    {
        isInUse    = true;
        cc.enabled = false;

        var interactionLock = new InteractionLock(manager);
        interactionLock.Acquire();

        yield return doorAction.Execute(
            animator,
            player,
            onFade:       (hold, callback) => fade.FadeToBlackAndBack(hold, callback),
            holdDuration: holdDuration
        );

        yield return new WaitForSeconds(fade.fadeOutDuration);

        cc.enabled = true;
        interactionLock.Release();
        isInUse = false;
    }
}