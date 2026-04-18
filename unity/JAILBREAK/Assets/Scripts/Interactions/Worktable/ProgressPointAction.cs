using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(ProgressAction))]
public class ProgressPointAction : MonoBehaviour
{
    [Header("Position")]
    [Tooltip("Where the character stands during the action. Optional.")]
    public Transform actionPoint;

    private ProgressAction progressAction;

    void Awake()
    {
        progressAction = GetComponent<ProgressAction>();
    }

    public ProgressAction ProgressAction => progressAction;

    public IEnumerator Execute(Animator animator, Transform actor, Action onStop = null)
    {
        if (actionPoint != null)
        {
            actor.position = new Vector3(actionPoint.position.x, actor.position.y, actionPoint.position.z);
            actor.rotation = actionPoint.rotation;
        }

        yield return progressAction.Execute(animator, onStop);
    }

    public void Stop()
    {
        progressAction.Stop();
    }
}