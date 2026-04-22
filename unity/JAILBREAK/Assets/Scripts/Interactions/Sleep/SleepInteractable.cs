using System.Collections;
using UnityEngine;

[RequireComponent(typeof(SleepAction))]
public class SleepInteractable : MonoBehaviour, IInteractable
{
    [Header("UI")]
    public FadeScreen fade;

    [Header("Timing")]
    [Tooltip("How long the screen stays black while sleeping")]
    public float holdDuration = 2f;

    public KeyCode InteractKey      => KeyCode.E;
    public string ActionLabel       => "Sleep";
    public int Priority             => 10;
    public Transform Transform      => transform;
    public bool CanInteract         => !isInUse;
    public string[] AllowedInStates => null;

    private bool isInUse;
    private SleepAction sleepAction;

    void Awake()
    {
        sleepAction = GetComponent<SleepAction>();
    }

    public void OnInteract(Collider source)
    {
        if (isInUse) return;

        var root     = source.transform.root;
        var animator = root.GetComponentInChildren<Animator>();
        var cc       = root.GetComponent<CharacterController>();
        var manager  = root.GetComponent<InteractionManager>();

        if (animator == null || fade == null) return;

        StartCoroutine(PlayerSleepRoutine(animator, cc, manager, root));
    }

    IEnumerator PlayerSleepRoutine(Animator animator, CharacterController cc, InteractionManager manager, Transform player)
    {
        isInUse    = true;
        cc.enabled = false;

        var interactionLock = new InteractionLock(manager);
        interactionLock.Acquire();

        yield return sleepAction.Execute(
            animator,
            player,
            onFade:      (hold, callback) => fade.FadeToBlackAndBack(hold, callback),
            holdDuration: holdDuration
        );

        yield return new WaitForSeconds(fade.fadeOutDuration);

        cc.enabled = true;
        interactionLock.Release();
        isInUse = false;
    }
}